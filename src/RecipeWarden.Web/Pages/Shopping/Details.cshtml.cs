using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Shopping;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Shopping;

/// <summary>
/// FR-SHOP-02 to 04, AC-50, AC-51, D-10, ADR 0028/0029: one list, merged by item and grouped by store section.
/// Anyone may view it and tick items (the one anonymous write: antiforgery and its own rate limit, SEC-30, SEC-63).
/// Adding and removing items or recipes and deleting the list are Editor only.
/// </summary>
public sealed class DetailsModel(ShoppingService shopping, Permissions permissions, UserPreferenceStore preferences, TimeProvider clock)
    : PageModel
{
    public const string CheckHandler = "Check";

    public ShoppingListPage? List { get; private set; }
    public bool CanEdit { get; private set; }
    public string? Error { get; private set; }

    /// <summary>ADR 0028: whole minutes until a finished list is removed (at least 1).</summary>
    public int? MinutesLeft => List?.DeleteAt is { } at ? Math.Max(1, (int)Math.Ceiling((at - clock.GetUtcNow()).TotalMinutes)) : null;

    public async Task<IActionResult> OnGetAsync(int id) => await ShowAsync(id) ?? Page();

    /// <summary>The print layout (FR-SHOP-04).</summary>
    public async Task<IActionResult> OnGetPrintAsync(int id)
    {
        if (await ShowAsync(id) is { } missing)
        {
            return missing;
        }

        ViewData["Print"] = true;
        return Page();
    }

    /// <summary>FR-SHOP-04: plain text or Markdown of the items still to buy.</summary>
    public async Task<IActionResult> OnGetTextAsync(int id, string? format)
    {
        if (await ShowAsync(id) is { } missing)
        {
            return missing;
        }

        var markdown = string.Equals(format, "md", StringComparison.Ordinal);
        var text = markdown ? ShoppingText.Markdown(List!.Name, List.View) : ShoppingText.Plain(List!.Name, List.View);
        return File(Encoding.UTF8.GetBytes(text), markdown ? "text/markdown; charset=utf-8" : "text/plain; charset=utf-8",
            "shopping-list." + (markdown ? "md" : "txt"));
    }

    /// <summary>ADR 0028: tick or untick one item. Anonymous; changes nothing else.</summary>
    public async Task<IActionResult> OnPostCheckAsync(int id, string? key, bool done)
    {
        var result = await shopping.CheckAsync(id, key, done, HttpContext.RequestAborted);
        return result.Ok ? Back(id) : NotFound();
    }

    public async Task<IActionResult> OnPostAddItemAsync(int id, string? item)
    {
        if (await EditorOnlyAsync() is { } denied)
        {
            return denied;
        }

        var added = await shopping.AddItemAsync(id, item, HttpContext.RequestAborted);
        if (added.Ok)
        {
            return Back(id);
        }

        Error = added.Error;
        return await ShowAsync(id) ?? Page();
    }

    public async Task<IActionResult> OnPostRemoveItemAsync(int id, int lineId)
    {
        if (await EditorOnlyAsync() is { } denied)
        {
            return denied;
        }

        await shopping.RemoveItemAsync(id, lineId, HttpContext.RequestAborted);
        return Back(id);
    }

    public async Task<IActionResult> OnPostRemoveRecipeAsync(int id, int listRecipeId)
    {
        if (await EditorOnlyAsync() is { } denied)
        {
            return denied;
        }

        await shopping.RemoveRecipeAsync(id, listRecipeId, HttpContext.RequestAborted);
        return Back(id);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await EditorOnlyAsync() is { } denied)
        {
            return denied;
        }

        await shopping.DeleteAsync(id, HttpContext.RequestAborted);
        return Redirect("/shopping?done=deleted");
    }

    private RedirectResult Back(int id) => Redirect("/shopping/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private async Task<IActionResult?> EditorOnlyAsync()
    {
        if (await permissions.CanEditAsync(User))
        {
            return null;
        }

        return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
    }

    private async Task<IActionResult?> ShowAsync(int id)
    {
        CanEdit = await permissions.CanEditAsync(User);
        var system = await ShoppingUnits.ResolveAsync(HttpContext, preferences);
        List = await shopping.GetAsync(id, system, HttpContext.RequestAborted);
        return List is null ? NotFound() : null;
    }
}
