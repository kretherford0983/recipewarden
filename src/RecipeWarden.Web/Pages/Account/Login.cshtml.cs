using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Web.Theme;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>SEC-01, SEC-02, SEC-05: sign-in only; there is no registration page.</summary>
public sealed class LoginModel(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> users,
    UserPreferenceStore preferences,
    AuditLog audit) : PageModel
{
    /// <summary>SEC-13: dedicated input DTO with only the bound fields.</summary>
    public sealed class LoginInput
    {
        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(1024)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public string? Notice { get; private set; }

    public void OnGet(string? setup)
    {
        Notice = setup switch
        {
            "done" => "Setup is complete. Sign in with the Admin account you just created.",
            "restored" => "The backup was restored. Sign in with an account from it.",
            _ => null,
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = AccountRules.InvalidSignInMessage;
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            // FR-ADM-01: failed sign-ins are audited with the address typed (never the password, SEC-72).
            await audit.RecordAsAsync(new AuditActorInfo(null, "anonymous", HttpContext.Connection.RemoteIpAddress?.ToString()), "Account.SignInFailed",
                null, null, new { email = Core.Audit.AuditRules.Truncate(Input.Email, 256), lockedOut = result.IsLockedOut }, HttpContext.RequestAborted);

            // SEC-02: same message for unknown user, wrong password and locked account.
            ErrorMessage = AccountRules.InvalidSignInMessage;
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email);
        await audit.RecordAsAsync(new AuditActorInfo(user?.Id, user?.Email ?? Input.Email, HttpContext.Connection.RemoteIpAddress?.ToString()), "Account.SignIn",
            "User", user?.Id, null, HttpContext.RequestAborted);
        if (user is not null && await preferences.GetThemeAsync(user.Id, HttpContext.RequestAborted) is { } theme)
        {
            ThemeResolver.WriteCookie(Response, theme);
        }

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
