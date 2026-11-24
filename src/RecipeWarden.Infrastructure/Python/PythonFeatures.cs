using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Python;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Python;

/// <summary>
/// FR-OPS-06, D5: Python features are on unless the server owner (config <c>Python:Enabled</c>) or an Admin (setting
/// <c>Python.Enabled</c>) turns them off, or the self-test failed. Every adapter asks here first (BR-119).
/// </summary>
public sealed partial class PythonFeatures(PythonOptions options, PythonWorkerHealth health, SettingsStore settings, ILogger<PythonFeatures> logger)
{
    public const string SettingKey = "Python.Enabled";

    public async Task<bool> AdminEnabledAsync(CancellationToken cancellationToken = default) =>
        await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false) is not { } value
        || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    public Task SetAdminEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        settings.SetAsync(SettingKey, enabled ? null : "false", cancellationToken); // default on: only "off" is stored

    public bool ServerEnabled => options.Enabled;

    /// <summary>None when a call may go ahead; otherwise why not.</summary>
    public async Task<WorkerFailure> GateAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || !await AdminEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return WorkerFailure.TurnedOff;
        }

        return health.Snapshot.Status == PythonStatus.Unavailable ? WorkerFailure.Unavailable : WorkerFailure.None;
    }

    /// <summary>The state to show (FR-OPS-06): turned off wins over the last self-test.</summary>
    public async Task<PythonHealthSnapshot> StatusAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = health.Snapshot;
        return !options.Enabled || !await AdminEnabledAsync(cancellationToken).ConfigureAwait(false)
            ? snapshot with { Status = PythonStatus.TurnedOff }
            : snapshot;
    }

    /// <summary>BR-119: one log line per request that fell back; "turned off" is a choice, not a problem.</summary>
    public void LogFallback(string feature, WorkerFailure failure, string? detail)
    {
        if (failure == WorkerFailure.TurnedOff)
        {
            return;
        }

        LogFellBack(logger, feature, failure.ToString(), detail ?? string.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Python worker: {Feature} used the .NET behaviour ({Failure}: {Detail})")]
    private static partial void LogFellBack(ILogger logger, string feature, string failure, string detail);

}

/// <summary>FR-NUT-07 adapter: the matcher's USDA candidates for unmatched names.</summary>
public sealed class PythonFoodSuggestionSource(PythonFeatures features, PythonWorker worker) : IFoodSuggestionSource
{
    public const string Library = "ingredient-parser-nlp";

    public async Task<WorkerOutcome<IReadOnlyList<FoodCandidates>>> SuggestAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (await features.GateAsync(cancellationToken).ConfigureAwait(false) is not WorkerFailure.None and var gate)
        {
            return WorkerOutcome.Failed<IReadOnlyList<FoodCandidates>>(gate);
        }

        var call = await worker.SuggestAsync(names, cancellationToken).ConfigureAwait(false);
        var outcome = !call.Ok
            ? WorkerOutcome.Failed<IReadOnlyList<FoodCandidates>>(call.Failure, call.Detail)
            : WorkerContract.Suggestions(call.Result!, names) is { } list
                ? WorkerOutcome.Success(list, call.Engine!.Describe(Library))
                : WorkerOutcome.Failed<IReadOnlyList<FoodCandidates>>(WorkerFailure.Malformed, "suggestions did not match the names asked");
        if (!outcome.Ok)
        {
            features.LogFallback("food suggestions", outcome.Failure, outcome.Detail);
        }

        return outcome;
    }
}

/// <summary>FR-IMP-11 adapter: the NLP parser's reading of each imported line.</summary>
public sealed class PythonIngredientSecondOpinion(PythonFeatures features, PythonWorker worker) : IIngredientSecondOpinion
{
    public async Task<WorkerOutcome<IReadOnlyList<IngredientReading?>>> ReadAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (await features.GateAsync(cancellationToken).ConfigureAwait(false) is not WorkerFailure.None and var gate)
        {
            return WorkerOutcome.Failed<IReadOnlyList<IngredientReading?>>(gate);
        }

        var call = await worker.ParseAsync(lines, fdc: false, cancellationToken).ConfigureAwait(false);
        var outcome = !call.Ok
            ? WorkerOutcome.Failed<IReadOnlyList<IngredientReading?>>(call.Failure, call.Detail)
            : WorkerContract.Readings(call.Result!, lines.Count) is { } readings
                ? WorkerOutcome.Success(readings, call.Engine!.Describe(PythonFoodSuggestionSource.Library))
                : WorkerOutcome.Failed<IReadOnlyList<IngredientReading?>>(WorkerFailure.Malformed, "readings broke the contract");
        if (!outcome.Ok)
        {
            features.LogFallback("second opinion", outcome.Failure, outcome.Detail);
        }

        return outcome;
    }
}

/// <summary>AC-109, M10 adapter: recipe-scrapers on HTML the app already has (AC-112: it never fetches).</summary>
public sealed class PythonSiteRecipeScraper(PythonFeatures features, PythonWorker worker) : ISiteRecipeScraper
{
    public const string Library = "recipe-scrapers";

    public async Task<WorkerOutcome<SiteRecipeReading>> ScrapeAsync(string html, Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(url);
        if (await features.GateAsync(cancellationToken).ConfigureAwait(false) is not WorkerFailure.None and var gate)
        {
            return WorkerOutcome.Failed<SiteRecipeReading>(gate);
        }

        var call = await worker.ScrapeAsync(html, url, cancellationToken).ConfigureAwait(false);
        var outcome = !call.Ok
            ? WorkerOutcome.Failed<SiteRecipeReading>(call.Failure, call.Detail)
            : WorkerContract.Recipe(call.Result!) is { } recipe
                ? WorkerOutcome.Success(recipe, call.Engine!.Describe(Library))
                : WorkerOutcome.Failed<SiteRecipeReading>(WorkerFailure.Malformed, "recipe broke the contract");
        if (!outcome.Ok)
        {
            features.LogFallback("site reader", outcome.Failure, outcome.Detail);
        }

        return outcome;
    }
}
