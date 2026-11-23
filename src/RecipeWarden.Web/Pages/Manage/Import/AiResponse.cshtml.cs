using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.AiExchange;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage.Import;

/// <summary>
/// FR-EXP-05, AC-27, AC-29, BR-92, BR-93, ADR 0033: paste or upload an AI reply, review it against the recipe, then save
/// it as a variant (default) or replace the original. Editor only (folder policy). The reply is read again on save.
/// </summary>
public sealed class AiResponseModel(AiExchangeService ai, Permissions permissions) : PageModel
{
    public const string BadFile = "Upload the reply as a .md or .txt file.";

    [BindProperty(SupportsGet = true, Name = "parent")]
    public string? Parent { get; set; }

    [BindProperty(Name = "reply")]
    public string? Reply { get; set; }

    public IReadOnlyList<AiReviewItem>? Items { get; private set; }
    public string? Error { get; private set; }

    public void OnGet() => Parent = SafeParent(Parent);

    private static string? SafeParent(string? slug) => SlugRules.IsValid(slug) ? slug : null;

    public async Task<IActionResult> OnPostReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        Parent = SafeParent(Parent);
        if (file is { Length: > 0 })
        {
            await using var stream = file.OpenReadStream();
            var (upload, error) = await UploadValidator.ReadDocumentAsync(stream, file.FileName, file.Length, cancellationToken);
            if (upload is null || upload.Kind is not (UploadKind.Markdown or UploadKind.Text))
            {
                Error = error ?? BadFile;
                return Page();
            }

            Reply = System.Text.Encoding.UTF8.GetString(upload.Bytes);
        }

        if (string.IsNullOrWhiteSpace(Reply))
        {
            Error = "Paste the assistant's reply, or upload it as a file.";
            return Page();
        }

        if (Reply.Length > AiReplyReader.MaxReplyLength)
        {
            Error = "The reply is too long (512 KB at most).";
            return Page();
        }

        Items = await ai.ReviewAsync(Reply, Parent, cancellationToken);
        if (Items.Count == 0)
        {
            Error = AiReplyReader.NoBlock;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int block, int version, string? mode, CancellationToken cancellationToken)
    {
        Parent = SafeParent(Parent);
        var saveMode = mode == "replace" ? AiSaveMode.Replace : AiSaveMode.Variant;
        var publish = mode is "publish" or "replace";
        var outcome = await ai.SaveAsync(Reply, block, Parent, saveMode, publish, version, Permissions.UserId(User),
            await permissions.IsAdminAsync(User), cancellationToken);
        switch (outcome.Status)
        {
            case SaveStatus.Saved:
                return outcome.RecipeStatus == RecipeStatus.Published
                    ? Redirect("/recipes/" + outcome.Slug)
                    : Redirect($"/manage/recipes/{outcome.Id}/edit?saved=draft");
            case SaveStatus.Conflict:
                Error = AiExchangeService.Changed;
                break;
            case SaveStatus.Invalid:
                Error = string.Join(" ", outcome.Errors.Concat(outcome.PublishProblems).Select(p => p.Message));
                break;
            default:
                Error = "That part of the reply cannot be saved. Review it again.";
                break;
        }

        Items = await ai.ReviewAsync(Reply, Parent, cancellationToken);
        return Page();
    }
}
