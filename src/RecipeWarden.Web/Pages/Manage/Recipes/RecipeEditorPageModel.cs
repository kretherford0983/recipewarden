using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>One group of the tag picker (ADR 0016): a tag type, or "Other" for untyped tags.</summary>
public sealed record TagGroup(string Label, IReadOnlyList<TagSummary> Tags);

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
    public const string UnknownTag = "A ticked tag no longer exists; it may have been deleted. Check the tags and save again.";

    /// <summary>ADR 0016: every tag, grouped by type for the picker.</summary>
    public IReadOnlyList<TagGroup> TagGroups { get; private set; } = [];
    public string? Message { get; protected set; }
    public bool IsAdmin { get; protected set; }

    /// <summary>ADR 0020: the pan field was prefilled from the steps.</summary>
    public bool PanFromSteps { get; protected set; }

    public abstract string FormAction { get; }

    /// <summary>W2 step 3: live parse of the ingredient lines, shown as chips (htmx partial; POST with antiforgery).</summary>
    public PartialViewResult OnPostParse() =>
        Partial("_IngredientPreview", IngredientLineParser.ParseList(Input.Ingredients));

    private IReadOnlyList<TagSummary> allTags = [];

    /// <summary>
    /// Loads the picker and moves typed names that match an existing tag into the ticked set, so the "New tags" box
    /// only holds names that are really new.
    /// </summary>
    protected async Task LoadTagsAsync()
    {
        allTags = await tags.ListAsync(publishedOnly: false, HttpContext.RequestAborted);
        TagGroups = Enum.GetValues<TagType>().Select(t => (TagType?)t).Append(null)
            .Select(type => new TagGroup(type?.ToString() ?? "Other", allTags.Where(x => x.TagType == type)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(g => g.Tags.Count > 0)
            .ToList();

        var byName = allTags.ToDictionary(t => NameRules.Normalize(t.Name), t => t.Id, StringComparer.Ordinal);
        var typed = SplitNames(Input.Tags);
        var ticked = Input.TagIds.ToHashSet();
        ticked.UnionWith(typed.Where(n => byName.ContainsKey(NameRules.Normalize(n))).Select(n => byName[NameRules.Normalize(n)]));
        Input.TagIds = ticked.Order().ToArray();
        Input.Tags = string.Join(", ", typed.Where(n => !byName.ContainsKey(NameRules.Normalize(n))));
    }

    public bool IsTicked(int tagId) => Input.TagIds.Contains(tagId);

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
    protected async Task<RecipeEdit?> ReadInputAsync()
    {
        // ADR 0016: ticked tags plus the names typed in "New tags". An id that is not a tag is refused, not ignored.
        var known = (await tags.ListAsync(publishedOnly: false, HttpContext.RequestAborted)).ToDictionary(t => t.Id, t => t.Name);
        if (Input.TagIds.Any(id => !known.ContainsKey(id)))
        {
            ModelState.AddModelError(RecipeInput.FieldName(RecipeField.Tags), UnknownTag);
            Input.TagIds = Input.TagIds.Where(known.ContainsKey).ToArray();
            return null;
        }

        Input.Tags = string.Join(", ", Input.TagIds.Distinct().Select(id => known[id]).Concat(SplitNames(Input.Tags)));
        Input.TagIds = [];
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

    private static List<string> SplitNames(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
