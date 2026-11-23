using System.Text;
using AngleSharp.Dom;

namespace RecipeWarden.Importers.Import;

/// <summary>
/// Turns an HTML element into Markdown-like lines for the heading-based extractor: headings become "## …", list
/// items "- …", paragraphs and line breaks new lines. Scripts, styles, navigation, headers, footers, side bars,
/// forms and comment sections are skipped. Only text is taken (BR-13).
/// </summary>
public static class HtmlText
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "svg", "nav", "header", "footer", "aside", "form", "iframe",
        "button", "select", "textarea", "object", "embed", "canvas", "video", "audio", "head",
    };

    private static readonly HashSet<string> Block = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "main", "ul", "ol", "table", "tr", "blockquote", "figure", "figcaption",
        "dl", "dt", "dd", "pre", "hr",
    };

    private static readonly string[] SkipMarkers = ["comment", "sidebar", "share", "related", "newsletter", "advert", "promo", "breadcrumb", "cookie"];

    /// <summary>A whole page's readable text (FR-IMP-09 AI input): the body, without scripts, navigation and the like.</summary>
    public static string PageText(string html)
    {
        var parser = new AngleSharp.Html.Parser.HtmlParser(new AngleSharp.Html.Parser.HtmlParserOptions { IsScripting = false });
        using var document = parser.ParseDocument(html ?? string.Empty);
        return document.Body is null ? string.Empty : Of(document.Body);
    }

    public static string Of(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var sb = new StringBuilder();
        Walk(element, sb);
        return sb.ToString();
    }

    /// <summary>The element's text with block elements and line breaks as new lines.</summary>
    public static string TextOf(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var sb = new StringBuilder();
        Walk(element, sb, markup: false);
        return sb.ToString();
    }

    private static void Walk(INode node, StringBuilder sb, bool markup = true, int depth = 0)
    {
        if (depth > 200)
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            if (child is IText text)
            {
                var t = text.Data.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                if (t.Trim().Length > 0 || (sb.Length > 0 && sb[^1] != ' ' && sb[^1] != '\n'))
                {
                    sb.Append(t);
                }

                continue;
            }

            if (child is not IElement el)
            {
                continue;
            }

            var name = el.LocalName;
            if (Skip.Contains(name) || el.HasAttribute("hidden") || IsChrome(el))
            {
                continue;
            }

            if (name == "br")
            {
                sb.Append('\n');
                continue;
            }

            if (name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6')
            {
                NewLine(sb);
                if (markup)
                {
                    sb.Append('#', name[1] - '0').Append(' ');
                }

                Walk(el, sb, markup, depth + 1);
                NewLine(sb);
                continue;
            }

            if (name == "li")
            {
                NewLine(sb);
                if (markup)
                {
                    sb.Append("- ");
                }

                Walk(el, sb, markup, depth + 1);
                NewLine(sb);
                continue;
            }

            var block = Block.Contains(name) || name is "td" or "th";
            if (block)
            {
                NewLine(sb);
            }

            Walk(el, sb, markup, depth + 1);
            if (block)
            {
                NewLine(sb);
            }
        }
    }

    private static bool IsChrome(IElement el)
    {
        if (el.LocalName is "p" or "li" or "span" or "a" or "strong" or "b" or "em" or "i")
        {
            return false;
        }

        var marker = ((el.Id ?? string.Empty) + " " + (el.ClassName ?? string.Empty)).ToLowerInvariant();
        return SkipMarkers.Any(m => marker.Contains(m, StringComparison.Ordinal))
            || string.Equals(el.GetAttribute("role"), "navigation", StringComparison.OrdinalIgnoreCase);
    }

    private static void NewLine(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }

        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.Append('\n');
        }
    }
}
