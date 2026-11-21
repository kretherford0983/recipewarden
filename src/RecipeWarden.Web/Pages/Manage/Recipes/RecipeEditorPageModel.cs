using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>FR-REC-02, W2: shared by the new and edit pages. Editor-only by the /Manage folder policy (SEC-10, SEC-14).</summary>
public abstract class RecipeEditorPageModel(TagService tags) : PageModel
{
    public const string PublishAction = "publish";

    [BindProperty]
    public RecipeInput Input { get; set; } = new();

    /// <summary>"publish" or "draft" from the button pressed.</summary>
    [BindProperty]
    public string? Action { get; set; }

    public IReadOnlyList<PublishProblem> PublishProblems { get; protected set; } = [];
    public IReadOnlyList<FieldDifference> Differences { get; protected set; } = [];
    public IReadOnlyList<string> KnownTags { get; private set; } = [];
    public string? Message { get; protected set; }
    public bool IsAdmin { get; protected set; }

    public abstract string FormAction { get; }

    /// <summary>W2 step 3: live parse of the ingredient lines, shown as chips (htmx partial; POST with antiforgery).</summary>
    public PartialViewResult OnPostParse() =>
        Partial("_IngredientPreview", IngredientLineParser.ParseList(Input.Ingredients));

    protected async Task LoadTagsAsync() =>
        KnownTags = (await tags.ListAsync(publishedOnly: false, HttpContext.RequestAborted)).Select(t => t.Name).ToList();

    /// <summary>Puts a save outcome on the page (UX-08 errors, BR-06 conflict) and returns the page.</summary>
    protected async Task<IActionResult> ShowAsync(SaveOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        foreach (var e in outcome.Errors)
        {
            ModelState.AddModelError(RecipeInput.FieldName(e.Field), e.Message);
        }

        if (outcome.Status == SaveStatus.Conflict)
        {
            Differences = outcome.Differences;
            Message = RecipeDiff.ConflictMessage;
            // Saving again from here is a deliberate overwrite of the newer version.
            ModelState.Remove("Input.Version");
            Input.Version = outcome.Version;
        }

        await LoadTagsAsync();
        return Page();
    }

    protected bool WantsPublish => string.Equals(Action, PublishAction, StringComparison.Ordinal);

    /// <summary>Reads the form; text that cannot be read (servings, times) is put on the page instead of saved.</summary>
    protected RecipeEdit? ReadInput()
    {
        var (edit, errors) = Input.ToEdit(WantsPublish);
        foreach (var e in errors)
        {
            ModelState.AddModelError(RecipeInput.FieldName(e.Field), e.Message);
        }

        return errors.Count == 0 ? edit : null;
    }

    /// <summary>Binding errors (e.g. "abc" in a number field) become the same summary as rule errors.</summary>
    protected bool HasBindingErrors()
    {
        foreach (var (key, entry) in ModelState)
        {
            if (entry.Errors.Count > 0 && key.StartsWith("Input.", StringComparison.Ordinal))
            {
                entry.Errors.Clear();
                ModelState.AddModelError(key, "Enter a number");
                return true;
            }
        }

        return false;
    }
}
