using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>ADR 0035, AC-55: imported nutrition from schema.org and recipe-md, and the recipe-md writer's extras.</summary>
[Trait("Req", "FR-NUT-01")]
public sealed class FR_NUT_01_NutritionTextTests
{
    [Theory]
    [InlineData("410 kcal", false, 410)]
    [InlineData("410 calories", false, 410)]
    [InlineData("410", false, 410)]
    [InlineData("14.5 g", false, 14.5)]
    [InlineData("14,5g", false, 14.5)]
    [InlineData("890 mg", true, 890)]
    [InlineData("0.89 g", true, 890)]
    [InlineData("500 mg", false, 0.5)]
    [InlineData("1715 kJ", false, 409.89)]
    public void Schema_org_values_are_read_in_panel_units(string text, bool mg, decimal value) => NutritionText.Parse(text, mg).ShouldBe(value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("9999999 g")]
    public void Other_text_is_not_a_value(string? text) => NutritionText.Parse(text).ShouldBeNull();

    [Fact]
    public void A_schema_org_block_needs_calories()
    {
        var map = new Dictionary<string, string> { ["calories"] = "410 kcal", ["fatContent"] = "14 g", ["sodiumContent"] = "890 mg" };
        NutritionText.FromSchemaOrg(k => map.GetValueOrDefault(k)).ShouldBe(new NutrientValues(410, 14, SodiumMg: 890));
        NutritionText.FromSchemaOrg(_ => null).ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "AC-25")]
    public void The_writer_adds_allergens_and_the_panel_and_the_reader_keeps_only_manual_or_imported_values()
    {
        var edit = new RecipeEdit { Title = "Soup", IngredientsText = "1 cup milk", StepsText = "Heat." };
        var panel = new NutrientValues(410, 14, 5, 95, 890, 38, 6, 7, 32);
        var manual = RecipeMarkdown.Write(edit, "soup", extras: new RecipeMdExtras(["milk", "soy"], panel, "manual"));
        manual.ShouldContain("allergens: [milk, soy]\nnutrition:");
        manual.ShouldContain("nutrition:\n  source: manual\n  per_serving: { calories: 410, fat_g: 14, saturated_fat_g: 5, cholesterol_mg: 95, sodium_mg: 890, carbs_g: 38, fiber_g: 6, sugar_g: 7, protein_g: 32 }\n---");
        RecipeMarkdownValidator.Validate(manual).ShouldBeEmpty();
        RecipeMarkdown.Read(manual).Single().Nutrition.ShouldBe(panel);

        var calculated = RecipeMarkdown.Write(edit, "soup", extras: new RecipeMdExtras([], new NutrientValues(100), "calculated", 80));
        calculated.ShouldContain("  completeness_pct: 80\n");
        calculated.ShouldNotContain("allergens:");
        RecipeMarkdown.Read(calculated).Single().Nutrition.ShouldBeNull();
        RecipeMarkdown.Write(edit, extras: new RecipeMdExtras(Nutrition: new NutrientValues())).ShouldNotContain("nutrition:");
        RecipeMarkdown.Write(edit, extras: new RecipeMdExtras(Nutrition: new NutrientValues(5))).ShouldContain("source: calculated");
    }

    [Fact]
    public void A_recipe_md_panel_without_calories_or_with_bad_numbers_is_dropped()
    {
        NutritionText.FromRecipeMd(k => k == "fat_g" ? "3" : null).ShouldBeNull();
        NutritionText.FromRecipeMd(k => k switch { "calories" => "200", "fat_g" => "-1", _ => "x" }).ShouldBe(new NutrientValues(200));
        NutritionText.ToRecipeMd(new NutrientValues(1.25m)).ShouldBe("{ calories: 1.3 }");
    }
}
