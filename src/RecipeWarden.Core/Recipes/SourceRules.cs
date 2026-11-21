namespace RecipeWarden.Core.Recipes;

/// <summary>A problem with a recipe's source fields.</summary>
public sealed record SourceProblem(RecipeField Field, string Message);

/// <summary>
/// BR-10: a Web recipe must have a source URL and display name, and its credit cannot be hidden.
/// BR-11: only an Admin can change a Web recipe to another source type, and the change is audited.
/// Any source URL must be an absolute http/https address, so a link in the reading view can never run script.
/// </summary>
public static class SourceRules
{
    public const int MaxUrlLength = 2048;
    public const int MaxNameLength = 200;

    public const string UrlMissing = "A web recipe needs the address it came from";
    public const string NameMissing = "A web recipe needs the source's name";
    public const string UrlScheme = "The source address must start with http:// or https://";
    public const string UrlTooLong = "The source address is too long";
    public const string NameTooLong = "The source name must be 200 characters or fewer";
    public const string AuthorTooLong = "The author must be 200 characters or fewer";

    public static IReadOnlyList<SourceProblem> Validate(SourceType type, string? url, string? name, string? author = null)
    {
        var problems = new List<SourceProblem>();
        var trimmedUrl = url?.Trim() ?? string.Empty;
        var trimmedName = name?.Trim() ?? string.Empty;

        if (trimmedUrl.Length > MaxUrlLength)
        {
            problems.Add(new SourceProblem(RecipeField.SourceUrl, UrlTooLong));
        }
        else if (trimmedUrl.Length > 0 && !IsHttpUrl(trimmedUrl))
        {
            problems.Add(new SourceProblem(RecipeField.SourceUrl, UrlScheme));
        }
        else if (trimmedUrl.Length == 0 && type == SourceType.Web)
        {
            problems.Add(new SourceProblem(RecipeField.SourceUrl, UrlMissing));
        }

        if (trimmedName.Length > MaxNameLength)
        {
            problems.Add(new SourceProblem(RecipeField.SourceName, NameTooLong));
        }
        else if (trimmedName.Length == 0 && type == SourceType.Web)
        {
            problems.Add(new SourceProblem(RecipeField.SourceName, NameMissing));
        }

        if ((author?.Trim().Length ?? 0) > MaxNameLength)
        {
            problems.Add(new SourceProblem(RecipeField.SourceAuthor, AuthorTooLong));
        }

        return problems;
    }

    public static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>BR-11. Moving away from Web by any route would hide the credit, so all such changes need an Admin.</summary>
    public static bool CanChangeType(SourceType from, SourceType to, bool isAdmin) =>
        isAdmin || !ChangeNeedsAudit(from, to);

    /// <summary>BR-11: changes away from Web are audit-logged (FR-ADM-01, M7).</summary>
    public static bool ChangeNeedsAudit(SourceType from, SourceType to) => from == SourceType.Web && to != SourceType.Web;
}
