using System.Text;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Core.Tests;

/// <summary>SEC-50, SEC-51, AC-15: uploads are allowed by extension and by their first bytes.</summary>
[Trait("Req", "SEC-50")]
[Trait("Req", "SEC-51")]
[Trait("Req", "AC-15")]
public sealed class SEC_50_UploadRulesTests
{
    private static readonly byte[] Pdf = "%PDF-1.7\n%\u00e2\u00e3"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0, 0, 0];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];
    private static readonly byte[] Webp = "RIFF\u0010\0\0\0WEBPVP8 "u8.ToArray();
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Exe = "MZ\u0090\0\u0003\0\0\0"u8.ToArray();
    private static readonly byte[] Text = "# Soup\n\n## Ingredients\n"u8.ToArray();

    [Theory]
    [InlineData("soup.md", UploadKind.Markdown)]
    [InlineData("soup.MARKDOWN", UploadKind.Markdown)]
    [InlineData("soup.txt", UploadKind.Text)]
    public void Text_files_are_accepted(string name, UploadKind kind)
    {
        UploadRules.CheckDocument(name, Text.Length, Text).Kind.ShouldBe(kind);
    }

    [Fact]
    public void Docx_and_pdf_need_their_magic_bytes()
    {
        UploadRules.CheckDocument("soup.docx", 1000, Zip).Kind.ShouldBe(UploadKind.Docx);
        UploadRules.CheckDocument("soup.pdf", 1000, Pdf).Kind.ShouldBe(UploadKind.Pdf);
        UploadRules.CheckDocument("soup.pdf", 1000, Zip).Error.ShouldBe(UploadRules.Mismatch);
        UploadRules.CheckDocument("soup.docx", 1000, Pdf).Error.ShouldBe(UploadRules.Mismatch);
    }

    [Fact]
    public void A_program_renamed_to_pdf_or_md_is_refused()
    {
        UploadRules.CheckDocument("soup.pdf", 1000, Exe).Error.ShouldBe(UploadRules.Mismatch);
        UploadRules.CheckDocument("soup.md", 1000, Exe).Error.ShouldBe(UploadRules.Mismatch);
    }

    [Theory]
    [InlineData("soup.docm")]
    [InlineData("soup.dotm")]
    public void Macro_formats_are_refused(string name)
    {
        UploadRules.CheckDocument(name, 1000, Zip).Error.ShouldBe(UploadRules.Macro);
    }

    [Fact]
    public void Legacy_doc_is_refused_by_name_or_content()
    {
        UploadRules.CheckDocument("soup.doc", 1000, Ole).Error.ShouldBe(UploadRules.LegacyDoc);
        UploadRules.CheckDocument("soup.docx", 1000, Ole).Error.ShouldBe(UploadRules.LegacyDoc);
    }

    [Theory]
    [InlineData("soup.exe")]
    [InlineData("soup.html")]
    [InlineData("soup")]
    [InlineData(null)]
    public void Other_types_are_refused(string? name)
    {
        UploadRules.CheckDocument(name, 1000, Text).Error.ShouldBe(UploadRules.NotAllowed);
    }

    [Fact]
    public void Size_caps_apply()
    {
        UploadRules.CheckDocument("soup.pdf", 60L * 1024 * 1024, Pdf).Error.ShouldBe(UploadRules.DocumentTooBig);
        UploadRules.CheckDocument("soup.pdf", UploadRules.MaxDocumentBytes, Pdf).IsAllowed.ShouldBeTrue();
        UploadRules.CheckDocument("soup.pdf", 0, Pdf).Error.ShouldBe(UploadRules.Empty);
        UploadRules.CheckImage("a.jpg", UploadRules.MaxImageBytes + 1, Jpeg).Error.ShouldBe(UploadRules.ImageTooBig);
    }

    [Fact]
    public void Images_need_matching_magic_bytes()
    {
        UploadRules.CheckImage("a.JPG", 100, Jpeg).Kind.ShouldBe(UploadKind.Jpeg);
        UploadRules.CheckImage("a.jpeg", 100, Jpeg).Kind.ShouldBe(UploadKind.Jpeg);
        UploadRules.CheckImage("a.png", 100, Png).Kind.ShouldBe(UploadKind.Png);
        UploadRules.CheckImage("a.webp", 100, Webp).Kind.ShouldBe(UploadKind.Webp);
        UploadRules.CheckImage("a.png", 100, Jpeg).Error.ShouldBe(UploadRules.Mismatch);
        UploadRules.CheckImage("a.jpg", 100, Exe).Error.ShouldBe(UploadRules.Mismatch);
    }

    [Theory]
    [InlineData("a.gif")]
    [InlineData("a.svg")]
    [InlineData("a.bmp")]
    public void Other_image_types_are_refused(string name)
    {
        UploadRules.CheckImage(name, 100, Png).Error.ShouldBe(UploadRules.ImageNotAllowed);
    }

    [Fact]
    public void Whole_text_files_must_be_utf8_without_control_characters()
    {
        UploadRules.IsText("Crème brûlée\r\n\t½ cup"u8).ShouldBeTrue();
        UploadRules.IsText([0xEF, 0xBB, 0xBF, (byte)'a']).ShouldBeTrue();
        UploadRules.IsText([(byte)'a', 0x00, (byte)'b']).ShouldBeFalse();
        UploadRules.IsText([0xC3, 0x28]).ShouldBeFalse();
        UploadRules.IsText(Encoding.Latin1.GetBytes("caf\u00e9")).ShouldBeFalse();
    }

    [Fact]
    public void Kinds_are_grouped()
    {
        UploadRules.IsDocument(UploadKind.Pdf).ShouldBeTrue();
        UploadRules.IsDocument(UploadKind.Png).ShouldBeFalse();
        UploadRules.IsImage(UploadKind.Webp).ShouldBeTrue();
        UploadRules.IsImage(UploadKind.Text).ShouldBeFalse();
        UploadRules.SniffImage("GIF89a"u8).ShouldBeNull();
    }
}
