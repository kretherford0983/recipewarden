using System.Text;
using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Recipes;

/// <summary>
/// FR-EXP-02, AC-25, AC-12, ADR 0031: <c>export.md</c> (recipe-md v1) or <c>export.zip</c> (the .md and its images),
/// at the reading view's scale. Anonymous for published recipes, like the recipe page.
/// </summary>
public sealed class ExportModel(RecipeService recipes, Permissions permissions, RecipeScaler scaler, UserPreferenceStore preferences,
    RecipeExporter exporter) : RecipeViewModel(recipes, permissions, scaler, preferences)
{
    private string format = string.Empty;

    protected override string RouteSuffix => "/export." + format;

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug, [FromRoute] string format, string? scale, string? servings,
        string? units, int? from, string? have)
    {
        if (format is not ("md" or "zip"))
        {
            return NotFound();
        }

        this.format = format;
        if (await LoadAsync(slug, scale, servings, units, from, have) is { } result)
        {
            return result;
        }

        var ct = HttpContext.RequestAborted;
        var recipe = Recipe!;
        if (format == "md")
        {
            var md = await exporter.MarkdownAsync(recipe.Id, Scaled, ct);
            return md is null ? NotFound() : File(Encoding.UTF8.GetBytes(md), "text/markdown; charset=utf-8", recipe.Slug + ".md");
        }

        var file = ZipFiles.Create();
        if (!await exporter.WriteZipAsync(file, recipe.Id, Scaled, ct))
        {
            await file.DisposeAsync();
            return NotFound();
        }

        file.Position = 0;
        return File(file, "application/zip", recipe.Slug + ".zip");
    }
}

/// <summary>Zips are built in a temporary file that is deleted when the response is done.</summary>
internal static class ZipFiles
{
    public static FileStream Create() => new(Path.Combine(Path.GetTempPath(), "rw-export-" + Guid.NewGuid().ToString("N") + ".zip"),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
}
