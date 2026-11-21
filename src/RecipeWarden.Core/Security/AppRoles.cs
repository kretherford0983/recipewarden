namespace RecipeWarden.Core.Security;

/// <summary>SEC-11, D-04: the two roles. Code checks policies, never these strings directly.</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Editor = "Editor";

    public static IReadOnlyList<string> All { get; } = [Admin, Editor];
}

/// <summary>SEC-11: authorization policy names.</summary>
public static class AppPolicies
{
    /// <summary>FR-AUTH-02: create, edit, delete, import; manage tags, collections, inventory.</summary>
    public const string CanEditRecipes = "CanEditRecipes";

    /// <summary>FR-AUTH-03: settings, users, backups, restore.</summary>
    public const string CanAdminister = "CanAdminister";

    /// <summary>Roles that satisfy <see cref="CanEditRecipes"/>.</summary>
    public static IReadOnlyList<string> EditorRoles { get; } = [AppRoles.Editor, AppRoles.Admin];

    /// <summary>Roles that satisfy <see cref="CanAdminister"/>.</summary>
    public static IReadOnlyList<string> AdminRoles { get; } = [AppRoles.Admin];
}

/// <summary>SEC-01, SEC-02, SEC-03, FR-INST-04: account and session rules.</summary>
public static class AccountRules
{
    public const int MinimumPasswordLength = 12;
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionSlidingExpiry = TimeSpan.FromHours(8);
    public static readonly TimeSpan SecurityStampValidationInterval = TimeSpan.FromMinutes(5);

    /// <summary>SEC-02: the only message shown for any failed sign-in.</summary>
    public const string InvalidSignInMessage = "Invalid sign-in attempt.";

    /// <summary>
    /// Characters Identity allows in a user name. The email is the user name, so every valid email
    /// (<see cref="ValidateEmail"/>) uses only these.
    /// </summary>
    public const string UserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";

    public const int MaxEmailLength = 254;
    public const int MaxEmailLocalPartLength = 64;
    private const int MaxDomainLabelLength = 63;

    /// <summary>
    /// FR-INST-04, AC-100: checks an account email before a password is asked for or anything is stored.
    /// Plain ASCII addresses only (the email is also the user name): letters, digits and . _ + - before the @,
    /// dot-separated host labels of letters, digits and hyphens after it, ending in a letters-only top-level domain.
    /// Returns null when valid, otherwise a message that quotes the address and says what is wrong.
    /// </summary>
    public static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "An email address is required.";
        }

        string Invalid(string reason) => $"'{email}' is not a valid email address: {reason}.";

        if (email.Length > MaxEmailLength)
        {
            return Invalid($"it is longer than {MaxEmailLength} characters");
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at < 0 || email.IndexOf('@', at + 1) >= 0)
        {
            return Invalid("it must contain exactly one @");
        }

        var local = email[..at];
        var domain = email[(at + 1)..];

        if (local.Length == 0)
        {
            return Invalid("the part before the @ is empty");
        }

        if (local.Length > MaxEmailLocalPartLength)
        {
            return Invalid($"the part before the @ is longer than {MaxEmailLocalPartLength} characters");
        }

        if (!local.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '+' or '-'))
        {
            return Invalid("the part before the @ may contain only letters, digits and . _ + -");
        }

        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal))
        {
            return Invalid("the part before the @ cannot start or end with a dot or contain two dots in a row");
        }

        var labels = domain.Split('.');
        if (labels.Length < 2)
        {
            return Invalid("the part after the @ must be a domain such as example.com");
        }

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > MaxDomainLabelLength
                || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || label.StartsWith('-') || label.EndsWith('-'))
            {
                return Invalid("the part after the @ may contain only letters, digits, hyphens and dots, as in example.com");
            }
        }

        var tld = labels[^1];
        if (tld.Length < 2 || !tld.All(char.IsAsciiLetter))
        {
            return Invalid("the part after the @ must end in a domain such as .com or .org");
        }

        return null;
    }
}
