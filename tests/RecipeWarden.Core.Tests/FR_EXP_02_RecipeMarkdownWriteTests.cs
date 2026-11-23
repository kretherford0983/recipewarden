using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-EXP-02, AC-25, ADR 0031: the writer's scale and image lines, read back without them.</summary>
[Trait("Req", "FR-EXP-02")]
public sealed class FR_EXP_02_RecipeMarkdownWriteTests
{
    private static readonly RecipeEdit Bread = new()
    {
        Title = "Bread",
        IngredientsText = "6 cups flour",
        StepsText = "Mix.\nKnead.\nBake.",
    };

    [Fact]
    [Trait("Req", "AC-25")]
    public void Scale_and_images_are_written_and_ignored_on_reading()
    {
        var md = RecipeMarkdown.Write(Bread, "bread", null, 1.5m, "images/finish.webp", [null, "images/step-02.webp"]);
        md.ShouldContain("image: \"images/finish.webp\"\nscale: 1.5\nsource:");
        md.ShouldContain("1. Mix.\n2. Knead.\n   ![Step 2](images/step-02.webp)\n3. Bake.\n");

        var back = RecipeMarkdown.Read(md).ShouldHaveSingleItem();
        back.Steps.ShouldBe("Mix.\nKnead.\nBake.");
        back.Ingredients.ShouldBe("6 cups flour");
    }

    [Fact]
    public void One_x_and_no_images_write_neither()
    {
        var md = RecipeMarkdown.Write(Bread, scale: 1m, stepImages: []);
        md.ShouldNotContain("scale:");
        md.ShouldNotContain("![");
        md.ShouldNotContain("image:");
    }
}
