using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>
/// SEC-04, AC-73: the second sign-in step. Reached only after a correct password for an account with two-factor on
/// (Identity's short-lived two-factor cookie); takes a 6-digit authenticator code or a recovery code. Failures count
/// toward the lockout (SEC-02) and the sign-in rate limit (SEC-63); the message never says which part was wrong.
/// </summary>
public sealed class LoginTwoFactorModel(SignInManager<AppUser> signIn, AuditLog audit) : PageModel
{
    public const string NoPendingSignIn = "Sign in with your email and password first.";

    [BindProperty(SupportsGet = true)]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public bool Pending { get; private set; }

    public async Task OnGetAsync() =>
        Pending = await signIn.GetTwoFactorAuthenticationUserAsync() is not null;

    public async Task<IActionResult> OnPostAsync(string? code, string? recoveryCode)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            ErrorMessage = NoPendingSignIn;
            return Page();
        }

        Pending = true;
        SignInResult result;
        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            result = await signIn.TwoFactorRecoveryCodeSignInAsync(recoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal).Trim());
        }
        else if (AccountRules.NormalizeTotp(code) is { } totp)
        {
            result = await signIn.TwoFactorAuthenticatorSignInAsync(totp, RememberMe, rememberClient: false);
        }
        else
        {
            result = SignInResult.Failed;
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!result.Succeeded)
        {
            await audit.RecordAsAsync(new AuditActorInfo(user.Id, user.Email ?? "unknown", ip), "Account.TwoFactorFailed", "User", user.Id,
                new { lockedOut = result.IsLockedOut }, HttpContext.RequestAborted);
            ErrorMessage = AccountRules.InvalidSignInMessage;
            return Page();
        }

        await audit.RecordAsAsync(new AuditActorInfo(user.Id, user.Email ?? "unknown", ip), "Account.SignIn", "User", user.Id,
            new { twoFactor = true, recovery = !string.IsNullOrWhiteSpace(recoveryCode) }, HttpContext.RequestAborted);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
