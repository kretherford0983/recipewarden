using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace RecipeWarden.Importers.Tests;

/// <summary>Builds .docx and PDF files in memory, so no binary fixtures are committed.</summary>
public static class TestDocuments
{
    /// <summary>A paragraph: style "Heading1"/"Title"/null, list item or not, and its text.</summary>
    public sealed record Para(string Text, string? Style = null, bool ListItem = false);

    public static byte[] Docx(IEnumerable<Para> paragraphs, WordprocessingDocumentType type = WordprocessingDocumentType.Document)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, type))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var p in paragraphs)
            {
                var props = new ParagraphProperties();
                if (p.Style is not null)
                {
                    props.ParagraphStyleId = new ParagraphStyleId { Val = p.Style };
                }

                if (p.ListItem)
                {
                    props.NumberingProperties = new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 });
                }

                body.Append(new Paragraph(props, new Run(new Text(p.Text) { Space = SpaceProcessingModeValues.Preserve })));
            }

            main.Document = new Document(body);
        }

        return ms.ToArray();
    }

    public static byte[] Pdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.Letter);
        var y = 740.0;
        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                page.AddText(line, 11, new PdfPoint(40, y), font);
            }

            y -= 16;
        }

        return builder.Build();
    }

    /// <summary>A PDF with no text at all (like a scan).</summary>
    public static byte[] EmptyPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.Letter);
        return builder.Build();
    }

    /// <summary>A minimal PDF whose trailer declares Standard-handler encryption with an unknown password.</summary>
    public static byte[] EncryptedPdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            "<< /Filter /Standard /V 2 /R 3 /Length 128 /P -3904 /O <" + new string('A', 64) + "> /U <" + new string('B', 64) + "> >>",
        };
        var sb = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{o:D10} 00000 n \n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Encrypt 4 0 R /ID [<0123456789ABCDEF0123456789ABCDEF> <0123456789ABCDEF0123456789ABCDEF>] >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
