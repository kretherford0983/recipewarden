using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Manage;

/// <summary>
/// FR-REC-04, BR-05, AC-04: trash with restore (Editors) and delete-for-good (Admins only; re-checked in the
/// handler per SEC-14, so an Editor posting the purge form directly gets 403). UX-07: undo after a delete.
/// </summary>
public sealed class TrashModel(RecipeService recipes, Permissions permissions) : PageModel
{
    public IReadOnlyList<TrashItem> Items { get; private set; } = [];
    public bool IsAdmin { get; private set; }
    public string? Message { get; private set; }
    public int? UndoId { get; private set; }

    public async Task OnGetAsync(int? deleted, string? done)
    {
        Items = await recipes.ListTrashAsync(HttpContext.RequestAborted);
        IsAdmin = await permissions.IsAdminAsync(User);
        if (deleted is int id && Items.Any(i => i.Id == id))
        {
            Message = "Moved to the trash.";
            UndoId = id;
        }
        else
        {
            Message = done switch
            {
                "restored" => "Restored.",
                "purged" => "Deleted for good.",
                "expired" => "That recipe can no longer be restored.",
                _ => null,
            };
        }
    }

    public async Task<IActionResult> OnPostRestoreAsync(int id)
    {
        var restored = await recipes.RestoreAsync(id, HttpContext.RequestAborted);
        return Redirect("/manage/trash?done=" + (restored ? "restored" : "expired"));
    }

    public async Task<IActionResult> OnPostPurgeAsync(int id)
    {
        if (!await permissions.IsAdminAsync(User))
        {
            return Forbid(); // SEC-14, BR-05
        }

        await recipes.PurgeAsync(id, Permissions.Roles(User), HttpContext.RequestAborted);
        return Redirect("/manage/trash?done=purged");
    }
}
