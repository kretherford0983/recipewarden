using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>
/// FR-REC-03, UX-13: tidies an ingredient line's text. A leading list bullet ("- ", "* ", "• ", a Markdown checkbox)
/// is removed, because the ingredient list draws its own; brackets doubled around the same text ("((13.5 oz))")
/// show once. Used when a line is parsed (typing, import) and when it is shown (lines saved before this rule).
/// </summary>
public static partial class IngredientText
{
    public static string Clean(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return line ?? string.Empty;
        }

        var text = Bullet().Replace(line, string.Empty, 1);
        string before;
        do
        {
            before = text;
            text = DoubledBrackets().Replace(text, "($1)");
        }
        while (text != before);

        return text == line ? line : text.Trim();
    }

    // Symbols that are only ever bullets are removed with or without a following space; "-", "+" and "*" (which
    // can start an amount or a footnote) only when a space follows. Then an optional "[ ]" / "[x]" checkbox.
    [GeneratedRegex(@"^\s*(?:[•·◦▪▫‣⁃●○■□–—]+\s*|[-+*]+\s+)(?:\[[ xX]?\]\s+)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Bullet();

    // "( (text) )" around exactly one bracketed group with no other text inside the outer pair.
    [GeneratedRegex(@"\(\s*\(([^()]*)\)\s*\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex DoubledBrackets();
}
