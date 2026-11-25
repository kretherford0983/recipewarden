using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Shopping;

namespace RecipeWarden.Web.Pages.Shopping;

/// <summary>
/// FR-SHOP-05, BR-43 to BR-47, route /shopping/{id}/staples: one walk around the kitchen. Staples the list's recipes
/// need come first, then every other staple by where it lives. Ticked ones go on the list (Need it, or a restock
/// item). Editor only (page policy): it adds items to the list.
/// </summary>
public sealed class StaplesModel(ShoppingService shopping, StapleService staples, TimeProvider clock) : PageModel
{
    public ShoppingListPage? List { get; private set; }

    /// <summary>Staples the recipes need that nobody has answered yet.</summary>
    public IReadOnlyList<ShoppingItem> FromRecipes { get; private set; } = [];

    /// <summary>The other staples, by kitchen location.</summary>
    public IReadOnlyList<IGrouping<string, StapleInfo>> Others { get; private set; } = [];

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    /// <summary>
    /// <paramref name="need"/> holds item keys of recipe staples ticked "need more"; <paramref name="restock"/> the
    /// staple keys (food or name keys) of other staples to add. Unticked recipe staples are answered Have it.
    /// </summary>
    public async Task<IActionResult> OnPostAsync(int id, string[]? need, string[]? restock)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        var ct = HttpContext.RequestAborted;
        var needed = (need ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (var item in FromRecipes)
        {
            await shopping.AnswerStapleAsync(id, item.Key, needed.Contains(item.Key) ? StapleAnswer.Need : StapleAnswer.Have, ct);
        }

        var wanted = (restock ?? []).ToHashSet(StringComparer.Ordinal);
        var chosen = Others.SelectMany(g => g).Where(s => wanted.Contains(Key(s))).ToList();
        if (chosen.Count > 0)
        {
            await shopping.AddRestockAsync(id, chosen, ct);
        }

        return Redirect("/shopping/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static string Key(StapleInfo staple) => ShoppingRules.KeyFor(staple.FoodId, staple.Name);

    private async Task<bool> LoadAsync(int id)
    {
        var ct = HttpContext.RequestAborted;
        List = await shopping.GetAsync(id, RecipeWarden.Core.Measurement.UnitSystem.US, ct);
        if (List is null)
        {
            return false;
        }

        FromRecipes = List.View.Pantry.Where(i => i.Answer is null).ToList();
        var onList = List.View.ToBuy.Concat(List.View.Pantry).Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        Others = StapleRules.ByLocation((await staples.ListAsync(ct)).Where(s => !onList.Contains(Key(s))), await staples.LocationsAsync(ct));
        return true;
    }
}
