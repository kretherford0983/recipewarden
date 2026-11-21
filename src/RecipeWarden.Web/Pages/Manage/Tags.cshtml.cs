using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Organization;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>FR-ORG-01: tag management (create, rename, type, delete). Editor-only by folder policy.</summary>
public sealed class TagsModel(TagService tags) : PageModel
{
    public IReadOnlyList<TagSummary> Tags { get; private set; } = [];
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(string? done)
    {
        Message = done switch { "saved" => "Saved.", "deleted" => "Deleted.", _ => null };
        Tags = await tags.ListAsync(publishedOnly: false, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name, string? type) =>
        await ResultAsync(await tags.CreateAsync(name, NameRules.ParseTagType(type), HttpContext.RequestAborted));

    public async Task<IActionResult> OnPostUpdateAsync(int id, string? name, string? type) =>
        await ResultAsync(await tags.UpdateAsync(id, name, NameRules.ParseTagType(type), HttpContext.RequestAborted));

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await tags.DeleteAsync(id, HttpContext.RequestAborted);
        return Redirect("/manage/tags?done=deleted");
    }

    private async Task<IActionResult> ResultAsync(SaveOutcome outcome)
    {
        if (outcome.Status == SaveStatus.Saved)
        {
            return Redirect("/manage/tags?done=saved");
        }

        if (outcome.Status == SaveStatus.NotFound)
        {
            return NotFound();
        }

        Error = (outcome.Errors.Count > 0 ? outcome.Errors[0].Message : null);
        Tags = await tags.ListAsync(publishedOnly: false, HttpContext.RequestAborted);
        return Page();
    }
}
