using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Web.Theme;

namespace RecipeWarden.Web.Pages.Account;

/// <summary>SEC-01, SEC-02, SEC-05: sign-in only; there is no registration page.</summary>
public sealed class LoginModel(
    SignInManager<AppUser> signInManager,
    UserManager<AppUser> users,
    UserPreferenceStore preferences) : PageModel
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

    public void OnGet()
    {
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
            // SEC-02: same message for unknown user, wrong password and locked account.
            ErrorMessage = AccountRules.InvalidSignInMessage;
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is not null && await preferences.GetThemeAsync(user.Id, HttpContext.RequestAborted) is { } theme)
        {
            ThemeResolver.WriteCookie(Response, theme);
        }

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
