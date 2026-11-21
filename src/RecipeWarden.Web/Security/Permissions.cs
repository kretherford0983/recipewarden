using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Security;

/// <summary>SEC-11, SEC-14: policy checks for pages that show more to editors, and for handlers that re-check writes.</summary>
public sealed class Permissions(IAuthorizationService authorization)
{
    public async Task<bool> CanEditAsync(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (await authorization.AuthorizeAsync(user, AppPolicies.CanEditRecipes).ConfigureAwait(false)).Succeeded;

    public async Task<bool> IsAdminAsync(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (await authorization.AuthorizeAsync(user, AppPolicies.CanAdminister).ConfigureAwait(false)).Succeeded;

    public static string UserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Signed-in user has no id.");

    public static IReadOnlyList<string> Roles(ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
}
