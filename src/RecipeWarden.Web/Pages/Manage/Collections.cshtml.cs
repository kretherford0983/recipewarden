using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>FR-ORG-02, AC-20: create, rename, reorder and fill collections. Editor-only by folder policy.</summary>
public sealed class CollectionsModel(CollectionService collections) : PageModel
{
    public IReadOnlyList<CollectionSummary> Collections { get; private set; } = [];
    public CollectionView? Selected { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? c, string? done)
    {
        Message = done switch { "saved" => "Saved.", "deleted" => "Deleted.", "added" => "Added.", _ => null };
        await LoadAsync(c);
        return c is not null && Selected is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name, string? description)
    {
        var outcome = await collections.CreateAsync(name, description, Permissions.UserId(User), HttpContext.RequestAborted);
        return await AfterAsync(outcome);
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string? name, string? description, int version)
    {
        var outcome = await collections.UpdateAsync(id, name, description, version, Permissions.UserId(User), HttpContext.RequestAborted);
        return await AfterAsync(outcome);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await collections.DeleteAsync(id, HttpContext.RequestAborted);
        return Redirect("/manage/collections?done=deleted");
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int direction)
    {
        await collections.MoveAsync(id, direction, HttpContext.RequestAborted);
        return Redirect("/manage/collections");
    }

    /// <summary>From the reading view's "Add to collection" form; returns to that recipe.</summary>
    public async Task<IActionResult> OnPostAddRecipeAsync(int collectionId, int recipeId, string? returnSlug)
    {
        if (!await collections.AddRecipeAsync(collectionId, recipeId, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        // Only a valid slug is used, so this can never redirect off the site.
        return SlugRules.IsValid(returnSlug) ? Redirect("/recipes/" + returnSlug) : Redirect("/manage/collections?done=added");
    }

    public async Task<IActionResult> OnPostMoveRecipeAsync(int id, int recipeId, int direction)
    {
        await collections.MoveRecipeAsync(id, recipeId, direction, HttpContext.RequestAborted);
        return await BackToAsync(id);
    }

    public async Task<IActionResult> OnPostRemoveRecipeAsync(int id, int recipeId)
    {
        await collections.RemoveRecipeAsync(id, recipeId, HttpContext.RequestAborted);
        return await BackToAsync(id);
    }

    private async Task<IActionResult> AfterAsync(SaveOutcome outcome)
    {
        if (outcome.Status == SaveStatus.Saved)
        {
            return Redirect($"/manage/collections?c={outcome.Slug}&done=saved");
        }

        if (outcome.Status == SaveStatus.NotFound)
        {
            return NotFound();
        }

        Error = (outcome.Errors.Count > 0 ? outcome.Errors[0].Message : null);
        await LoadAsync(outcome.Slug);
        return Page();
    }

    private async Task<IActionResult> BackToAsync(int id)
    {
        var slug = (await collections.ListAsync(HttpContext.RequestAborted)).FirstOrDefault(c => c.Id == id)?.Slug;
        return Redirect(slug is null ? "/manage/collections" : "/manage/collections?c=" + slug);
    }

    private async Task LoadAsync(string? selectedSlug)
    {
        Collections = await collections.ListAsync(HttpContext.RequestAborted);
        if (selectedSlug is not null)
        {
            Selected = await collections.GetBySlugAsync(selectedSlug, includeDrafts: true, HttpContext.RequestAborted);
        }
    }
}
