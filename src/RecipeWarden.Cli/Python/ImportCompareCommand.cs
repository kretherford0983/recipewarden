using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Import;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Import;

namespace RecipeWarden.Cli.Python;

/// <summary>
/// AC-109, D6: "recipewarden import-compare" lists each saved URL import read by the .NET extractor and by the site
/// reader, then a summary that says whether M10 (site readers in the import order, BR-117) would have helped.
/// Reads only; run it after a few weeks of real imports (they are kept 30 days).
/// </summary>
internal static class ImportCompareCommand
{
    public static async Task<int> RunAsync(IConfiguration config, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddRecipeWardenData(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<ImportCompareService>().RunAsync(cancellationToken);

        if (report.Failure != Core.Python.WorkerFailure.None)
        {
            await error.WriteLineAsync($"error: the site reader is not available ({report.Failure}: {report.Detail}). Try: recipewarden python-selftest");
            return 1;
        }

        if (report.Rows.Count == 0)
        {
            await output.WriteLineAsync($"No saved URL imports to compare ({report.Missing} purged or unreadable). Imports are kept 30 days.");
            return 0;
        }

        await output.WriteLineAsync("Verdict          .NET           Site reader      Ingredients  Steps   Address");
        foreach (var r in report.Rows)
        {
            var net = (r.DotNetMethod?.ToString() ?? "none") + (r.DotNetComplete ? "" : " (incomplete)");
            var site = !r.SupportedHost ? "no site reader" : r.SiteComplete ? "complete" : "incomplete";
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{r.Verdict,-16} {net,-14} {site,-16} {r.DotNetIngredients,4} / {r.SiteIngredients,-4} {r.DotNetSteps,3} / {r.SiteSteps,-3} {r.Url}"));
        }

        var helped = report.Rows.Count(r => r.Verdict == CompareVerdict.OnlySiteReader);
        var supported = report.Rows.Count(r => r.SupportedHost);
        await output.WriteLineAsync();
        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
            $"{report.Rows.Count} URL imports compared ({report.Missing} purged or unreadable); {supported} from hosts with a site reader."));
        await output.WriteLineAsync(helped == 0
            ? "The site reader would not have completed any import the .NET extractor missed: M10 would not help yet."
            : string.Create(CultureInfo.InvariantCulture,
                $"The site reader would have completed {helped} import{(helped == 1 ? "" : "s")} the .NET extractor missed: M10 would help."));
        return 0;
    }
}
