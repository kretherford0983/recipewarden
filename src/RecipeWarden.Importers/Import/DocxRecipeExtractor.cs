using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RecipeWarden.Core.Import;

namespace RecipeWarden.Importers.Import;

/// <summary>
/// FR-IMP-05, AC-15: reads a Word .docx. Headings (Title, Heading 1–6, or a heading outline level) become Markdown
/// headings and list paragraphs become "- " items, then the heading-based extractor finds the sections. Only
/// paragraph text is read: macros, embedded objects, fields and links are never run or followed. The upload
/// validator has already checked the zip (SEC-51) and refused macro-enabled files (SEC-50).
/// </summary>
public static class DocxRecipeExtractor
{
    public const string Unreadable = "The Word file could not be read";
    public const string MacroContent = "This Word file contains macros and is not accepted";

    public static (IReadOnlyList<ImportedRecipe> Recipes, string? Error) Extract(Stream stream, string? fallbackTitle)
    {
        var (text, error) = ExtractText(stream);
        return error is not null ? ([], error) : (TextRecipeExtractor.Extract(text!, fallbackTitle), null);
    }

    /// <summary>The document as light Markdown (headings, lists); also what the AI parser reads (FR-IMP-09).</summary>
    public static (string? Text, string? Error) ExtractText(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        string text;
        try
        {
            using var doc = WordprocessingDocument.Open(stream, false, new OpenSettings { AutoSave = false });
            if (doc.DocumentType != DocumentFormat.OpenXml.WordprocessingDocumentType.Document)
            {
                return (null, MacroContent);
            }

            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return (null, Unreadable);
            }

            var styles = doc.MainDocumentPart!.StyleDefinitionsPart?.Styles;
            text = ToMarkdown(body, styles);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, Unreadable);
        }

        return (text, null);
    }

    private static string ToMarkdown(Body body, Styles? styles)
    {
        var sb = new StringBuilder();
        foreach (var p in body.Descendants<Paragraph>().Take(20000))
        {
            var text = string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim();
            if (text.Length == 0)
            {
                sb.Append('\n');
                continue;
            }

            var props = p.ParagraphProperties;
            var level = HeadingLevel(props?.ParagraphStyleId?.Val?.Value, styles) ?? (props?.OutlineLevel?.Val?.Value is { } o ? o + 1 : null);
            if (level is { } l and >= 1 and <= 6)
            {
                sb.Append('#', l).Append(' ').Append(text).Append('\n');
            }
            else if (props?.NumberingProperties is not null)
            {
                sb.Append("- ").Append(text).Append('\n');
            }
            else
            {
                sb.Append(text).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static int? HeadingLevel(string? styleId, Styles? styles)
    {
        if (string.IsNullOrEmpty(styleId))
        {
            return null;
        }

        var name = styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId)?.StyleName?.Val?.Value ?? styleId;
        var n = name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (n == "title")
        {
            return 1;
        }

        return n.StartsWith("heading", StringComparison.Ordinal) && int.TryParse(n.AsSpan(7), out var level) ? level : null;
    }
}
