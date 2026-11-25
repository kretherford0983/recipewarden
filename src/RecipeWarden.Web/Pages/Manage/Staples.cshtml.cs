using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Shopping;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>
/// FR-SHOP-05, ADR 0057, route /manage/staples: the household's staples (one list for everyone). Editor-only by
/// folder policy; every change is audited (AC-74).
/// </summary>
public sealed class StaplesModel(StapleService staples) : PageModel
{
    public IReadOnlyList<IGrouping<string, StapleInfo>> Groups { get; private set; } = [];

    /// <summary>BR-49: the kitchen locations in walking order.</summary>
    public IReadOnlyList<string> Locations { get; private set; } = StapleRules.Locations;
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(string? done)
    {
        Message = done switch { "added" => "Added.", "saved" => "Saved.", "removed" => "Removed.", "locations" => "Locations saved.", _ => null };
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAddAsync(string? name, string? location, string? restock, string? section) =>
        await DoneAsync(await staples.AddAsync(name, location, restock, section, HttpContext.RequestAborted), "added");

    public async Task<IActionResult> OnPostUpdateAsync(int id, int? foodId, string? location, string? restock, string? section) =>
        await DoneAsync(await staples.UpdateAsync(id, foodId, location, restock, section, HttpContext.RequestAborted), "saved");

    /// <summary>
    /// BR-49: the location editor. Rows arrive in their new order: <paramref name="original"/> and
    /// <paramref name="name"/> by position; <paramref name="remove"/> holds the positions ticked for removal.
    /// </summary>
    public async Task<IActionResult> OnPostLocationsAsync(string?[]? original, string?[]? name, int[]? remove, int[]? order)
    {
        original ??= [];
        name ??= [];
        order ??= [];
        var removed = (remove ?? []).ToHashSet();
        var rows = Enumerable.Range(0, Math.Min(name.Length, StapleRules.MaxLocations + 1))
            .OrderBy(i => i < order.Length ? order[i] : int.MaxValue).ThenBy(i => i)
            .Select(i => new LocationEdit(i < original.Length && !string.IsNullOrEmpty(original[i]) ? original[i] : null, name[i], removed.Contains(i)))
            .ToList();
        return await DoneAsync(await staples.SaveLocationsAsync(rows, HttpContext.RequestAborted), "locations");
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int? foodId) =>
        await staples.RemoveAsync(id, foodId, HttpContext.RequestAborted) ? Redirect("/manage/staples?done=removed") : NotFound();

    private async Task<IActionResult> DoneAsync(string? error, string done)
    {
        if (error is null)
        {
            return Redirect("/manage/staples?done=" + done);
        }

        Error = error;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Locations = await staples.LocationsAsync(HttpContext.RequestAborted);
        Groups = StapleRules.ByLocation(await staples.ListAsync(HttpContext.RequestAborted), Locations);
    }
}
