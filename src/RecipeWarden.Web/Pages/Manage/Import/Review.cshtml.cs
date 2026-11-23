using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Pages.Manage.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Import;

/// <summary>
/// FR-IMP-06, FR-IMP-07, W3 step 3, ADR 0025: the review page is the recipe editor filled from the import, with the
/// warnings above it, a possible-duplicate choice (AC-16), the image opt-in (BR-12) and, for web imports, a locked
/// credit (BR-10). Nothing is saved until the editor presses Publish or Save draft. The job id comes from the route.
/// </summary>
public sealed class ReviewModel(ImportService imports, TagService tags) : RecipeEditorPageModel(tags)
{
    public const string AlreadyDone = "This import has already been saved or discarded.";

    public int JobId { get; private set; }
    public ImportReview? Review { get; private set; }

    [BindProperty]
    public bool CopyImage { get; set; }

    [BindProperty]
    public bool SaveAnyway { get; set; }

    public bool NeedsDuplicateChoice { get; private set; }

    public override string FormAction => $"/manage/import/{JobId}/review";

    public override bool CreditLocked => Review?.CreditLocked == true;

    public override string? ExtraPartial => Review?.Status == ImportStatus.Review ? "_ReviewOptions" : null;

    public async Task<IActionResult> OnGetAsync([FromRoute] int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Input = ToInput(Review!.Recipe);
        CopyImage = Review.CopyImage;
        if (Review.Status != ImportStatus.Review)
        {
            Message = AlreadyDone;
        }

        await LoadTagsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync([FromRoute] int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        if (Review!.Status != ImportStatus.Review)
        {
            Message = AlreadyDone;
            await LoadTagsAsync();
            Response.StatusCode = StatusCodes.Status409Conflict;
            return Page();
        }

        if (HasBindingErrors() || await ReadInputAsync() is not { } edit)
        {
            await LoadTagsAsync();
            return Page();
        }

        var result = await imports.SaveAsync(id, edit, CopyImage, SaveAnyway, Permissions.UserId(User), HttpContext.RequestAborted);
        if (result.NeedsDuplicateChoice)
        {
            NeedsDuplicateChoice = true;
            ModelState.AddModelError(nameof(SaveAnyway), ImportService.DuplicateChoiceNeeded);
            await LoadTagsAsync();
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        var outcome = result.Outcome!;
        if (outcome.Status == SaveStatus.NotFound)
        {
            return NotFound();
        }

        if (outcome.Status != SaveStatus.Saved)
        {
            return await ShowAsync(outcome);
        }

        var image = result.ImageWarning is null ? string.Empty : "image=failed";
        return outcome.RecipeStatus == RecipeStatus.Published
            ? Redirect("/recipes/" + outcome.Slug + (image.Length > 0 ? "?" + image : string.Empty))
            : Redirect($"/manage/recipes/{outcome.Id}/edit?saved=draft" + (image.Length > 0 ? "&" + image : string.Empty));
    }

    public async Task<IActionResult> OnPostDiscardAsync([FromRoute] int id) =>
        await imports.DiscardAsync(id, HttpContext.RequestAborted) ? Redirect("/manage/import") : NotFound();

    private async Task<bool> LoadAsync(int id)
    {
        JobId = id;
        Review = await imports.GetReviewAsync(id, HttpContext.RequestAborted);
        return Review is not null;
    }

    /// <summary>The import's text fields, as the editor form takes them (ADR 0025).</summary>
    private static RecipeInput ToInput(ImportedRecipe r) => new()
    {
        Title = r.Title,
        Description = r.Description,
        Servings = r.Servings,
        PrepTime = r.PrepTime,
        CookTime = r.CookTime,
        TotalTime = r.TotalTime,
        IsPrecision = r.IsPrecision,
        Notes = r.Notes,
        SourceType = r.SourceType ?? SourceType.Manual,
        SourceName = r.SourceName,
        SourceUrl = r.SourceUrl,
        SourceAuthor = r.SourceAuthor,
        Ingredients = r.Ingredients,
        Steps = r.Steps,
        Tags = string.Join(", ", r.Tags),
        Yield = r.Yield,
        FinishedDensity = r.FinishedDensity,
        Pan = r.Pan,
    };
}
