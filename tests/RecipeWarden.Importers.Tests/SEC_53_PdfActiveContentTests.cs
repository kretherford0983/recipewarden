using RecipeWarden.Importers.Import;
using static RecipeWarden.Importers.Tests.TestDocuments;

namespace RecipeWarden.Importers.Tests;

/// <summary>SEC-53: PDFs are read for page text only; scripts, form values and attachments are ignored.</summary>
[Trait("Req", "SEC-53")]
public sealed class SEC_53_PdfActiveContentTests
{
    [Fact]
    public void Only_page_text_is_read_from_a_pdf_with_script_form_and_attachment()
    {
        var (text, error) = PdfRecipeExtractor.ExtractText(PdfWithActiveContent());
        error.ShouldBeNull();
        text.ShouldNotBeNull();
        text.ShouldContain("Apple Pie");
        text.ShouldContain("2 cups flour");
        text.ShouldNotContain("SCRIPT-RAN");
        text.ShouldNotContain("FORM-FIELD-VALUE");
        text.ShouldNotContain("EMBEDDED-SECRET-PAYLOAD");
    }
}
