using System.Text.RegularExpressions;

namespace RecipeWarden.Core.Recipes;

/// <summary>
/// UX-13, FR-SRCH-02: the short plain-text summary on a recipe card, from the Markdown description. Removes the
/// common Markdown marks (emphasis, links, headings, list markers, code ticks), joins lines and cuts at a word.
/// The result is text: Razor encodes it like any other value.
/// </summary>
public static partial class CardSummary
{
    public const int MaxLength = 160;

    public static string? From(string? markdown)
    {
        var text = PlainText(markdown);
        if (text is null || text.Length <= MaxLength)
        {
            return text;
        }

        var cut = text[..MaxLength];
        var space = cut.LastIndexOf(' ');
        return (space > MaxLength / 2 ? cut[..space] : cut).TrimEnd(' ', ',', ';', ':', '.') + "…";
    }

    /// <summary>UX-14: the whole description as plain text (no length cut), e.g. for recipe structured data.</summary>
    public static string? PlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => ListOrHeading().Replace(l.Trim(), string.Empty));
        var text = string.Join(' ', lines);
        text = Link().Replace(text, "$1");
        text = Emphasis().Replace(text, "$2");
        text = Strike().Replace(text, "$1");
        text = text.Replace("`", string.Empty, StringComparison.Ordinal);
        text = Spaces().Replace(text, " ").Trim();
        if (!text.Any(char.IsLetterOrDigit))
        {
            return null; // only marks, nothing to read
        }

        return text;
    }

    [GeneratedRegex(@"^(#{1,6}\s+|[-*+]\s+|\d+[.)]\s+|>\s?)")]
    private static partial Regex ListOrHeading();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    // **x**, __x__, *x*, _x_ when the marks hug a word (so "2 * 3" and snake_case stay as written).
    [GeneratedRegex(@"(\*\*|__|\*|(?<![\w])_)(?=\S)(.+?)(?<=\S)\1(?![\w])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~")]
    private static partial Regex Strike();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
