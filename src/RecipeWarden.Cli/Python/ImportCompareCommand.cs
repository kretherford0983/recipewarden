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
/// Reads only; run it after a few weeks of real imports (they are kept 30 days). With <c>--url</c> (owner, 2026-11-25) it
/// fetches the given pages instead, through the same checks as a URL import, and saves nothing.
/// </summary>
internal static class ImportCompareCommand
{
    public static Task<int> RunAsync(IConfiguration config, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
        RunAsync(config, output, error, [], cancellationToken);

    /// <param name="urls">Pages to fetch and compare; empty compares the saved URL imports.</param>
    public static async Task<int> RunAsync(IConfiguration config, TextWriter output, TextWriter error, IReadOnlyList<string> urls,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddRecipeWardenData(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var compare = scope.ServiceProvider.GetRequiredService<ImportCompareService>();
        if (urls.Count > 0)
        {
            return await CompareUrlsAsync(compare, urls, output, error, cancellationToken);
        }

        var report = await compare.RunAsync(cancellationToken);

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

        await WriteRowsAsync(output, report.Rows);

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

    /// <summary>"--url": each page fetched once and compared; a page that cannot be fetched is reported and the rest go on.</summary>
    private static async Task<int> CompareUrlsAsync(ImportCompareService compare, IReadOnlyList<string> urls, TextWriter output, TextWriter error,
        CancellationToken ct)
    {
        var rows = new List<ImportComparison>();
        var failed = 0;
        foreach (var url in urls)
        {
            var report = await compare.CompareUrlAsync(url, ct);
            if (report.Failure != Core.Python.WorkerFailure.None)
            {
                await error.WriteLineAsync($"error: the site reader is not available ({report.Failure}: {report.Detail}). Try: recipewarden python-selftest");
                return 1;
            }

            if (report.FetchError is not null)
            {
                failed++;
                await error.WriteLineAsync($"{url}: {report.FetchError}");
                continue;
            }

            rows.AddRange(report.Rows);
        }

        if (rows.Count > 0)
        {
            await WriteRowsAsync(output, rows);
            await output.WriteLineAsync();
            foreach (var r in rows)
            {
                await output.WriteLineAsync(r.Verdict switch
                {
                    CompareVerdict.OnlySiteReader => $"{r.Url}: only the site reader reads it completely; turning site readers on (Admin → Health) would help.",
                    CompareVerdict.BothComplete => $"{r.Url}: the normal import already reads it completely.",
                    _ when !r.SupportedHost => $"{r.Url}: recipe-scrapers has no reader for this site.",
                    _ => $"{r.Url}: {r.Verdict}.",
                });
            }
        }

        if (failed > 0)
        {
            await output.WriteLineAsync("A page that cannot be fetched here cannot be imported by address either: save the page from your browser "
                + "(or copy its HTML) and use Import → paste with the page address filled in.");
        }

        return failed > 0 ? 1 : 0;
    }

    private static async Task WriteRowsAsync(TextWriter output, IEnumerable<ImportComparison> rows)
    {
        await output.WriteLineAsync("Verdict          .NET           Site reader      Ingredients  Steps   Address");
        foreach (var r in rows)
        {
            var net = (r.DotNetMethod?.ToString() ?? "none") + (r.DotNetComplete ? "" : " (incomplete)");
            var site = !r.SupportedHost ? "no site reader" : r.SiteComplete ? "complete" : "incomplete";
            await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"{r.Verdict,-16} {net,-14} {site,-16} {r.DotNetIngredients,4} / {r.SiteIngredients,-4} {r.DotNetSteps,3} / {r.SiteSteps,-3} {r.Url}"));
        }
    }
}
