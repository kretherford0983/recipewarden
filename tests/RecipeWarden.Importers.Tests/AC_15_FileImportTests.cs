using System.Text;
using DocumentFormat.OpenXml;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Security;
using RecipeWarden.Importers.Import;
using static RecipeWarden.Importers.Tests.TestDocuments;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-IMP-04, FR-IMP-05, AC-14, AC-15, W4: Markdown, text, Word and PDF files.</summary>
[Trait("Req", "AC-15")]
[Trait("Req", "FR-IMP-05")]
public sealed class AC_15_FileImportTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Text", name));

    [Fact]
    [Trait("Req", "FR-IMP-04")]
    [Trait("Req", "W4")]
    public void A_markdown_file_with_two_recipes_gives_two()
    {
        var result = ImportReader.ReadFile(UploadKind.Markdown, Fixture("two-recipes.md"), "two-recipes.md");
        result.Error.ShouldBeNull();
        result.Recipes.Count.ShouldBe(2);

        var bread = result.Recipes[0];
        bread.Title.ShouldBe("Garlic Bread");
        bread.Description.ShouldBe("Crispy and quick.");
        bread.Servings.ShouldBe("4");
        bread.PrepTime.ShouldBe("10 min");
        bread.SourceType.ShouldBe(SourceType.File);
        bread.Ingredients.ShouldBe("1 baguette\n4 Tbsp butter, softened\n2 cloves garlic, minced");
        StepTextRules.Split(bread.Steps).ShouldBe(
        [
            new StepText(null, "Mix the butter and garlic."),
            new StepText(null, "Spread on the bread and bake 10 minutes at 400°F.", "Bake"),
        ]);

        var salad = result.Recipes[1];
        salad.Title.ShouldBe("Simple Salad");
        salad.Ingredients.ShouldBe("Dressing:\n3 Tbsp olive oil\n1 Tbsp vinegar\nSalad:\n1 head lettuce");
        StepTextRules.Split(salad.Steps).ShouldHaveSingleItem().Text.ShouldBe("Toss everything together.");
        salad.Notes.ShouldBe("Dress just before serving.");
    }

    [Fact]
    public void A_plain_text_file_uses_stand_alone_headings()
    {
        var r = ImportReader.ReadFile(UploadKind.Text, Fixture("plain.txt"), "plain.txt").Recipes.ShouldHaveSingleItem();
        r.Title.ShouldBe("Overnight Oats");
        r.Yield.ShouldBe("2 quarts");
        r.Ingredients.ShouldBe("2 cups rolled oats\n2 cups milk");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(["Stir together.", "Chill overnight."]);
    }

    [Fact]
    [Trait("Req", "AC-14")]
    public void A_recipe_md_file_is_read_exactly()
    {
        var md = RecipeMarkdown.Write(new RecipeEdit
        {
            Title = "Toast",
            Servings = 2,
            SourceType = SourceType.Personal,
            IngredientsText = "2 slices bread",
            StepsText = "Toast the bread.",
        });
        var r = ImportReader.ReadFile(UploadKind.Markdown, Encoding.UTF8.GetBytes(md), "toast.md").Recipes.ShouldHaveSingleItem();
        r.Method.ShouldBe(ExtractionMethod.RecipeMarkdown);
        r.SourceType.ShouldBe(SourceType.Personal);
        r.Ingredients.ShouldBe("2 slices bread");
    }

    [Fact]
    public void A_file_with_no_title_uses_its_name()
    {
        var bytes = Encoding.UTF8.GetBytes("## Ingredients\n- 1 cup rice\n## Steps\n1. Cook the rice.");
        ImportReader.ReadFile(UploadKind.Markdown, bytes, "jasmine_rice-basic.md").Recipes.ShouldHaveSingleItem().Title.ShouldBe("jasmine rice basic");
    }

    [Fact]
    public void Text_without_sections_or_not_utf8_is_refused()
    {
        ImportReader.ReadFile(UploadKind.Text, "Just a note."u8.ToArray(), "a.txt").Error.ShouldBe(ImportReader.NoRecipeInFile);
        ImportReader.ReadFile(UploadKind.Text, [0x61, 0x00, 0x62], "a.txt").Error.ShouldBe(UploadRules.NotText);
        ImportReader.ReadFile(UploadKind.Jpeg, [1], "a.jpg").Error.ShouldBe(UploadRules.NotAllowed);
    }

    [Fact]
    public void A_word_file_with_ingredients_and_directions_headings_is_separated()
    {
        var bytes = Docx(
        [
            new Para("Chili", "Title"),
            new Para("A weeknight chili."),
            new Para("Ingredients", "Heading2"),
            new Para("1 lb ground beef", ListItem: true),
            new Para("1 can (15 oz) beans", ListItem: true),
            new Para("Directions", "Heading2"),
            new Para("Brown the beef.", ListItem: true),
            new Para("Add the beans and simmer.", ListItem: true),
        ]);

        var r = ImportReader.ReadFile(UploadKind.Docx, bytes, "chili.docx").Recipes.ShouldHaveSingleItem();
        r.Title.ShouldBe("Chili");
        r.Description.ShouldBe("A weeknight chili.");
        r.Ingredients.ShouldBe("1 lb ground beef\n1 can (15 oz) beans");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(["Brown the beef.", "Add the beans and simmer."]);
        r.SourceType.ShouldBe(SourceType.File);
    }

    [Fact]
    public void Word_heading_1s_split_recipes()
    {
        var bytes = Docx(
        [
            new Para("Tea", "Heading1"), new Para("Ingredients", "Heading2"), new Para("1 tea bag"), new Para("Steps", "Heading2"), new Para("Steep."),
            new Para("Coffee", "Heading1"), new Para("Ingredients", "Heading2"), new Para("2 Tbsp coffee"), new Para("Steps", "Heading2"), new Para("Brew."),
        ]);
        ImportReader.ReadFile(UploadKind.Docx, bytes, "drinks.docx").Recipes.Select(r => r.Title).ShouldBe(["Tea", "Coffee"]);
    }

    [Fact]
    public void A_macro_enabled_word_file_is_refused_by_content()
    {
        var bytes = Docx([new Para("Ingredients", "Heading1")], WordprocessingDocumentType.MacroEnabledDocument);
        ImportReader.ReadFile(UploadKind.Docx, bytes, "renamed.docx").Error.ShouldBe(DocxRecipeExtractor.MacroContent);
    }

    [Fact]
    public void A_damaged_word_file_is_refused()
    {
        ImportReader.ReadFile(UploadKind.Docx, [0x50, 0x4B, 0x03, 0x04, 1, 2, 3], "x.docx").Error.ShouldBe(DocxRecipeExtractor.Unreadable);
    }

    [Fact]
    public void A_text_pdf_with_ingredients_and_directions_headings_is_separated()
    {
        var bytes = Pdf(
            "Banana Bread",
            "",
            "Serves 8",
            "",
            "Ingredients",
            "3 ripe bananas",
            "2 cups all-purpose flour",
            "",
            "Directions",
            "1. Mash the bananas.",
            "2. Fold in the flour and bake 60 minutes at 350 F.");

        var r = ImportReader.ReadFile(UploadKind.Pdf, bytes, "banana.pdf").Recipes.ShouldHaveSingleItem();
        r.Title.ShouldBe("Banana Bread");
        r.Servings.ShouldBe("8");
        r.Ingredients.ShouldBe("3 ripe bananas\n2 cups all-purpose flour");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(["Mash the bananas.", "Fold in the flour and bake 60 minutes at 350 F."]);
    }

    [Fact]
    public void Encrypted_scanned_and_damaged_pdfs_are_refused_clearly()
    {
        ImportReader.ReadFile(UploadKind.Pdf, EncryptedPdf(), "locked.pdf").Error.ShouldBe(PdfRecipeExtractor.Encrypted);
        ImportReader.ReadFile(UploadKind.Pdf, EmptyPdf(), "scan.pdf").Error.ShouldBe(PdfRecipeExtractor.NoText);
        ImportReader.ReadFile(UploadKind.Pdf, "%PDF-1.7 garbage"u8.ToArray(), "bad.pdf").Error.ShouldNotBeNull();
    }
}
