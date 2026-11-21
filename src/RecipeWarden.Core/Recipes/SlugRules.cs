using System.Globalization;
using System.Text;
using RecipeWarden.Core.Text;

namespace RecipeWarden.Core.Recipes;

/// <summary>The slug to use after a title change, and the old slug to keep as a redirect (ADR 0008).</summary>
public sealed record SlugChange(string NewSlug, string? AliasToKeep, bool Changed);

/// <summary>
/// BR-02, ADR 0008: URL slugs for recipes, tags and collections. Lower-case ASCII letters and digits separated by
/// single hyphens, at most 80 characters. Published recipe slugs follow the title; the old slug becomes an alias.
/// </summary>
public static class SlugRules
{
    public const int MaxLength = 80;
    private const int MaxSuffix = 999;

    public static string FromText(string? text, string fallback = "recipe")
    {
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        var decomposed = SafeText.Normalize(FoldSpecialLetters(text), NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // accent stripped; the base letter was already handled
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingHyphen && sb.Length > 0)
                {
                    sb.Append('-');
                }

                pendingHyphen = false;
                sb.Append(char.IsAsciiLetterUpper(c) ? (char)(c + ('a' - 'A')) : c);
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = Truncate(sb.ToString());
        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary>Returns <paramref name="baseSlug"/> or the first free <c>-2</c>, <c>-3</c>… variant.</summary>
    public static string MakeUnique(string baseSlug, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseSlug);
        ArgumentNullException.ThrowIfNull(isTaken);

        if (!isTaken(baseSlug))
        {
            return baseSlug;
        }

        for (var n = 2; n <= MaxSuffix; n++)
        {
            var suffix = "-" + n.ToString(CultureInfo.InvariantCulture);
            var stem = baseSlug.Length + suffix.Length > MaxLength ? baseSlug[..(MaxLength - suffix.Length)].TrimEnd('-') : baseSlug;
            var candidate = stem + suffix;
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"No free slug for '{baseSlug}' after {MaxSuffix} attempts.");
    }

    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length > MaxLength || slug[0] == '-' || slug[^1] == '-')
        {
            return false;
        }

        for (var i = 0; i < slug.Length; i++)
        {
            var c = slug[i];
            var ok = (c is >= 'a' and <= 'z') || char.IsAsciiDigit(c) || (c == '-' && slug[i - 1] != '-');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ADR 0008: a Draft's slug simply follows its title. A Published recipe's slug follows its title too, and the
    /// previous slug is returned in <see cref="SlugChange.AliasToKeep"/> so it keeps redirecting. A title that yields
    /// the current slug (or its numbered variant) changes nothing. <paramref name="isTaken"/> must treat this recipe's
    /// own aliases as free, so a title can change back.
    /// </summary>
    public static SlugChange PlanTitleChange(string currentSlug, RecipeStatus status, string? newTitle, Func<string, bool> isTaken)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentSlug);
        ArgumentNullException.ThrowIfNull(isTaken);

        var baseSlug = FromText(newTitle);
        if (IsSameOrNumberedVariant(currentSlug, baseSlug))
        {
            return new SlugChange(currentSlug, null, false);
        }

        var newSlug = MakeUnique(baseSlug, isTaken);
        return new SlugChange(newSlug, status == RecipeStatus.Published ? currentSlug : null, true);
    }

    private static bool IsSameOrNumberedVariant(string current, string baseSlug)
    {
        if (string.Equals(current, baseSlug, StringComparison.Ordinal))
        {
            return true;
        }

        if (!current.StartsWith(baseSlug + "-", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = current.AsSpan(baseSlug.Length + 1);
        foreach (var c in rest)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return rest.Length > 0;
    }

    private static string Truncate(string slug)
    {
        if (slug.Length <= MaxLength)
        {
            return slug;
        }

        var cut = slug[..MaxLength];
        if (slug[MaxLength] != '-')
        {
            var lastHyphen = cut.LastIndexOf('-');
            if (lastHyphen > 0)
            {
                cut = cut[..lastHyphen];
            }
        }

        return cut.Trim('-');
    }

    /// <summary>Letters that Unicode decomposition does not reduce to ASCII.</summary>
    private static string FoldSpecialLetters(string text)
    {
        if (text.All(char.IsAscii))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                'ß' => "ss",
                'æ' or 'Æ' => "ae",
                'œ' or 'Œ' => "oe",
                'ø' or 'Ø' => "o",
                'đ' or 'Đ' => "d",
                'ł' or 'Ł' => "l",
                'þ' or 'Þ' => "th",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }
}
