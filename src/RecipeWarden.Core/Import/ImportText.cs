using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Text;

namespace RecipeWarden.Core.Import;

/// <summary>
/// BR-13: imported text is stored as plain text, never as HTML. Entities are decoded, any tags left in the text are
/// removed, control characters dropped, whitespace tidied and the result capped in length.
/// </summary>
public static partial class ImportText
{
    public const int MaxFieldLength = 10000;

    [GeneratedRegex(@"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ScriptBlocks();

    [GeneratedRegex(@"<\s*(br|/p|/li|/div|/h[1-6]|p|li|div|h[1-6])\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]{0,2000}>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t  -   　]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Spaces();

    [GeneratedRegex(@" +(?=[.,;:!?)])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ManyBlankLines();

    /// <summary>One line of plain text (newlines become spaces).</summary>
    public static string? Line(string? text, int maxLength = 500)
    {
        var clean = Block(text, maxLength);
        return clean is null ? null : Spaces().Replace(clean.Replace('\n', ' '), " ").Trim();
    }

    /// <summary>Plain text that may span lines; null when nothing is left.</summary>
    public static string? Block(string? text, int maxLength = MaxFieldLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Length > maxLength * 4 ? text[..(maxLength * 4)] : text;
        s = ScriptBlocks().Replace(s, " ");
        s = BlockTags().Replace(s, "\n");
        s = AnyTag().Replace(s, " ");
        // Decode twice: some sites double-encode ("&amp;frac12;").
        s = WebUtility.HtmlDecode(WebUtility.HtmlDecode(s));
        // A decoded "<script>" stays as inert text; it is never rendered as HTML (SEC-22).
        s = SafeText.ReplaceLoneSurrogates(s).Normalize(NormalizationForm.FormC);
        s = s.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\n' || c == '\t' || !char.IsControl(c))
            {
                sb.Append(c is '​' or '﻿' ? ' ' : c);
            }
        }

        var lines = sb.ToString().Split('\n').Select(l => SpaceBeforePunctuation().Replace(Spaces().Replace(l, " "), string.Empty).Trim());
        var joined = ManyBlankLines().Replace(string.Join('\n', lines), "\n\n").Trim();
        if (joined.Length == 0)
        {
            return null;
        }

        return joined.Length > maxLength ? joined[..maxLength].TrimEnd() : joined;
    }

    /// <summary>Non-empty cleaned lines, each at most <paramref name="maxLength"/> characters.</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<string?> items, int maxLength = 500) =>
        items.Select(i => Line(i, maxLength)).Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
}
