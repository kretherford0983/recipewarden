using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>
/// FR-SCL-06, AC-39, ADR 0019: "I made this" — records the cook date and, optionally, the measured yield, which
/// container scaling then prefers (BR-111). Editor-only by the /Manage folder policy; the recipe page links here.
/// </summary>
public sealed class CookedModel(RecipeService recipes) : PageModel
{
    public RecipeForEdit? Recipe { get; private set; }
    public string? Error { get; private set; }

    [BindProperty]
    public string? Made { get; set; }

    public async Task<IActionResult> OnGetAsync([FromRoute] int id)
    {
        Recipe = await recipes.GetForEditAsync(id, HttpContext.RequestAborted);
        return Recipe is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync([FromRoute] int id)
    {
        if (Made is { Length: > AmountText.MaxLength })
        {
            Made = null;
            Error = "Enter what it made as an amount and unit, e.g. 12 fl oz";
            return await OnGetAsync(id);
        }

        var outcome = await recipes.MarkCookedAsync(id, Made, Permissions.UserId(User), HttpContext.RequestAborted);
        if (outcome.Status == SaveStatus.NotFound)
        {
            return NotFound();
        }

        if (outcome.Status == SaveStatus.Invalid)
        {
            Error = outcome.Errors[0].Message;
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return await OnGetAsync(id);
        }

        return Redirect("/recipes/" + outcome.Slug + "?made=1");
    }
}
