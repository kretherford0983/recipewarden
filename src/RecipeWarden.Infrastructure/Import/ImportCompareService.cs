using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Import;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>What import-compare found, or why it could not run the site reader (or, with --url, fetch the page).</summary>
public sealed record ImportCompareReport(IReadOnlyList<ImportComparison> Rows, int Missing, WorkerFailure Failure, string? Detail,
    string? FetchError = null);

/// <summary>
/// AC-109, D6, ADR 0051: re-reads every saved URL import (the raw page kept for 30 days, FR-IMP-08) with the .NET
/// extractor and with recipe-scrapers' site reader, and reports how the two compare. Reads the import records and
/// files only; writes nothing and fetches nothing (the HTML is the copy SafeFetcher saved, AC-112).
/// </summary>
public sealed class ImportCompareService(AppDbContext db, DataOptions data, ISiteRecipeScraper scraper, SafeFetcher? fetcher = null)
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

    /// <summary>
    /// AC-109 (widened 2026-11-25, owner): "import-compare --url". Fetches one page through SafeFetcher (SEC-40 to SEC-42,
    /// the same checks as a URL import) and compares the two readings of it. The site reader gets only that body (AC-112);
    /// nothing is saved and no import is made.
    /// </summary>
    public async Task<ImportCompareReport> CompareUrlAsync(string? address, CancellationToken cancellationToken = default)
    {
        var check = UrlRules.Check(address);
        if (!check.IsAllowed)
        {
            return new ImportCompareReport([], 0, WorkerFailure.None, null, check.Error);
        }

        var fetched = await (fetcher ?? throw new InvalidOperationException("--url needs the safe fetcher")).FetchAsync(check.Uri!.AbsoluteUri, FetchPurpose.Page, cancellationToken).ConfigureAwait(false);
        if (!fetched.Ok)
        {
            return new ImportCompareReport([], 0, WorkerFailure.None, null, fetched.Error ?? "The page could not be fetched.");
        }

        var html = Decode(fetched.Body!, fetched.Charset);
        if (fetched.ContentType == "application/ld+json")
        {
            html = "<script type=\"application/ld+json\">" + html.Replace("</", "<\\/", StringComparison.Ordinal) + "</script>";
        }

        var url = fetched.FinalUri ?? check.Uri!;
        var read = ImportReader.ReadPage(html, url).Recipes;
        var site = await scraper.ScrapeAsync(html, url, cancellationToken).ConfigureAwait(false);
        return site.Ok
            ? new ImportCompareReport([ImportCompareRules.Compare(url.AbsoluteUri, read.Count > 0 ? read[0] : null, site.Value)], 0, WorkerFailure.None, null)
            : new ImportCompareReport([], 0, site.Failure, site.Detail);
    }

    /// <summary>The fetched page as text, as a URL import reads it.</summary>
    private static string Decode(byte[] body, string? charset)
    {
        var encoding = charset?.Trim('"', ' ').ToLowerInvariant() switch
        {
            "iso-8859-1" or "latin1" or "latin-1" => Encoding.Latin1,
            "us-ascii" or "ascii" => Encoding.ASCII,
            "utf-16" => Encoding.Unicode,
            _ => new UTF8Encoding(false),
        };
        return encoding.GetString(body).TrimStart('\uFEFF');
    }
}
