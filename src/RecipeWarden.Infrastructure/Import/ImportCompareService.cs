using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Importers.Import;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>What import-compare found, or why it could not run the site reader.</summary>
public sealed record ImportCompareReport(IReadOnlyList<ImportComparison> Rows, int Missing, WorkerFailure Failure, string? Detail);

/// <summary>
/// AC-109, D6, ADR 0051: re-reads every saved URL import (the raw page kept for 30 days, FR-IMP-08) with the .NET
/// extractor and with recipe-scrapers' site reader, and reports how the two compare. Reads the import records and
/// files only; writes nothing and fetches nothing (the HTML is the copy SafeFetcher saved, AC-112).
/// </summary>
public sealed class ImportCompareService(AppDbContext db, DataOptions data, ISiteRecipeScraper scraper)
{
    public const long MaxPageBytes = 5 * 1024 * 1024;

    public async Task<ImportCompareReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await db.ImportJobs.AsNoTracking()
            .Where(j => j.SourceKind == ImportSourceKind.Url && j.RawPath != null)
            .OrderBy(j => j.Id)
            .Select(j => new { j.SourceRef, j.RawPath })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var root = Path.GetFullPath(data.ImportsDirectory) + Path.DirectorySeparatorChar;
        var rows = new List<ImportComparison>();
        var missing = 0;
        foreach (var job in jobs.DistinctBy(j => j.RawPath))
        {
            var path = Path.GetFullPath(Path.Combine(root, job.RawPath!));
            if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path) || new FileInfo(path).Length > MaxPageBytes
                || !Uri.TryCreate(job.SourceRef, UriKind.Absolute, out var url))
            {
                missing++; // purged after 30 days, or not a page this tool reads
                continue;
            }

            var html = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
            var read = ImportReader.ReadPage(html, url).Recipes;
            var ours = read.Count > 0 ? read[0] : null;
            var site = await scraper.ScrapeAsync(html, url, cancellationToken).ConfigureAwait(false);
            if (!site.Ok)
            {
                return new ImportCompareReport(rows, missing, site.Failure, site.Detail);
            }

            rows.Add(ImportCompareRules.Compare(url.AbsoluteUri, ours, site.Value));
        }

        return new ImportCompareReport(rows, missing, WorkerFailure.None, null);
    }
}
