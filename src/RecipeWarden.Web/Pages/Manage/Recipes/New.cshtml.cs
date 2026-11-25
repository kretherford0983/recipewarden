using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>FR-REC-02, W2: add a recipe by hand. Publishes when BR-01 allows, else saves a Draft (AC-01, AC-02).</summary>
public sealed class NewModel(RecipeService recipes, TagService tags) : RecipeEditorPageModel(tags)
{
    public override string FormAction => "/manage/recipes/new";

    public async Task OnGetAsync() => await LoadTagsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (HasBindingErrors() || await ReadInputAsync() is not { } edit)
        {
            await LoadTagsAsync();
            return Page();
        }

        var outcome = await recipes.CreateAsync(edit, Permissions.UserId(User), HttpContext.RequestAborted);
        if (outcome.Status != SaveStatus.Saved)
        {
            return await ShowAsync(outcome);
        }

        return outcome.RecipeStatus == RecipeStatus.Published
            ? Redirect("/recipes/" + outcome.Slug)
            : Redirect($"/manage/recipes/{outcome.Id}/edit?saved=draft");
    }
}
