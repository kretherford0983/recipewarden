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
public sealed class ReviewModel(ImportService imports, TagService tags, Infrastructure.Ai.AiParserService ai) : RecipeEditorPageModel(tags)
{
    /// <summary>FR-IMP-09: "Re-parse with AI" is offered when the AI parser is on.</summary>
    public bool AiEnabled { get; private set; }

    public string? AiError { get; private set; }

    /// <summary>FR-IMP-09: the fields the AI parser filled, listed and marked on the form.</summary>
    public IReadOnlyList<string> AiFields => Review is { ParsedByAi: true } r
        ? new (string Name, string? Value)[]
            {
                ("Title", r.Recipe.Title), ("Description", r.Recipe.Description), ("Servings", r.Recipe.Servings),
                ("Times", r.Recipe.PrepTime ?? r.Recipe.CookTime ?? r.Recipe.TotalTime), ("Ingredients", r.Recipe.Ingredients),
                ("Steps", r.Recipe.Steps), ("Notes", r.Recipe.Notes),
            }.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList()
        : [];

    /// <summary>FR-IMP-09, ADR 0042: parses the kept raw text again with AI; the result replaces the draft, for review.</summary>
    public async Task<IActionResult> OnPostReparseAsync([FromRoute] int id)
    {
        var error = await imports.ReparseWithAiAsync(id, HttpContext.RequestAborted);
        if (error is null)
        {
            return Redirect($"/manage/import/{id}/review");
        }

        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Input = ToInput(Review!.Recipe);
        CopyImage = Review.CopyImage;
        AiError = error;
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    public const string AlreadyDone = "This import has already been saved or discarded.";

    public int JobId { get; private set; }
    public ImportReview? Review { get; private set; }

    [BindProperty]
    public bool CopyImage { get; set; }

    [BindProperty]
    public bool SaveAnyway { get; set; }

    /// <summary>BR-131: "Save the card photo with the recipe" for a photo import (FR-IMP-13).</summary>
    [BindProperty]
    public bool KeepPhotos { get; set; }

    public bool NeedsDuplicateChoice { get; private set; }

    public override string FormAction => $"/manage/import/{JobId}/review";

    public override bool CreditLocked => Review?.CreditLocked == true;

    /// <summary>AC-144: the options box only when it holds an option (image, card photos or a duplicate).</summary>
    public override string? ExtraPartial => Review is { Status: ImportStatus.Review } r && (r.ImageUrl is not null || r.PhotoCount > 0 || r.Duplicate is not null)
        ? "_ReviewOptions"
        : null;

    public override string? FormClass => Review?.ParsedByAi == true ? "ai-parsed" : null;

    public override string? SecondaryFormId => Review?.Status == ImportStatus.Review ? "discard-import" : null;

    public override string? SecondaryFormLabel => "Discard this import";

    public async Task<IActionResult> OnGetAsync([FromRoute] int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }

        Input = ToInput(Review!.Recipe);
        CopyImage = Review.CopyImage;
        KeepPhotos = Review.KeepPhotos;
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

        var result = Review.PhotoCount > 0
            ? await imports.SaveAsync(id, edit, CopyImage, SaveAnyway, KeepPhotos, Permissions.UserId(User), HttpContext.RequestAborted)
            : await imports.SaveAsync(id, edit, CopyImage, SaveAnyway, Permissions.UserId(User), HttpContext.RequestAborted);
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

    /// <summary>FR-IMP-13: a photo of this import (the copy sent to the AI), for Editors only (the /Manage folder).</summary>
    public async Task<IActionResult> OnGetPhotoAsync([FromRoute] int id, int n) =>
        await imports.PhotoPathAsync(id, n, HttpContext.RequestAborted) is { } path
            ? PhysicalFile(path, "image/jpeg")
            : NotFound();

    /// <summary>BR-125: a folded batch size becomes its own draft.</summary>
    public async Task<IActionResult> OnPostSplitAsync([FromRoute] int id, int index) =>
        await imports.SplitFoldedAsync(id, index, HttpContext.RequestAborted) is { } newId
            ? Redirect($"/manage/import/{newId}/review")
            : NotFound();

    /// <summary>BR-132: keep one version of an ingredient group; drop, move to notes or split the others.</summary>
    public async Task<IActionResult> OnPostVersionsAsync([FromRoute] int id, string? name, string? keep, string? choice) =>
        Enum.GetNames<VersionChoice>().Contains(choice, StringComparer.Ordinal)
        && await imports.ResolveVersionsAsync(id, name, keep, Enum.Parse<VersionChoice>(choice!), HttpContext.RequestAborted) is { } kept
            ? Redirect($"/manage/import/{kept}/review")
            : NotFound();

    public async Task<IActionResult> OnPostDiscardAsync([FromRoute] int id) =>
        await imports.DiscardAsync(id, HttpContext.RequestAborted) ? Redirect("/manage/import") : NotFound();

    private async Task<bool> LoadAsync(int id)
    {
        JobId = id;
        Review = await imports.GetReviewAsync(id, HttpContext.RequestAborted);
        AiEnabled = (await ai.GetSettingsAsync(HttpContext.RequestAborted)).Enabled;
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
