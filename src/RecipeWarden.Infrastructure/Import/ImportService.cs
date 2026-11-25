using RecipeWarden.Core.AiParser;
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

    /// <summary>FR-IMP-09: the fields came from the AI parser and are marked on the review page.</summary>
    public bool ParsedByAi { get; init; }

    /// <summary>FR-IMP-13: the photos the recipe was read from (shown on the review page) and whether to keep them.</summary>
    public int PhotoCount { get; init; }
    public bool KeepPhotos { get; init; }
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
    ImageStore images, Ai.AiParserService ai, Core.Python.IIngredientSecondOpinion secondOpinion, Core.Python.ISiteRecipeScraper siteReader,
    SiteReaderSettings siteReaders, TimeProvider clock)
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

        var pageUri = fetched.FinalUri ?? check.Uri!;
        var page = await WithSiteReaderAsync(ImportReader.ReadPage(html, pageUri), html, pageUri, cancellationToken).ConfigureAwait(false);
        var read = await WithAiAsync(page, () => HtmlText.PageText(html), pageUri, cancellationToken).ConfigureAwait(false);
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

        var pasted = ImportReader.ReadPaste(text, page);
        if (page is not null && ImportReader.LooksLikeHtml(text))
        {
            pasted = await WithSiteReaderAsync(pasted, text, page, cancellationToken).ConfigureAwait(false); // AC-112: the pasted HTML only
        }

        var read = await WithAiAsync(pasted, () => ImportPlainText.From(Encoding.UTF8.GetBytes(text), null), page, cancellationToken)
            .ConfigureAwait(false);
        if (read.Error is not null)
        {
            return ImportStart.Fail(read.Error);
        }

        var batch = await CreateBatchAsync(ImportSourceKind.Paste, page?.AbsoluteUri ?? "Pasted text", [(Encoding.UTF8.GetBytes(text), read.Recipes)],
            copyImage && page is not null, userId, cancellationToken).ConfigureAwait(false);
        return new ImportStart(batch.BatchId, batch.Jobs, null, false, []);
    }

    /// <summary>
    /// FR-IMP-13, W11, BR-128 to BR-131, SEC-48, ADR 0058: 1 to 4 photos read by the AI parser's vision model. Nothing
    /// is sent when photo import is unavailable (BR-130) or a photo fails validation (SEC-50, SEC-51). Only the SEC-48
    /// JPEG copies are sent; they and the SEC-52 WebP copies are kept in the batch folder for 30 days, the upload is not.
    /// </summary>
    public async Task<ImportStart> StartPhotosAsync(IReadOnlyList<ImportFile> photos, string? sourceName, bool keepPhotos, string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photos);
        if (PhotoImportRules.CheckCount(photos.Count) is { } count)
        {
            return ImportStart.Fail(count);
        }

        if (await ai.PhotoImportUnavailableAsync(cancellationToken).ConfigureAwait(false) is { } reason)
        {
            return ImportStart.Fail(reason);
        }

        var name = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName.Trim();
        if (name is not null && (name.Length > SourceRules.MaxNameLength || name.Any(char.IsControl)))
        {
            return ImportStart.Fail($"The source name must be at most {SourceRules.MaxNameLength} characters on one line.");
        }

        var sent = new List<byte[]>();
        var kept = new List<byte[]>();
        foreach (var photo in photos)
        {
            var label = string.IsNullOrWhiteSpace(photo.FileName) ? "(no name)" : Path.GetFileName(photo.FileName);
            var (upload, error) = await UploadValidator.ReadImageAsync(photo.Content, photo.FileName, photo.Length, cancellationToken).ConfigureAwait(false);
            if (upload is null)
            {
                return ImportStart.Fail(label + ": " + error);
            }

            var (jpeg, aiError) = ImageEncoder.ForAi(upload.Bytes);
            var (webp, keepError) = ImageEncoder.Encode(upload.Bytes);
            if (jpeg is null || webp is null)
            {
                return ImportStart.Fail(label + ": " + (aiError ?? keepError));
            }

            sent.Add(jpeg);
            kept.Add(webp.Full);
        }

        var parsed = await ai.ParsePhotosAsync(sent, AiPurpose.PhotoImport, cancellationToken).ConfigureAwait(false);
        if (parsed.Recipe is null)
        {
            return ImportStart.Fail(parsed.Error ?? "The photos could not be read.");
        }

        var recipe = parsed.Recipe with { SourceType = SourceType.Personal, SourceName = name, SourceUrl = null, SourceAuthor = null, ImageUrl = null };
        var batchId = NewId();
        var folder = Path.Combine(data.ImportsDirectory, batchId);
        Directory.CreateDirectory(folder);
        for (var i = 0; i < sent.Count; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, PhotoFile(i, "jpg")), sent[i], cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(folder, PhotoFile(i, "webp")), kept[i], cancellationToken).ConfigureAwait(false);
        }

        var candidates = await DuplicateCandidatesAsync([recipe], cancellationToken).ConfigureAwait(false);
        var duplicate = ImportRules.FindDuplicate(recipe.Title, null, ImportReader.IngredientNames(recipe.Ingredients), candidates);
        var now = clock.GetUtcNow();
        var job = new ImportJob
        {
            BatchId = batchId,
            SourceKind = ImportSourceKind.Photo,
            SourceRef = name ?? (sent.Count == 1 ? "1 photo" : $"{sent.Count} photos"),
            Status = ImportStatus.Review,
            RawPath = batchId + "/" + PhotoFile(0, "jpg"),
            ParsedJson = JsonSerializer.Serialize(recipe, Json),
            WarningsJson = JsonSerializer.Serialize(await WarningsAsync(recipe, cancellationToken).ConfigureAwait(false), Json),
            DuplicateOfRecipeId = duplicate?.RecipeId,
            CreatedById = userId,
            CreatedAt = now,
            UpdatedAt = now,
            ParsedByAi = true,
            PhotoCount = sent.Count,
            KeepPhotos = keepPhotos,
        };
        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ImportStart(batchId, [job.Id], null, false, []);
    }

    /// <summary>FR-IMP-13: the photo the review page shows (the copy that was sent), or null.</summary>
    public async Task<string?> PhotoPathAsync(int jobId, int index, CancellationToken cancellationToken = default)
    {
        var job = await db.ImportJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || index < 0 || index >= job.PhotoCount || !IsBatchId(job.BatchId))
        {
            return null;
        }

        var path = Path.Combine(data.ImportsDirectory, job.BatchId, PhotoFile(index, "jpg"));
        return File.Exists(path) ? path : null;
    }

    private static string PhotoFile(int index, string extension) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"photo-{index}.{extension}");

    /// <summary>FR-IMP-13, BR-131: the kept photos of a photo job, in order, or null when they are gone.</summary>
    private async Task<List<byte[]>?> ReadPhotosAsync(ImportJob job, string extension, CancellationToken ct)
    {
        var photos = new List<byte[]>();
        for (var i = 0; i < job.PhotoCount; i++)
        {
            var path = Path.Combine(data.ImportsDirectory, job.BatchId, PhotoFile(i, extension));
            if (!IsBatchId(job.BatchId) || !File.Exists(path))
            {
                return null;
            }

            photos.Add(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false));
        }

        return photos;
    }

    /// <summary>
    /// FR-IMP-12, BR-117, ADR 0059: when site readers are on and JSON-LD/microdata gave no complete reading, the site
    /// reader reads the same HTML (from SafeFetcher or the paste, AC-112); a complete reading on a supported host
    /// replaces the .NET one. Off, failed or incomplete: the .NET reading stands (AC-131, BR-119).
    /// </summary>
    private async Task<ReadResult> WithSiteReaderAsync(ReadResult read, string html, Uri pageUri, CancellationToken ct)
    {
        var ours = read.Recipes.Count > 0 ? read.Recipes[0] : null;
        if (!SiteReaderRules.ShouldTry(ours) || !await siteReaders.EnabledAsync(ct).ConfigureAwait(false))
        {
            return read;
        }

        var outcome = await siteReader.ScrapeAsync(html, pageUri, ct).ConfigureAwait(false);
        return outcome.Ok && SiteReaderRules.ToImported(outcome.Value!, pageUri, ours) is { } site
            ? new ReadResult([site], null)
            : read;
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

            var bytes = upload.Bytes;
            var fileName = upload.FileName;
            var result = await WithAiAsync(ImportReader.ReadFile(upload.Kind, bytes, fileName), () => ImportPlainText.From(bytes, fileName), null, cancellationToken)
                .ConfigureAwait(false);
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

    /// <summary>
    /// FR-IMP-09, ADR 0042: when the AI parser is on and the rules found no recipe, or a recipe without ingredients or
    /// steps, or too many uncertain lines, the plain text is parsed with AI. A usable AI result replaces the rules'
    /// result (marked for review); otherwise the rules' result stays, with the reason. When the parser is off nothing is
    /// sent (AC-17).
    /// </summary>
    private async Task<ReadResult> WithAiAsync(ReadResult rules, Func<string?> plainText, Uri? page, CancellationToken ct)
    {
        var needs = rules.Error is not null || rules.Recipes.Any(ImportReader.NeedsAi);
        if (!needs || !(await ai.GetSettingsAsync(ct).ConfigureAwait(false)).Enabled)
        {
            return rules;
        }

        var text = plainText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return rules;
        }

        var parsed = await ai.ParseAsync(text, AiPurpose.Import, ct).ConfigureAwait(false);
        if (parsed.Recipe is { } recipe)
        {
            var first = rules.Recipes.Count > 0 ? rules.Recipes[0] : null;
            recipe = recipe with
            {
                SourceType = first?.SourceType ?? (page is null ? recipe.SourceType : SourceType.Web),
                SourceUrl = first?.SourceUrl ?? page?.AbsoluteUri ?? recipe.SourceUrl,
                SourceName = first?.SourceName ?? (page is null ? recipe.SourceName : HostName(page)),
                SourceAuthor = first?.SourceAuthor ?? recipe.SourceAuthor,
                ImageUrl = first?.ImageUrl ?? recipe.ImageUrl,
            };
            return new ReadResult([recipe], null);
        }

        if (rules.Error is not null || parsed.Error is null)
        {
            return rules;
        }

        // The AI was not usable (cap, reply, network): keep the rules' result and say why.
        return new ReadResult(rules.Recipes.Select(r => r with { Remarks = [.. r.Remarks, parsed.Error] }).ToList(), null);
    }

    /// <summary>FR-IMP-09 "Re-parse with AI" on the review page: the stored raw payload, parsed again with AI.</summary>
    public async Task<string?> ReparseWithAiAsync(int jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == jobId && j.Status == ImportStatus.Review, cancellationToken).ConfigureAwait(false);
        if (job?.RawPath is null)
        {
            return "That import is no longer waiting for review.";
        }

        if (!(await ai.GetSettingsAsync(cancellationToken).ConfigureAwait(false)).Enabled)
        {
            return AiParserRules.Disabled;
        }

        Ai.AiParseResult parsed;
        if (job.SourceKind == ImportSourceKind.Photo)
        {
            // BR-131: the kept SEC-48 copies are sent again; no new upload.
            if (await ReadPhotosAsync(job, "jpg", cancellationToken).ConfigureAwait(false) is not { } photos)
            {
                return "The photos of this import are no longer kept (30 days).";
            }

            parsed = await ai.ParsePhotosAsync(photos, AiPurpose.Reparse, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var raw = Path.Combine(data.ImportsDirectory, job.RawPath);
            if (!File.Exists(raw))
            {
                return "The original text of this import is no longer kept (30 days).";
            }

            var text = ImportPlainText.From(await File.ReadAllBytesAsync(raw, cancellationToken).ConfigureAwait(false), job.SourceRef);
            if (string.IsNullOrWhiteSpace(text))
            {
                return "There is no text to send to the AI parser.";
            }

            parsed = await ai.ParseAsync(text, AiPurpose.Reparse, cancellationToken).ConfigureAwait(false);
        }

        if (parsed.Recipe is null)
        {
            return parsed.Error;
        }

        var before = Parsed(job);
        var recipe = parsed.Recipe with
        {
            SourceType = before.SourceType ?? parsed.Recipe.SourceType,
            SourceUrl = before.SourceUrl ?? parsed.Recipe.SourceUrl,
            SourceName = before.SourceName ?? parsed.Recipe.SourceName,
            SourceAuthor = before.SourceAuthor ?? parsed.Recipe.SourceAuthor,
            ImageUrl = before.ImageUrl ?? parsed.Recipe.ImageUrl,
        };
        job.ParsedJson = JsonSerializer.Serialize(recipe, Json);
        job.WarningsJson = JsonSerializer.Serialize(await WarningsAsync(recipe, cancellationToken).ConfigureAwait(false), Json);
        job.ParsedByAi = true;
        job.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// FR-IMP-06 warnings, plus FR-IMP-11's second opinion: one worker call per imported recipe. When the worker is off,
    /// missing or fails, the review has the v1 warnings only (BR-119); the imported text is never changed.
    /// </summary>
    private async Task<IReadOnlyList<ImportWarning>> WarningsAsync(ImportedRecipe recipe, CancellationToken ct)
    {
        var warnings = ImportReader.Warnings(recipe).ToList();

        // BR-129: what the model could not read in a photo is listed for the Editor to fix.
        warnings.AddRange(new[] { recipe.Title, recipe.Ingredients, recipe.Steps, recipe.Notes }
            .SelectMany(PhotoImportRules.UnclearLines).Select(l => new ImportWarning(PhotoImportRules.UnclearWarning, l)));
        var parses = ImportReader.IngredientParses(recipe.Ingredients);
        if (parses.Count == 0)
        {
            return warnings;
        }

        var lines = parses.Select(p => p.RawText.Length <= ImportReader.MaxSecondOpinionLineLength ? p.RawText : string.Empty).ToList();
        var outcome = await secondOpinion.ReadAsync(lines, ct).ConfigureAwait(false);
        if (outcome.Ok)
        {
            warnings.AddRange(ImportRules.SecondOpinionWarnings(parses, outcome.Value!));
        }

        return warnings;
    }

    private static string HostName(Uri page) =>
        page.IdnHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.IdnHost[4..] : page.IdnHost;

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
            job.CreatedRecipeId, batch.Count > 1 ? batch.Select(Summary).ToList() : [])
        {
            ParsedByAi = job.ParsedByAi,
            PhotoCount = job.PhotoCount,
            KeepPhotos = job.KeepPhotos,
        };
    }

    /// <summary>
    /// Saves the reviewed recipe (ADR 0025). A possible duplicate needs <paramref name="saveAnyway"/> (AC-16). A web
    /// import keeps the credit it was imported with (BR-10); the image is copied only when ticked (BR-12, AC-11).
    /// </summary>
    public Task<ImportSaveResult> SaveAsync(int jobId, RecipeEdit edit, bool copyImage, bool saveAnyway, string userId,
        CancellationToken cancellationToken = default) =>
        SaveAsync(jobId, edit, copyImage, saveAnyway, null, userId, cancellationToken);

    /// <summary>
    /// As above; for a photo import (FR-IMP-13) <paramref name="keepPhotos"/> is "Save the card photo with the recipe"
    /// (null keeps the import's choice), and an <c>[unclear]</c> left in an ingredient amount stops the save (BR-129).
    /// </summary>
    public async Task<ImportSaveResult> SaveAsync(int jobId, RecipeEdit edit, bool copyImage, bool saveAnyway, bool? keepPhotos, string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != ImportStatus.Review)
        {
            return ImportSaveResult.NotFound;
        }

        if (job.SourceKind == ImportSourceKind.Photo && PhotoImportRules.UnclearAmounts(edit.IngredientsText) is { Count: > 0 } unclear)
        {
            return new ImportSaveResult(new SaveOutcome
            {
                Status = SaveStatus.Invalid,
                Errors = [new PublishProblem(RecipeField.Ingredients, PhotoImportRules.UnclearAmount + ": " + string.Join("; ", unclear))],
            }, false, null);
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
        job.KeepPhotos = keepPhotos ?? job.KeepPhotos;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (job.SourceKind == ImportSourceKind.Photo && job.KeepPhotos)
        {
            await KeepCardPhotosAsync(job, outcome.Id, userId, cancellationToken).ConfigureAwait(false);
        }

        string? imageWarning = null;
        if (copyImage && job.ImageUrl is { } imageUrl)
        {
            imageWarning = await CopyImageAsync(outcome.Id, imageUrl, userId, cancellationToken).ConfigureAwait(false);
        }

        return new ImportSaveResult(outcome, false, imageWarning);
    }

    /// <summary>
    /// BR-125: "Import it as its own recipe" for a batch size folded into this import's notes. The batch becomes a new
    /// draft in the same batch, and its note and warning leave this one. Null when there is nothing to split.
    /// </summary>
    public async Task<int?> SplitFoldedAsync(int jobId, int index, CancellationToken cancellationToken = default)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != ImportStatus.Review)
        {
            return null;
        }

        var recipe = Parsed(job);
        if (index < 0 || index >= recipe.FoldedBatches.Count)
        {
            return null;
        }

        var batch = recipe.FoldedBatches[index].Recipe with { SourceType = recipe.SourceType };
        var remaining = BatchRules.Unfold(recipe, index);
        var now = clock.GetUtcNow();
        job.ParsedJson = JsonSerializer.Serialize(remaining, Json);
        job.WarningsJson = JsonSerializer.Serialize(await WarningsAsync(remaining, cancellationToken).ConfigureAwait(false), Json);
        job.UpdatedAt = now;
        var split = new ImportJob
        {
            BatchId = job.BatchId,
            SourceKind = job.SourceKind,
            SourceRef = job.SourceRef,
            Status = ImportStatus.Review,
            RawPath = job.RawPath,
            ParsedJson = JsonSerializer.Serialize(batch, Json),
            WarningsJson = JsonSerializer.Serialize(await WarningsAsync(batch, cancellationToken).ConfigureAwait(false), Json),
            CreatedById = job.CreatedById,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ImportJobs.Add(split);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return split.Id;
    }

    /// <summary>
    /// BR-132: resolves two versions of an ingredient group on an import under review. A split adds one draft per other
    /// version to the same batch. Returns the kept draft's id, or null when nothing changed.
    /// </summary>
    public async Task<int?> ResolveVersionsAsync(int jobId, string? name, string? keep, VersionChoice choice, CancellationToken cancellationToken = default)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != ImportStatus.Review || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(keep))
        {
            return null;
        }

        var recipe = Parsed(job);
        var (kept, split) = BatchRules.ResolveVersions(recipe, name, keep, choice);
        if (ReferenceEquals(kept, recipe))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        job.ParsedJson = JsonSerializer.Serialize(kept, Json);
        job.WarningsJson = JsonSerializer.Serialize(await WarningsAsync(kept, cancellationToken).ConfigureAwait(false), Json);
        job.UpdatedAt = now;
        foreach (var other in split)
        {
            var draft = new ImportJob
            {
                BatchId = job.BatchId,
                SourceKind = job.SourceKind,
                SourceRef = job.SourceRef,
                Status = ImportStatus.Review,
                RawPath = job.RawPath,
                ParsedJson = JsonSerializer.Serialize(other, Json),
                WarningsJson = JsonSerializer.Serialize(await WarningsAsync(other, cancellationToken).ConfigureAwait(false), Json),
                CreatedById = job.CreatedById,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.ImportJobs.Add(draft);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return job.Id;
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

    /// <summary>BR-131, ADR 0058 D7: the kept SEC-52 copies become the recipe's card images, in order.</summary>
    private async Task KeepCardPhotosAsync(ImportJob job, int recipeId, string userId, CancellationToken ct)
    {
        var photos = await ReadPhotosAsync(job, "webp", ct).ConfigureAwait(false) ?? [];
        for (var i = 0; i < photos.Count; i++)
        {
            var (id, _) = await images.SaveAsync(photos[i], null, userId, ct).ConfigureAwait(false);
            if (id is not null)
            {
                db.RecipeCardImages.Add(new RecipeCardImage { RecipeId = recipeId, Position = i, ImageId = id });
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
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
                    WarningsJson = JsonSerializer.Serialize(await WarningsAsync(recipe, ct).ConfigureAwait(false), Json),
                    ImageUrl = recipe.ImageUrl,
                    CopyImage = copyImage && recipe.ImageUrl is not null,
                    DuplicateOfRecipeId = duplicate?.RecipeId,
                    CreatedById = userId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    ParsedByAi = recipe.Remarks.Contains(AiParserRules.AiParsedNotice),
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
