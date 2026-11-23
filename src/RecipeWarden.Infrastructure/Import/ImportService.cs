using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Import;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>A file posted to the import page.</summary>
public sealed record ImportFile(string? FileName, Stream Content, long Length);

/// <summary>A file that could not be imported, and why.</summary>
public sealed record FileProblem(string FileName, string Error);

/// <summary>The result of starting an import: the jobs to review, or why nothing was read.</summary>
public sealed record ImportStart(string? BatchId, IReadOnlyList<int> JobIds, string? Error, bool OfferPaste, IReadOnlyList<FileProblem> FileProblems)
{
    public static ImportStart Fail(string error, bool offerPaste = false) => new(null, [], error, offerPaste, []);
}

public sealed record DuplicateLink(int Id, string Title, string Slug);

public sealed record ImportJobSummary(int Id, string BatchId, string Title, ImportStatus Status, ImportSourceKind Kind, string SourceRef,
    DateTimeOffset CreatedAt, int? CreatedRecipeId);

/// <summary>Everything the review page shows (FR-IMP-06, ADR 0025).</summary>
public sealed record ImportReview(int JobId, ImportSourceKind Kind, string SourceRef, ImportStatus Status, ImportedRecipe Recipe,
    IReadOnlyList<ImportWarning> Warnings, string? ImageUrl, bool CopyImage, DuplicateLink? Duplicate, int? CreatedRecipeId,
    IReadOnlyList<ImportJobSummary> Batch)
{
    /// <summary>BR-10: a web import's credit is shown and cannot be edited on the review page.</summary>
    public bool CreditLocked => Recipe.SourceType == SourceType.Web && Recipe.SourceUrl is not null;
}

public sealed record ImportSaveResult(SaveOutcome? Outcome, bool NeedsDuplicateChoice, string? ImageWarning)
{
    public static ImportSaveResult NotFound { get; } = new(SaveOutcome.NotFound, false, null);
}

/// <summary>
/// FR-IMP-01 to 08, W3, W4, ADR 0024, ADR 0025: runs an import in the request — fetch (through
/// <see cref="SafeFetcher"/>) or validate the upload (<see cref="UploadValidator"/>), extract, keep the raw payload
/// for 30 days, check for duplicates, and store one ImportJob per recipe for review. Saving goes through the normal
/// recipe pipeline, keeps a web recipe's credit (BR-10), and copies the image only when ticked (BR-12, AC-11).
/// </summary>
public sealed class ImportService(AppDbContext db, DataOptions data, ImportOptions options, SafeFetcher fetcher, RecipeService recipes,
    ImageStore images, TimeProvider clock)
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    public const string TooManyFiles = "Import at most 10 files at a time";
    public const string NoFiles = "Choose at least one file";
    public const string PasteEmpty = "Paste the recipe text or the page's HTML";
    public const string PasteTooLong = "The pasted text is too long (5 MB at most)";
    public const string NothingInFiles = "No recipes were found in the files";
    public const string DuplicateChoiceNeeded = "This looks like a recipe you already have. Open the existing one, or choose \"Save anyway\".";
    public const string ImageNotCopied = "The recipe was saved, but its image could not be copied";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task<ImportStart> StartUrlAsync(string? url, bool copyImage, string userId, CancellationToken cancellationToken = default)
    {
        var check = UrlRules.Check(url);
        if (!check.IsAllowed)
        {
            return ImportStart.Fail(check.Error!);
        }

        var fetched = await fetcher.FetchAsync(check.Uri!.AbsoluteUri, FetchPurpose.Page, cancellationToken).ConfigureAwait(false);
        if (!fetched.Ok)
        {
            return ImportStart.Fail(fetched.Error!, fetched.OfferPaste);
        }

        var html = Decode(fetched.Body!, fetched.Charset);
        if (fetched.ContentType == "application/ld+json")
        {
            html = "<script type=\"application/ld+json\">" + html.Replace("</", "<\\/", StringComparison.Ordinal) + "</script>";
        }

        var read = ImportReader.ReadPage(html, fetched.FinalUri ?? check.Uri!);
        if (read.Error is not null)
        {
            return ImportStart.Fail(read.Error, offerPaste: true);
        }

        var batch = await CreateBatchAsync(ImportSourceKind.Url, (fetched.FinalUri ?? check.Uri!).AbsoluteUri, [(fetched.Body!, read.Recipes)], copyImage, userId, cancellationToken)
            .ConfigureAwait(false);
        return new ImportStart(batch.BatchId, batch.Jobs, null, false, []);
    }

    /// <summary>ADR 0024: pasted page text or saved HTML, with the page address kept as the source when given.</summary>
    public async Task<ImportStart> StartPasteAsync(string? text, string? url, bool copyImage, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImportStart.Fail(PasteEmpty);
        }

        if (Encoding.UTF8.GetByteCount(text) > options.MaxPageBytes)
        {
            return ImportStart.Fail(PasteTooLong);
        }

        Uri? page = null;
        if (!string.IsNullOrWhiteSpace(url))
        {
            var check = UrlRules.Check(url);
            if (!check.IsAllowed)
            {
                return ImportStart.Fail(check.Error!);
            }

            page = check.Uri;
        }

        var read = ImportReader.ReadPaste(text, page);
        if (read.Error is not null)
        {
            return ImportStart.Fail(read.Error);
        }

        var batch = await CreateBatchAsync(ImportSourceKind.Paste, page?.AbsoluteUri ?? "Pasted text", [(Encoding.UTF8.GetBytes(text), read.Recipes)],
            copyImage && page is not null, userId, cancellationToken).ConfigureAwait(false);
        return new ImportStart(batch.BatchId, batch.Jobs, null, false, []);
    }

    /// <summary>FR-IMP-04, FR-IMP-05, W4: several files in one go; every file is validated (SEC-50, SEC-51).</summary>
    public async Task<ImportStart> StartFilesAsync(IReadOnlyList<ImportFile> files, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            return ImportStart.Fail(NoFiles);
        }

        if (files.Count > UploadRules.MaxFilesPerImport)
        {
            return ImportStart.Fail(TooManyFiles);
        }

        var problems = new List<FileProblem>();
        var read = new List<(byte[] Raw, IReadOnlyList<ImportedRecipe> Recipes, string Name, UploadKind Kind)>();
        foreach (var file in files)
        {
            var name = string.IsNullOrWhiteSpace(file.FileName) ? "(no name)" : Path.GetFileName(file.FileName);
            var (upload, error) = await UploadValidator.ReadDocumentAsync(file.Content, file.FileName, file.Length, cancellationToken).ConfigureAwait(false);
            if (upload is null)
            {
                problems.Add(new FileProblem(name, error!));
                continue;
            }

            var result = ImportReader.ReadFile(upload.Kind, upload.Bytes, upload.FileName);
            if (result.Error is not null)
            {
                problems.Add(new FileProblem(upload.FileName, result.Error));
                continue;
            }

            read.Add((upload.Bytes, result.Recipes, upload.FileName, upload.Kind));
        }

        if (read.Count == 0)
        {
            return new ImportStart(null, [], problems.Count == 1 ? problems[0].Error : NothingInFiles, false, problems);
        }

        var batchId = NewId();
        var jobs = new List<int>();
        for (var i = 0; i < read.Count; i++)
        {
            var kind = read[i].Kind switch
            {
                UploadKind.Docx => ImportSourceKind.Docx,
                UploadKind.Pdf => ImportSourceKind.Pdf,
                UploadKind.Text => ImportSourceKind.Text,
                _ => ImportSourceKind.Markdown,
            };
            var made = await CreateBatchAsync(kind, read[i].Name, [(read[i].Raw, read[i].Recipes)], false, userId, cancellationToken, batchId, i)
                .ConfigureAwait(false);
            jobs.AddRange(made.Jobs);
        }

        return new ImportStart(batchId, jobs, null, false, problems);
    }

    /// <summary>Imports waiting for review, newest first.</summary>
    public async Task<IReadOnlyList<ImportJobSummary>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await db.ImportJobs.AsNoTracking().Where(j => j.Status == ImportStatus.Review)
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id).Take(100)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return jobs.Select(Summary).ToList();
    }

    public async Task<ImportReview?> GetReviewAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return null;
        }

        var recipe = Parsed(job);
        DuplicateLink? duplicate = null;
        if (job.DuplicateOfRecipeId is { } dupId)
        {
            duplicate = await db.Recipes.AsNoTracking().Where(r => r.Id == dupId)
                .Select(r => new DuplicateLink(r.Id, r.Title, r.Slug)).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        var batch = await db.ImportJobs.AsNoTracking().Where(j => j.BatchId == job.BatchId).OrderBy(j => j.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var warnings = JsonSerializer.Deserialize<List<ImportWarning>>(job.WarningsJson, Json) ?? [];
        return new ImportReview(job.Id, job.SourceKind, job.SourceRef, job.Status, recipe, warnings, job.ImageUrl, job.CopyImage, duplicate,
            job.CreatedRecipeId, batch.Count > 1 ? batch.Select(Summary).ToList() : []);
    }

    /// <summary>
    /// Saves the reviewed recipe (ADR 0025). A possible duplicate needs <paramref name="saveAnyway"/> (AC-16). A web
    /// import keeps the credit it was imported with (BR-10); the image is copied only when ticked (BR-12, AC-11).
    /// </summary>
    public async Task<ImportSaveResult> SaveAsync(int jobId, RecipeEdit edit, bool copyImage, bool saveAnyway, string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != ImportStatus.Review)
        {
            return ImportSaveResult.NotFound;
        }

        if (job.DuplicateOfRecipeId is { } dupId && !saveAnyway
            && await db.Recipes.AnyAsync(r => r.Id == dupId, cancellationToken).ConfigureAwait(false))
        {
            return new ImportSaveResult(null, true, null);
        }

        var parsed = Parsed(job);
        var locked = parsed.SourceType == SourceType.Web && parsed.SourceUrl is not null;
        if (locked)
        {
            edit = edit with
            {
                SourceType = SourceType.Web,
                SourceUrl = parsed.SourceUrl,
                SourceName = parsed.SourceName ?? edit.SourceName,
                SourceAuthor = parsed.SourceAuthor ?? edit.SourceAuthor,
            };
        }

        var outcome = await recipes.CreateAsync(edit, userId, cancellationToken).ConfigureAwait(false);
        if (outcome.Status != SaveStatus.Saved)
        {
            return new ImportSaveResult(outcome, false, null);
        }

        var now = clock.GetUtcNow();
        var saved = await db.Recipes.SingleAsync(r => r.Id == outcome.Id, cancellationToken).ConfigureAwait(false);
        if (locked || job.SourceKind is not ImportSourceKind.Paste)
        {
            saved.SourceImportedAt = now;
        }

        if (parsed.Nutrition is { IsEmpty: false } nutrition)
        {
            // ADR 0035: the page's (or file's) panel is kept as the imported nutrition.
            var facts = new NutritionFacts { RecipeId = outcome.Id, Source = NutritionSource.Imported, UpdatedAt = now, UpdatedById = userId };
            facts.SetValues(nutrition);
            db.NutritionFacts.Add(facts);
        }

        job.Status = ImportStatus.Saved;
        job.CreatedRecipeId = outcome.Id;
        job.CopyImage = copyImage;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string? imageWarning = null;
        if (copyImage && job.ImageUrl is { } imageUrl)
        {
            imageWarning = await CopyImageAsync(outcome.Id, imageUrl, userId, cancellationToken).ConfigureAwait(false);
        }

        return new ImportSaveResult(outcome, false, imageWarning);
    }

    public async Task<bool> DiscardAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != ImportStatus.Review)
        {
            return false;
        }

        job.Status = ImportStatus.Discarded;
        job.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>FR-IMP-08: jobs and raw payloads older than 30 days are removed.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow() - KeepFor;
        var old = await db.ImportJobs.Where(j => j.CreatedAt < cutoff).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var batch in old.Select(j => j.BatchId).Distinct(StringComparer.Ordinal))
        {
            if (IsBatchId(batch))
            {
                var folder = Path.Combine(data.ImportsDirectory, batch);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        db.ImportJobs.RemoveRange(old);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return old.Count;
    }

    private async Task<string?> CopyImageAsync(int recipeId, string imageUrl, string userId, CancellationToken ct)
    {
        var fetched = await fetcher.FetchAsync(imageUrl, FetchPurpose.Image, ct).ConfigureAwait(false);
        if (!fetched.Ok)
        {
            return ImageNotCopied + ": " + fetched.Error;
        }

        var (id, error) = await images.SaveAsync(fetched.Body!, (fetched.FinalUri?.AbsoluteUri ?? imageUrl), userId, ct).ConfigureAwait(false);
        if (id is null)
        {
            return ImageNotCopied + ": " + error;
        }

        await recipes.SetFinishImageAsync(recipeId, id, userId, ct).ConfigureAwait(false);
        return null;
    }

    private async Task<(string BatchId, List<int> Jobs)> CreateBatchAsync(ImportSourceKind kind, string sourceRef,
        IReadOnlyList<(byte[] Raw, IReadOnlyList<ImportedRecipe> Recipes)> sources, bool copyImage, string userId, CancellationToken ct,
        string? batchId = null, int fileIndex = 0)
    {
        batchId ??= NewId();
        var now = clock.GetUtcNow();
        var folder = Path.Combine(data.ImportsDirectory, batchId);
        Directory.CreateDirectory(folder);
        var candidates = await DuplicateCandidatesAsync(sources.SelectMany(s => s.Recipes).ToList(), ct).ConfigureAwait(false);
        var jobs = new List<ImportJob>();
        foreach (var (raw, list) in sources)
        {
            var rawName = fileIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".raw";
            await File.WriteAllBytesAsync(Path.Combine(folder, rawName), raw, ct).ConfigureAwait(false);
            foreach (var recipe in list)
            {
                var names = ImportReader.IngredientNames(recipe.Ingredients);
                var duplicate = ImportRules.FindDuplicate(recipe.Title, recipe.SourceUrl, names, candidates);
                var job = new ImportJob
                {
                    BatchId = batchId,
                    SourceKind = kind,
                    SourceRef = sourceRef.Length > SourceRules.MaxUrlLength ? sourceRef[..SourceRules.MaxUrlLength] : sourceRef,
                    Status = ImportStatus.Review,
                    RawPath = batchId + "/" + rawName,
                    ParsedJson = JsonSerializer.Serialize(recipe, Json),
                    WarningsJson = JsonSerializer.Serialize(ImportReader.Warnings(recipe), Json),
                    ImageUrl = recipe.ImageUrl,
                    CopyImage = copyImage && recipe.ImageUrl is not null,
                    DuplicateOfRecipeId = duplicate?.RecipeId,
                    CreatedById = userId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.ImportJobs.Add(job);
                jobs.Add(job);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (batchId, jobs.Select(j => j.Id).ToList());
    }

    /// <summary>Existing recipes with the same source address or title, with their ingredient names (FR-IMP-07).</summary>
    private async Task<List<DuplicateCandidate>> DuplicateCandidatesAsync(IReadOnlyList<ImportedRecipe> incoming, CancellationToken ct)
    {
        var keys = incoming.Select(r => ImportRules.DuplicateKey(r.SourceUrl)).Where(k => k is not null).ToHashSet(StringComparer.Ordinal);
        var titles = incoming.Select(r => Core.Organization.NameRules.Normalize(r.Title)).Where(t => t.Length > 0).ToHashSet(StringComparer.Ordinal);
        var all = await db.Recipes.AsNoTracking().Select(r => new { r.Id, r.Title, r.SourceUrl }).ToListAsync(ct).ConfigureAwait(false);
        var matches = all.Where(r => keys.Contains(ImportRules.DuplicateKey(r.SourceUrl)) || titles.Contains(Core.Organization.NameRules.Normalize(r.Title)))
            .ToList();
        var ids = matches.Select(m => m.Id).ToList();
        var names = await db.RecipeIngredients.AsNoTracking().Where(i => ids.Contains(i.RecipeId))
            .Select(i => new { i.RecipeId, i.Name }).ToListAsync(ct).ConfigureAwait(false);
        return matches.Select(m => new DuplicateCandidate(m.Id, m.Title, m.SourceUrl,
            names.Where(n => n.RecipeId == m.Id && !string.IsNullOrEmpty(n.Name)).Select(n => n.Name!).ToList())).ToList();
    }

    private static ImportedRecipe Parsed(ImportJob job) =>
        JsonSerializer.Deserialize<ImportedRecipe>(job.ParsedJson, Json) ?? new ImportedRecipe();

    private static ImportJobSummary Summary(ImportJob j) =>
        new(j.Id, j.BatchId, Parsed(j).Title ?? "(untitled)", j.Status, j.SourceKind, j.SourceRef, j.CreatedAt, j.CreatedRecipeId);

    /// <summary>Page bytes as text: the header's charset when it is one we know, else UTF-8.</summary>
    private static string Decode(byte[] body, string? charset)
    {
        var encoding = charset?.Trim('"', ' ').ToLowerInvariant() switch
        {
            "iso-8859-1" or "latin1" or "latin-1" => Encoding.Latin1,
            "us-ascii" or "ascii" => Encoding.ASCII,
            "utf-16" => Encoding.Unicode,
            _ => new UTF8Encoding(false),
        };
        return encoding.GetString(body).TrimStart('﻿');
    }

    private static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static bool IsBatchId(string id) => ImageStore.IsId(id);
}
