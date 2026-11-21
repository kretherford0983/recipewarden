using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using RecipeWarden.Core.Text;

namespace RecipeWarden.Core.Search;

/// <summary>
/// SEC-21, AC-22: turns raw search text into a safe FTS5 MATCH expression. The input is split into words
/// (runs of letters, digits and combining marks); every word is wrapped in double quotes, so FTS5 operators,
/// column filters, NEAR, parentheses, stars and quotes in the input are only ever literal text or separators.
/// The expression is still passed to SQLite as a parameter, never concatenated into SQL.
/// </summary>
public sealed class FtsQuery
{
    public const int MaxInputLength = 200;
    public const int MaxTerms = 8;
    public const int MaxTermLength = 40;
    private const int MinPrefixLength = 2;

    private FtsQuery(IReadOnlyList<string> terms, bool lastIsPrefix)
    {
        Terms = terms;
        TermExpressions = terms.Select((t, i) => Quote(t) + (lastIsPrefix && i == terms.Count - 1 ? "*" : string.Empty)).ToArray();
        MatchAny = string.Join(" OR ", TermExpressions);
    }

    public static FtsQuery Empty { get; } = new([], false);

    /// <summary>The distinct lower-case words, in input order.</summary>
    public IReadOnlyList<string> Terms { get; }

    /// <summary>One quoted expression per term; the last ends in <c>*</c> when the user may still be typing it.</summary>
    public IReadOnlyList<string> TermExpressions { get; }

    /// <summary>All terms joined with OR. Ranking puts rows that match more terms first (AC-21).</summary>
    public string MatchAny { get; }

    public bool IsEmpty => Terms.Count == 0;

    public static FtsQuery Build(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return Empty;
        }

        var text = SafeText.Normalize(input.Length > MaxInputLength ? input[..MaxInputLength] : input, NormalizationForm.FormC);
        var terms = new List<string>();
        var current = new StringBuilder();
        var hasLetterOrDigit = false;
        var lastEndsInput = false;

        for (var i = 0; i <= text.Length && terms.Count < MaxTerms; i++)
        {
            if (i < text.Length && IsWordChar(text[i]))
            {
                current.Append(text[i]);
                hasLetterOrDigit |= char.IsLetterOrDigit(text[i]);
                continue;
            }

            if (current.Length > 0)
            {
                if (hasLetterOrDigit)
                {
                    var term = Lower(current.Length > MaxTermLength ? current.ToString(0, MaxTermLength) : current.ToString());
                    if (!terms.Contains(term, StringComparer.Ordinal))
                    {
                        terms.Add(term);
                        lastEndsInput = i == text.Length;
                    }
                }

                current.Clear();
                hasLetterOrDigit = false;
            }
        }

        if (terms.Count == 0)
        {
            return Empty;
        }

        return new FtsQuery(terms, lastEndsInput && terms[^1].Length >= MinPrefixLength);
    }

    private static bool IsWordChar(char c)
    {
        if (char.IsLetterOrDigit(c))
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(c);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }

    // Terms hold only letters, digits and marks, so they cannot contain a quote; doubling is a second guard.
    private static string Quote(string term) => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "FTS5 unicode61 folds to lower case; terms are matched, never displayed.")]
    private static string Lower(string s) => s.ToLowerInvariant();
}
