using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>
/// FR-REC-02, BR-06: edit a recipe; a stale version is refused with a diff. Delete moves it to the trash (BR-05).
/// SEC-13: the recipe id comes from the route only. MVC binds form values before route values, so a posted
/// "id" field would otherwise retarget the edit (found by AC_72_OverpostingTests).
/// </summary>
public sealed class EditModel(RecipeService recipes, TagService tags, Permissions permissions) : RecipeEditorPageModel(tags)
{
    public int Id { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public RecipeStatus Status { get; private set; }

    public override string FormAction => $"/manage/recipes/{Id}/edit";

    public async Task<IActionResult> OnGetAsync([FromRoute] int id, string? saved)
    {
        var current = await recipes.GetForEditAsync(id, HttpContext.RequestAborted);
        if (current is null)
        {
            return NotFound();
        }

        SetCurrent(current);
        Input = RecipeInput.From(current.Edit, current.Version);
        if (current.Status == RecipeStatus.Draft)
        {
            // AC-02: say what is missing before it can be published.
            var e = current.Edit;
            PublishProblems = PublishRules.Check(e.Title, IngredientLineParser.ParseList(e.IngredientsText).Count(l => !l.IsHeading),
                StepTextRules.Split(e.StepsText).Count).Problems;
            Message = saved == "draft" ? "Saved as a draft." : "This recipe is a draft.";
        }

        IsAdmin = await permissions.IsAdminAsync(User);
        await LoadTagsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync([FromRoute] int id)
    {
        var current = await recipes.GetForEditAsync(id, HttpContext.RequestAborted);
        if (current is null)
        {
            return NotFound();
        }

        SetCurrent(current);
        IsAdmin = await permissions.IsAdminAsync(User);
        if (HasBindingErrors() || ReadInput() is not { } edit)
        {
            await LoadTagsAsync();
            return Page();
        }

        var outcome = await recipes.UpdateAsync(id, edit, Input.Version, Permissions.UserId(User), IsAdmin,
            HttpContext.RequestAborted);
        return outcome.Status switch
        {
            SaveStatus.NotFound => NotFound(),
            SaveStatus.Saved when outcome.RecipeStatus == RecipeStatus.Published => Redirect("/recipes/" + outcome.Slug),
            SaveStatus.Saved => Redirect($"/manage/recipes/{id}/edit?saved=draft"),
            _ => await ShowAsync(outcome),
        };
    }

    /// <summary>BR-05, UX-07: moves to the trash (restorable; the trash page offers undo).</summary>
    public async Task<IActionResult> OnPostDeleteAsync([FromRoute] int id) =>
        await recipes.DeleteAsync(id, Permissions.UserId(User), HttpContext.RequestAborted)
            ? Redirect($"/manage/trash?deleted={id}")
            : NotFound();

    private void SetCurrent(RecipeForEdit current)
    {
        Id = current.Id;
        Slug = current.Slug;
        Status = current.Status;
    }
}
