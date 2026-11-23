using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Nutrition;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>
/// FR-NUT-05, AC-58, ADR 0034: the USDA dataset in use, and an upload for a newer FoodData Central CSV zip (SR Legacy
/// or Foundation Foods). The file streams to disk through UploadValidator, then the same importer as
/// "recipewarden nutrition-import" applies it; mappings are kept. Nothing is downloaded (D-06, BR-54).
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public sealed class NutritionDataModel(AppDbContext db, NutrientDatasetImporter importer, DataOptions data) : PageModel
{
    public const long MaxRequestBytes = 1024L * 1024 * 1024;

    /// <summary>Cloudflare's free plan stops uploads at 100 MB; larger files go through the CLI.</summary>
    public const long MaxUploadBytes = 100L * 1024 * 1024;

    public IReadOnlyList<NutrientDataset> Datasets { get; private set; } = [];

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync(string? done)
    {
        Message = done;
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? file, string? version)
    {
        if (file is not { Length: > 0 })
        {
            return await FailAsync("Choose a .zip file.");
        }

        var temp = Path.Combine(data.WorkDirectory, "dataset-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var upload = file.OpenReadStream())
            {
                var error = await UploadValidator.SaveArchiveAsync(upload, file.FileName, file.Length, ArchiveUploadKind.NutrientDataset, MaxUploadBytes, temp, HttpContext.RequestAborted);
                if (error is not null)
                {
                    return await FailAsync(error);
                }
            }

            var name = string.IsNullOrWhiteSpace(version) ? Path.GetFileNameWithoutExtension(file.FileName) : version.Trim();
            if (name.Length > 80)
            {
                name = name[..80];
            }

            await using var stream = System.IO.File.OpenRead(temp);
            var result = await importer.ImportAsync(stream, name, User.Identity?.Name ?? "admin", HttpContext.RequestAborted);
            if (!result.Ok)
            {
                return await FailAsync(result.Error ?? "The dataset could not be applied.");
            }

            return Redirect("/admin/nutrition-data?done=" + Uri.EscapeDataString(
                $"\"{result.Version}\" applied: {result.Foods:N0} foods ({result.Added:N0} new, {result.Updated:N0} updated, {result.Removed:N0} flagged for re-mapping)."));
        }
        finally
        {
            if (System.IO.File.Exists(temp))
            {
                System.IO.File.Delete(temp);
            }
        }
    }

    private async Task<IActionResult> FailAsync(string error)
    {
        Error = error;
        await LoadAsync();
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private async Task LoadAsync() =>
        Datasets = await db.NutrientDatasets.AsNoTracking().OrderByDescending(d => d.Id).Take(20).ToListAsync(HttpContext.RequestAborted);
}
