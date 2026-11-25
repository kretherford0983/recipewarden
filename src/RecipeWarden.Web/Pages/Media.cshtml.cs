using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages;

/// <summary>
/// Section 7.2 <c>/media/{id}</c>, SEC-52, ADR 0022: serves a stored WebP image (or its thumbnail) from outside the
/// web root. Anyone may open images of published recipes; editors may open any stored image; everything else is
/// 404. Sent with <c>nosniff</c> (global header) and a private cache.
/// </summary>
public sealed class MediaModel(ImageStore images, Permissions permissions) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string id, string? variant)
    {
        if (variant is not (null or "thumb"))
        {
            return NotFound();
        }

        var file = await images.FindAsync(id, variant == "thumb", await permissions.CanEditAsync(User), HttpContext.RequestAborted);
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(file.Path, "image/webp");
    }
}
