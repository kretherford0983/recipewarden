namespace RecipeWarden.Core.Python;

/// <summary>Why a Python worker call gave no result (BR-119: the caller then uses the .NET behaviour).</summary>
public enum WorkerFailure
{
    None = 0,

    /// <summary>An Admin turned Python features off (FR-OPS-06, D5).</summary>
    TurnedOff,

    /// <summary>The runtime is missing or failed its self-test.</summary>
    Unavailable,

    /// <summary>The call ran past its time limit and was killed (SEC-92).</summary>
    Timeout,

    /// <summary>The request or the answer was over its size limit (SEC-92).</summary>
    TooLarge,

    /// <summary>The answer broke the contract (SEC-93).</summary>
    Malformed,

    /// <summary>The worker answered with an error envelope.</summary>
    WorkerError,

    /// <summary>The process could not start or ended without an answer.</summary>
    Crashed,
}

/// <summary>A worker call's result, or why there is none. <see cref="Engine"/> names the library versions (BR-116 cache).</summary>
public sealed record WorkerOutcome<T>(T? Value, WorkerFailure Failure, string? Detail = null, string? Engine = null)
    where T : class
{
    public bool Ok => Failure == WorkerFailure.None && Value is not null;
}

/// <summary>Factories for <see cref="WorkerOutcome{T}"/>.</summary>
public static class WorkerOutcome
{
    public static WorkerOutcome<T> Success<T>(T value, string engine)
        where T : class => new(value, WorkerFailure.None, null, engine);

    public static WorkerOutcome<T> Failed<T>(WorkerFailure failure, string? detail = null)
        where T : class => new(null, failure, detail);
}

/// <summary>A USDA FoodData Central food the matcher named (FR-NUT-07); untrusted until resolved locally (SEC-93).</summary>
public sealed record FdcCandidate(int FdcId, string DataType, string Description, decimal Confidence);

/// <summary>The NLP parser's reading of one ingredient line (FR-IMP-11, D4: a second opinion only).</summary>
public sealed record IngredientReading(decimal? Quantity, decimal? QuantityMax, string? Unit, IReadOnlyList<string> Names,
    string? Preparation, string? Comment, IReadOnlyList<FdcCandidate> Foods);

/// <summary>The matcher's candidates for one unmatched name.</summary>
public sealed record FoodCandidates(string Name, IReadOnlyList<FdcCandidate> Foods);

/// <summary>recipe-scrapers' plain-text reading of a page the app already fetched (M9 import-compare, M10).</summary>
public sealed record SiteRecipeReading(bool SupportedHost, bool Found, string? Title, IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Instructions, string? Yields, int? TotalMinutes, int? PrepMinutes, int? CookMinutes, string? Author,
    string? Description, string? ImageUrl, string? SiteName);

/// <summary>FR-NUT-07: USDA candidates for unmatched ingredient names (ADR 0051).</summary>
public interface IFoodSuggestionSource
{
    Task<WorkerOutcome<IReadOnlyList<FoodCandidates>>> SuggestAsync(IReadOnlyList<string> names, CancellationToken cancellationToken = default);
}

/// <summary>FR-IMP-11: the NLP parser's reading of each line, in order; null for a blank line.</summary>
public interface IIngredientSecondOpinion
{
    Task<WorkerOutcome<IReadOnlyList<IngredientReading?>>> ReadAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken = default);
}

/// <summary>AC-109 and M10: a site-specific reading of HTML that came from SafeFetcher or paste (AC-112).</summary>
public interface ISiteRecipeScraper
{
    Task<WorkerOutcome<SiteRecipeReading>> ScrapeAsync(string html, Uri url, CancellationToken cancellationToken = default);
}
