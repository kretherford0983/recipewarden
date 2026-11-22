using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Kitchen;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>
/// FR-TOOL-05, AC-41, ADR 0017: editors dismiss a suggestion or save it to the recipe. Readers never write:
/// the recipe page only offers "Show me" to them. Editor-only by the /Manage folder policy.
/// </summary>
public sealed class SuggestionsModel(RecipeService recipes, RecipeScaler scaler, SuggestionService suggestions) : PageModel
{
    public RecipeView? Recipe { get; private set; }
    public IReadOnlyList<Suggestion> Offers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync([FromRoute] int id)
    {
        if (await LoadAsync(id) is not { } recipe)
        {
            return NotFound();
        }

        Offers = (await scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, HttpContext.RequestAborted)).Suggestions;
        return Page();
    }

    public async Task<IActionResult> OnPostDismissAsync([FromRoute] int id, string? key) =>
        await SettleAsync(id, key, apply: false);

    public async Task<IActionResult> OnPostApplyAsync([FromRoute] int id, string? key) =>
        await SettleAsync(id, key, apply: true);

    private async Task<IActionResult> SettleAsync(int id, string? key, bool apply)
    {
        if (await LoadAsync(id) is not { } recipe)
        {
            return NotFound();
        }

        var ct = HttpContext.RequestAborted;
        var userId = Permissions.UserId(User);
        var ok = apply ? await suggestions.ApplyAsync(id, key ?? string.Empty, userId, ct) : await suggestions.DismissAsync(id, key ?? string.Empty, userId, ct);
        return ok ? Redirect("/recipes/" + recipe.Slug) : BadRequest();
    }

    private async Task<RecipeView?> LoadAsync(int id)
    {
        var edit = await recipes.GetForEditAsync(id, HttpContext.RequestAborted);
        if (edit is null)
        {
            return null;
        }

        Recipe = (await recipes.GetForReadingAsync(edit.Slug, canSeeDrafts: true, HttpContext.RequestAborted)).Recipe;
        return Recipe;
    }
}
