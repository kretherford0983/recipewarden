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

    /// <summary>
    /// BR-122: <paramref name="line"/> holds the ticked lines (indexes as on the recipe page) when
    /// <paramref name="picked"/> is set; without it every offered line is weighed.
    /// </summary>
    public async Task<IActionResult> OnPostApplyAsync([FromRoute] int id, string? key, int[]? line, bool picked = false)
    {
        if (await LoadAsync(id) is not { } recipe)
        {
            return NotFound();
        }

        var ct = HttpContext.RequestAborted;
        var offer = (await scaler.ScaleAsync(recipe, 1m, DisplayUnits.AsWritten, ct)).Suggestions.FirstOrDefault(s => s.Key == key);
        if (offer is null)
        {
            return BadRequest();
        }

        var all = recipe.Ingredients.SelectMany(g => g.Lines).ToList();
        var choices = LineMeasureRules.Apply(offer.Lines.Select(l => l.Index).ToList(), picked ? line ?? [] : null)
            .Where(c => c.Key >= 0 && c.Key < all.Count && all[c.Key].Id > 0)
            .ToDictionary(c => all[c.Key].Id, c => c.Value);
        var ok = await suggestions.ApplyAsync(id, key!, choices, Permissions.UserId(User), ct);
        return ok ? Redirect("/recipes/" + recipe.Slug) : BadRequest();
    }

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
