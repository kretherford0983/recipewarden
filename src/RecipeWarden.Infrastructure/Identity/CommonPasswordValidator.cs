using System.Reflection;
using Microsoft.AspNetCore.Identity;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Identity;

/// <summary>
/// SEC-01, ADR 0043: refuses passwords on a bundled list of common passwords (12+ characters, from SecLists, MIT) and
/// passwords that are the account's own email. Length is checked by Identity's own rule (12).
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<AppUser>
{
    public const string TooCommon = "That password is too common. Use a few unrelated words, or a password manager.";
    public const string IsEmail = "The password cannot be your email address.";

    private static readonly Lazy<HashSet<string>> List = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static int Count => List.Value.Count;

    public static bool IsCommon(string? password) =>
        password is not null && List.Value.Contains(password.Trim().ToLowerInvariant());

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (IsCommon(password))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "CommonPassword", Description = TooCommon }));
        }

        var trimmed = password?.Trim();
        if (trimmed is not null && (string.Equals(trimmed, user.Email, StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, user.UserName, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "PasswordIsEmail", Description = IsEmail }));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    private static HashSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecipeWarden.Seed.common-passwords.txt")
            ?? throw new InvalidOperationException("The common-password list is missing from the build.");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                set.Add(line);
            }
        }

        return set;
    }
}
