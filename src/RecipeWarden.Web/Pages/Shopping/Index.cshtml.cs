using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Shopping;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Shopping;

/// <summary>FR-SHOP-01, D-10, ADR 0028: the open lists. Anyone may view; editors start, reuse and delete lists.</summary>
public sealed class IndexModel(ShoppingService shopping, Permissions permissions) : PageModel
{
    public IReadOnlyList<ShoppingListSummary> Lists { get; private set; } = [];
    public IReadOnlyList<string> Remembered { get; private set; } = [];
    public bool CanEdit { get; private set; }
    public string? Message { get; private set; }

    public async Task OnGetAsync(string? done)
    {
        var ct = HttpContext.RequestAborted;
        CanEdit = await permissions.CanEditAsync(User);
        Lists = await shopping.ListAsync(ct);
        if (CanEdit)
        {
            Remembered = await shopping.RememberedAsync(ct);
        }

        Message = done == "deleted" ? "List deleted." : null;
    }
}
