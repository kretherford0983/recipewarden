using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// FR-REC-03, UX-13 (owner feedback 2026-11-23): an ingredient line typed or imported with its own bullet does not
/// show a second bullet, and doubled brackets "((…))" show once.
/// </summary>
[Trait("Req", "FR-REC-03")]
[Trait("Req", "UX-13")]
public sealed class FR_REC_03_IngredientTextTests
{
    [Theory]
    [InlineData("- 1 cup flour", "1 cup flour")]
    [InlineData("* 2 eggs", "2 eggs")]
    [InlineData("• 1 tsp salt", "1 tsp salt")]
    [InlineData("·  3 cloves garlic", "3 cloves garlic")]
    [InlineData("◦ pinch of nutmeg", "pinch of nutmeg")]
    [InlineData("▪ 1 onion", "1 onion")]
    [InlineData("– 1 lemon", "1 lemon")]
    [InlineData("+ 1 lime", "1 lime")]
    [InlineData("- [ ] 2 carrots", "2 carrots")]
    [InlineData("* [x] 1 leek", "1 leek")]
    [InlineData("-- 1 cup milk", "1 cup milk")]
    [InlineData("•1 cup rice", "1 cup rice")]
    public void A_leading_bullet_is_removed(string line, string expected) => IngredientText.Clean(line).ShouldBe(expected);

    [Theory]
    [InlineData("1 ((13.5 oz)) can coconut milk", "1 (13.5 oz) can coconut milk")]
    [InlineData("1 can coconut milk (((13.5 oz)))", "1 can coconut milk (13.5 oz)")]
    [InlineData("2 cups stock ( (low sodium) )", "2 cups stock (low sodium)")]
    [InlineData("1 cup rice (about 1 cup (200 g))", "1 cup rice (about 1 cup (200 g))")]
    [InlineData("salt (to taste) (optional)", "salt (to taste) (optional)")]
    public void Doubled_brackets_show_once(string line, string expected) => IngredientText.Clean(line).ShouldBe(expected);

    [Theory]
    [InlineData("1-2 cups broth")]
    [InlineData("1/2 cup sugar")]
    [InlineData("-1 is not a bullet without a space? keep")]
    [InlineData("Salt and pepper")]
    [InlineData("")]
    public void Other_lines_are_unchanged(string line) => IngredientText.Clean(line).ShouldBe(line);

    [Fact]
    public void The_scaler_shows_cleaned_text_for_lines_saved_before_the_fix()
    {
        var saved = new ParsedIngredient { RawText = "- 1 ((13.5 oz)) can coconut milk", Name = "- 1 ((13.5 oz)) can coconut milk", Confidence = 0m };
        var shown = IngredientScaler.Scale(new ScaleInput(saved), ScaleContext.AsWritten);
        shown.Text.ShouldBe("1 (13.5 oz) can coconut milk");
    }
}
