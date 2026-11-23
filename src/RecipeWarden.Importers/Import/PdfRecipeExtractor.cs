using System.Text;
using RecipeWarden.Core.Import;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace RecipeWarden.Importers.Import;

/// <summary>
/// FR-IMP-05, SEC-53, AC-15, D-07: reads the text of a text-based PDF, page by page in reading order, then finds
/// the sections by their headings. Embedded JavaScript, forms and attachments are never read. Encrypted PDFs and
/// PDFs without text (scans; OCR is not in v1) are refused with a clear message.
/// </summary>
public static class PdfRecipeExtractor
{
    public const int MaxPages = 50;

    public const string Encrypted = "This PDF is encrypted or password-protected; save an unprotected copy and try again";
    public const string NoText = "This PDF has no text to read (it may be a scan); scanned PDFs are not supported";
    public const string Unreadable = "The PDF could not be read";
    public const string TooManyPages = "PDFs are limited to 50 pages";

    public static (IReadOnlyList<ImportedRecipe> Recipes, string? Error) Extract(byte[] bytes, string? fallbackTitle)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var sb = new StringBuilder();
        try
        {
            using var pdf = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
            if (pdf.IsEncrypted)
            {
                return ([], Encrypted);
            }

            if (pdf.NumberOfPages > MaxPages)
            {
                return ([], TooManyPages);
            }

            foreach (var page in pdf.GetPages())
            {
                foreach (var line in Lines(page))
                {
                    sb.Append(line).Append('\n');
                }

                sb.Append('\n');
            }
        }
        catch (PdfDocumentEncryptedException)
        {
            return ([], Encrypted);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ([], Unreadable);
        }

        var text = sb.ToString();
        if (text.Trim().Length == 0)
        {
            return ([], NoText);
        }

        return (TextRecipeExtractor.Extract(text, fallbackTitle), null);
    }

    /// <summary>Words grouped into lines by their baseline, top to bottom, left to right.</summary>
    private static IEnumerable<string> Lines(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            yield break;
        }

        var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
        foreach (var w in words.OrderByDescending(w => Math.Round(w.BoundingBox.Bottom, 0)).ThenBy(w => w.BoundingBox.Left))
        {
            var last = lines.Count > 0 ? lines[^1] : null;
            if (last is not null && Math.Abs(last[0].BoundingBox.Bottom - w.BoundingBox.Bottom) <= Math.Max(2.0, w.BoundingBox.Height * 0.4))
            {
                last.Add(w);
            }
            else
            {
                lines.Add([w]);
            }
        }

        foreach (var line in lines)
        {
            yield return string.Join(' ', line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text));
        }
    }
}
