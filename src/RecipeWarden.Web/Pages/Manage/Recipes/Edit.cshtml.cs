using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Recipes;

/// <summary>
/// FR-REC-02, BR-06: edit a recipe; a stale version is refused with a diff. Delete moves it to the trash (BR-05).
/// SEC-13: the recipe id comes from the route only. MVC binds form values before route values, so a posted
/// "id" field would otherwise retarget the edit (found by AC_72_OverpostingTests).
/// </summary>
[RequestSizeLimit(PhotoRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestBytes)]
public sealed class EditModel(RecipeService recipes, TagService tags, Permissions permissions, ImageStore images) : RecipeEditorPageModel(tags)
{
    /// <summary>A little over the 10 MB image cap, so an 11 MB photo gets a clear message (SEC-51).</summary>
    public const long PhotoRequestBytes = 12L * 1024 * 1024;

    public const string ChoosePhoto = "Choose a photo to upload";

    /// <summary>ADR 0022: the finish image.</summary>
    public string? FinishImageId { get; private set; }

    /// <summary>ADR 0023: the saved steps and their photos.</summary>
    public IReadOnlyList<StepPhoto> StepPhotos { get; private set; } = [];

    public string? PhotoError { get; private set; }

    public int Id { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public RecipeStatus Status { get; private set; }

    public override string FormAction => $"/manage/recipes/{Id}/edit";

    public async Task<IActionResult> OnGetAsync([FromRoute] int id, string? saved, string? image)
    {
        var current = await recipes.GetForEditAsync(id, HttpContext.RequestAborted);
        if (current is null)
        {
            return NotFound();
        }

        if (image == "failed")
        {
            PhotoError = Infrastructure.Import.ImportService.ImageNotCopied + ". Add a photo below.";
        }

        SetCurrent(current);
        Input = RecipeInput.From(current.Edit, current.Version);
        if (string.IsNullOrEmpty(Input.Pan) && Core.Kitchen.PanRules.FromSteps(current.Edit.StepsText) is { } pan)
        {
            Input.Pan = Core.Kitchen.PanRules.Describe(pan); // ADR 0020: prefilled, saved only if the editor saves the form
            PanFromSteps = true;
        }
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
        if (HasBindingErrors() || await ReadInputAsync() is not { } edit)
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

    /// <summary>ADR 0022, FR-REC-01: upload or replace the finish image (SEC-50 to SEC-52).</summary>
    public Task<IActionResult> OnPostFinishImageAsync([FromRoute] int id, IFormFile? photo) => SavePhotoAsync(id, photo, null);

    /// <summary>ADR 0023: upload or replace the photo of the step at position <paramref name="step"/>.</summary>
    public Task<IActionResult> OnPostStepPhotoAsync([FromRoute] int id, int step, IFormFile? photo) => SavePhotoAsync(id, photo, step);

    public async Task<IActionResult> OnPostRemoveFinishImageAsync([FromRoute] int id) =>
        await recipes.SetFinishImageAsync(id, null, Permissions.UserId(User), HttpContext.RequestAborted)
            ? Redirect($"/manage/recipes/{id}/edit#photos")
            : NotFound();

    public async Task<IActionResult> OnPostRemoveStepPhotoAsync([FromRoute] int id, int step) =>
        await recipes.SetStepImageAsync(id, step, null, Permissions.UserId(User), HttpContext.RequestAborted)
            ? Redirect($"/manage/recipes/{id}/edit#photos")
            : NotFound();

    private async Task<IActionResult> SavePhotoAsync(int id, IFormFile? photo, int? step)
    {
        var ct = HttpContext.RequestAborted;
        var current = await recipes.GetForEditAsync(id, ct);
        if (current is null || (step is { } s && (s < 0 || s >= current.StepPhotos.Count)))
        {
            return NotFound();
        }

        string? error;
        if (photo is null || photo.Length == 0)
        {
            error = ChoosePhoto;
        }
        else
        {
            await using var stream = photo.OpenReadStream();
            var (upload, uploadError) = await UploadValidator.ReadImageAsync(stream, photo.FileName, photo.Length, ct);
            error = uploadError;
            if (upload is not null)
            {
                var (imageId, imageError) = await images.SaveAsync(upload.Bytes, null, Permissions.UserId(User), ct);
                error = imageError;
                if (imageId is not null)
                {
                    var userId = Permissions.UserId(User);
                    _ = step is { } index
                        ? await recipes.SetStepImageAsync(id, index, imageId, userId, ct)
                        : await recipes.SetFinishImageAsync(id, imageId, userId, ct);
                    return Redirect($"/manage/recipes/{id}/edit#photos");
                }
            }
        }

        // Show the editor again with the reason (UX-08), as the GET would.
        await OnGetAsync(id, null, null);
        PhotoError = error;
        Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        return Page();
    }

    private void SetCurrent(RecipeForEdit current)
    {
        Id = current.Id;
        Slug = current.Slug;
        Status = current.Status;
        FinishImageId = current.FinishImageId;
        StepPhotos = current.StepPhotos;
    }
}
