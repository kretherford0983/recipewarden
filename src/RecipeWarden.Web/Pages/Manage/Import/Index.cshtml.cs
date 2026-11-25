using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Import;

/// <summary>
/// W3, W4, FR-IMP-01 to 05, ADR 0024: import from a web address, pasted text or HTML, or files. Each import runs in
/// the request; when it finds recipes the browser goes to the first review page. Editor-only (/Manage). The URL
/// handler is limited to 20 an hour per user (SEC-43); every POST needs the antiforgery token (SEC-30).
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public sealed class IndexModel(ImportService imports, Infrastructure.Ai.AiParserService ai) : PageModel
{
    /// <summary>FR-IMP-13, AC-126: the photo form is shown only when the AI parser is on and its model reads images.</summary>
    public bool OfferPhotos { get; private set; }

    /// <summary>Room for a 60 MB file, so an oversized document gets a clear message rather than a bare 413 (AC-15).</summary>
    public const long MaxRequestBytes = 64L * 1024 * 1024;

    [BindProperty(Name = "Url")]
    public string? Address { get; set; }

    [BindProperty]
    public bool CopyImage { get; set; }

    [BindProperty]
    public string? PasteText { get; set; }

    [BindProperty]
    public string? PasteUrl { get; set; }

    public string? Error { get; private set; }

    /// <summary>ADR 0024: the site refused or had no recipe; the paste form is opened with the address filled in.</summary>
    public bool OfferPaste { get; private set; }

    public IReadOnlyList<FileProblem> FileProblems { get; private set; } = [];
    public IReadOnlyList<int> CreatedJobs { get; private set; } = [];
    public IReadOnlyList<ImportJobSummary> Open { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Open = await imports.ListOpenAsync(HttpContext.RequestAborted);
        await LoadPhotoOfferAsync();
    }

    /// <summary>
    /// FR-IMP-13, W11, ADR 0058: 1 to 4 photos read by the AI parser. Refused with the reason, and nothing sent, when
    /// photo import is unavailable (BR-130, AC-126).
    /// </summary>
    public async Task<IActionResult> OnPostPhotosAsync(List<IFormFile> photos, string? sourceName, bool keepPhotos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        if (photos.Count > Core.AiParser.PhotoImportRules.MaxPhotos)
        {
            return await FailAsync(Core.AiParser.PhotoImportRules.TooMany);
        }

        var streams = new List<Stream>();
        try
        {
            var input = new List<ImportFile>();
            foreach (var f in photos)
            {
                var s = f.OpenReadStream();
                streams.Add(s);
                input.Add(new ImportFile(f.FileName, s, f.Length));
            }

            var start = await imports.StartPhotosAsync(input, sourceName, keepPhotos, Permissions.UserId(User), HttpContext.RequestAborted);
            return start.Error is null ? Redirect($"/manage/import/{start.JobIds[0]}/review") : await FailAsync(start.Error);
        }
        finally
        {
            foreach (var s in streams)
            {
                await s.DisposeAsync();
            }
        }
    }

    private async Task LoadPhotoOfferAsync()
    {
        var settings = await ai.GetSettingsAsync(HttpContext.RequestAborted);
        OfferPhotos = settings.Enabled && settings.CanReadImages;
    }

    public async Task<IActionResult> OnPostUrlAsync()
    {
        var start = await imports.StartUrlAsync(Address, CopyImage, Permissions.UserId(User), HttpContext.RequestAborted);
        if (start.Error is null)
        {
            return Redirect($"/manage/import/{start.JobIds[0]}/review");
        }

        OfferPaste = start.OfferPaste;
        if (OfferPaste)
        {
            PasteUrl = Address;
        }

        return await FailAsync(start.Error);
    }

    public async Task<IActionResult> OnPostPasteAsync()
    {
        var start = await imports.StartPasteAsync(PasteText, PasteUrl, CopyImage, Permissions.UserId(User), HttpContext.RequestAborted);
        if (start.Error is null)
        {
            return Redirect($"/manage/import/{start.JobIds[0]}/review");
        }

        OfferPaste = true;
        return await FailAsync(start.Error);
    }

    public async Task<IActionResult> OnPostFilesAsync(List<IFormFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count > UploadRules.MaxFilesPerImport)
        {
            return await FailAsync(ImportService.TooManyFiles);
        }

        var streams = new List<Stream>();
        try
        {
            var input = new List<ImportFile>();
            foreach (var f in files)
            {
                var s = f.OpenReadStream();
                streams.Add(s);
                input.Add(new ImportFile(f.FileName, s, f.Length));
            }

            var start = await imports.StartFilesAsync(input, Permissions.UserId(User), HttpContext.RequestAborted);
            if (start.JobIds.Count > 0 && start.FileProblems.Count == 0)
            {
                return Redirect($"/manage/import/{start.JobIds[0]}/review");
            }

            FileProblems = start.FileProblems;
            CreatedJobs = start.JobIds;
            return start.JobIds.Count > 0 ? await ShowAsync(StatusCodes.Status200OK) : await FailAsync(start.Error ?? ImportService.NothingInFiles);
        }
        finally
        {
            foreach (var s in streams)
            {
                await s.DisposeAsync();
            }
        }
    }

    private async Task<IActionResult> FailAsync(string error)
    {
        Error = error;
        return await ShowAsync(StatusCodes.Status422UnprocessableEntity);
    }

    private async Task<IActionResult> ShowAsync(int status)
    {
        Open = await imports.ListOpenAsync(HttpContext.RequestAborted);
        await LoadPhotoOfferAsync();
        Response.StatusCode = status;
        return Page();
    }
}
