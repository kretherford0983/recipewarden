using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>
/// FR-EXP-03, ADR 0031: every recipe (or a collection's) as one .zip of recipe-md files and images. Readers get
/// published recipes; editors drafts too. Shares the search rate limit (SEC-63).
/// </summary>
public sealed class BulkExportModel(RecipeExporter exporter, Permissions permissions) : PageModel
{
    public async Task<IActionResult> OnGetAsync([FromRoute] string? slug)
    {
        var ct = HttpContext.RequestAborted;
        var ids = await exporter.BulkIdsAsync(await permissions.CanEditAsync(User), slug, ct);
        if (ids is null)
        {
            return NotFound();
        }

        var file = ZipFiles.Create();
        await exporter.WriteBulkZipAsync(file, ids, ct);
        file.Position = 0;
        return File(file, "application/zip", (slug ?? "recipes") + ".zip");
    }
}
