using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using RecipeWarden.Core.Text;

namespace RecipeWarden.Core.Organization;

/// <summary>FR-ORG-01: optional tag types for grouped filtering.</summary>
public enum TagType
{
    Meal,
    Course,
    Season,
    Method,
    Cuisine,
    Diet,
}

/// <summary>
/// FR-ORG-01, FR-ORG-02, AC-20: tag and collection names. Names are de-duplicated on a normalized form
/// (Unicode NFC, trimmed, whitespace collapsed, lower case), stored in NormalizedName with a unique index.
/// </summary>
public static class NameRules
{
    public const int MaxTagLength = 50;
    public const int MaxCollectionLength = 100;

    public const string Required = "Enter a name";
    public const string BadCharacters = "The name contains characters that are not allowed";
    public const string TagTooLong = "Tag names are limited to 50 characters";
    public const string CollectionTooLong = "Collection names are limited to 100 characters";

    /// <summary>The display form: NFC, trimmed, runs of whitespace collapsed to one space; case kept.</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var c in SafeText.Normalize(name, NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>The de-duplication key: <see cref="Clean"/> then lower case (invariant).</summary>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "Lower-case keys match the lower-case slugs and are never round-tripped for display.")]
    public static string Normalize(string? name) => Clean(name).ToLowerInvariant();

    public static string? ValidateTag(string? name) => Validate(name, MaxTagLength, TagTooLong);

    public static string? ValidateCollection(string? name) => Validate(name, MaxCollectionLength, CollectionTooLong);

    /// <summary>Parses a tag type by name only (numbers are refused); empty or unknown gives null.</summary>
    public static TagType? ParseTagType(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.All(char.IsAsciiLetter))
        {
            return null;
        }

        return Enum.TryParse<TagType>(text, ignoreCase: true, out var type) ? type : null;
    }

    private static string? Validate(string? name, int maxLength, string tooLong)
    {
        var cleaned = Clean(name);
        if (cleaned.Length == 0)
        {
            return Required;
        }

        if (!ReferenceEquals(SafeText.ReplaceLoneSurrogates(name!), name))
        {
            return BadCharacters;
        }

        foreach (var c in cleaned)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse)
            {
                return BadCharacters;
            }
        }

        return cleaned.Length > maxLength ? tooLong : null;
    }
}
