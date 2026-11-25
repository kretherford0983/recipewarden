using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Shopping;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Shopping;

/// <summary>
/// FR-SHOP-01, ADR 0028: start a list (or start again from a remembered name), and add a recipe at its current scale
/// from the recipe page. Editor only (page policy).
/// </summary>
public sealed class NewModel(ShoppingService shopping) : PageModel
{
    public const string DefaultName = "Shopping";

    public string? Error { get; private set; }

    [BindProperty]
    public string? Name { get; set; }

    /// <summary>FR-SHOP-05: open the staples walk-through first.</summary>
    [BindProperty]
    public bool StaplesCheck { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var created = await shopping.CreateAsync(Name, Permissions.UserId(User), HttpContext.RequestAborted);
        if (!created.Ok)
        {
            Error = created.Error;
            return Page();
        }

        return Redirect("/shopping/" + created.Id + (StaplesCheck ? "/staples" : string.Empty));
    }

    /// <summary>From the recipe page: an existing list, or a new one named in the same form.</summary>
    public async Task<IActionResult> OnPostAddRecipeAsync(string? slug, string? scale, int? listId, string? newName)
    {
        var ct = HttpContext.RequestAborted;
        if (!SlugRules.IsValid(slug))
        {
            return NotFound();
        }

        var factor = ScaleRules.TryParseNumber(scale, out var f) ? f : 1m;
        // A typed name starts a new list even when a list is selected.
        var id = string.IsNullOrWhiteSpace(newName) ? listId : null;
        if (id is null)
        {
            var created = await shopping.CreateAsync(string.IsNullOrWhiteSpace(newName) ? DefaultName : newName, Permissions.UserId(User), ct);
            if (!created.Ok)
            {
                Error = created.Error;
                return Page();
            }

            id = created.Id;
        }

        var added = await shopping.AddRecipeAsync(id!.Value, slug!, factor, ct);
        if (!added.Ok)
        {
            Error = added.Error;
            return Page();
        }

        return Redirect("/recipes/" + slug + "?added=" + id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (factor != 1m ? "&scale=" + ScaleRules.ToQuery(factor) : string.Empty));
    }
}
