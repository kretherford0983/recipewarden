using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>
/// FR-AUTH-04, SEC-04, AC-73: turn two-factor sign-in on with an authenticator app (QR code or the key typed in),
/// confirm with a code, get 10 recovery codes shown once; make new recovery codes; turn it off. Every change updates
/// the security stamp (other sessions end within 5 minutes) and is audited (User.Update).
/// </summary>
public sealed class TwoFactorModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, SettingsStore settings) : PageModel
{
    public bool Enabled { get; private set; }

    public int RecoveryCodesLeft { get; private set; }

    public string? SharedKey { get; private set; }

    /// <summary>The QR code as an SVG data URI (CSP allows data: images; no script).</summary>
    public string? QrDataUri { get; private set; }

    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : Challenge();

    /// <summary>Starts setup: a new shared key (an unfinished setup can be restarted safely).</summary>
    public async Task<IActionResult> OnPostStartAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await users.ResetAuthenticatorKeyAsync(user);
        await LoadAsync(showKey: true);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(string? code)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var totp = AccountRules.NormalizeTotp(code);
        if (totp is null || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, totp))
        {
            Error = "That code did not match. Check the time on your phone and try the next code.";
            await LoadAsync(showKey: true);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, AccountRules.RecoveryCodeCount))?.ToList() ?? [];
        await signIn.RefreshSignInAsync(user);
        Message = "Two-factor sign-in is on. Save these recovery codes now; each works once, and they are not shown again.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRecoveryCodesAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || !user.TwoFactorEnabled)
        {
            return RedirectToPage();
        }

        RecoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, AccountRules.RecoveryCodeCount))?.ToList() ?? [];
        Message = "New recovery codes. The old ones no longer work. Save these now; they are not shown again.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync(string? code)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        // Turning it off needs a current code, so a borrowed session cannot quietly remove it.
        var totp = AccountRules.NormalizeTotp(code);
        if (totp is null || !await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, totp))
        {
            Error = "Enter a current code from your authenticator app to turn two-factor sign-in off.";
            await LoadAsync();
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        Message = "Two-factor sign-in is off.";
        await LoadAsync();
        return Page();
    }

    private async Task<bool> LoadAsync(bool showKey = false)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return false;
        }

        Enabled = user.TwoFactorEnabled;
        RecoveryCodesLeft = Enabled ? await users.CountRecoveryCodesAsync(user) : 0;
        if (showKey && !Enabled && await users.GetAuthenticatorKeyAsync(user) is { } key)
        {
            SharedKey = AccountRules.FormatKey(key);
            var issuer = (await settings.GetAsync(SettingKeys.SiteName, HttpContext.RequestAborted)) ?? SiteSettings.DefaultSiteName;
            var uri = AccountRules.AuthenticatorUri(issuer, user.Email ?? user.UserName ?? "user", key);
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
            var svg = new SvgQRCode(data).GetGraphic(6);
            QrDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
        }

        return true;
    }
}
