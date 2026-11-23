using RecipeWarden.Core.Nutrition;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-ALG-01, FR-ALG-02, BR-52, AC-56, ADR 0036: allergen word rules.</summary>
[Trait("Req", "FR-ALG-01")]
public sealed class FR_ALG_01_AllergenRulesTests
{
    private static AllergenCode[] Contains(string line, string? food = null) =>
        [.. AllergenRules.Detect(line, food).Where(p => p.Value == AllergenStatus.Contains).Select(p => p.Key).Order()];

    [Fact]
    [Trait("Req", "AC-56")]
    public void Soy_sauce_is_soy_wheat_and_gluten_and_tamari_is_soy_only()
    {
        Contains("2 Tbsp soy sauce").ShouldBe([AllergenCode.Wheat, AllergenCode.Soy, AllergenCode.Gluten]);
        Contains("2 Tbsp tamari").ShouldBe([AllergenCode.Soy]);
        Contains("2 Tbsp gluten-free soy sauce").ShouldBe([AllergenCode.Soy]);
    }

    [Theory]
    [InlineData("1 cup whole milk", AllergenCode.Milk)]
    [InlineData("4 Tbsp unsalted butter", AllergenCode.Milk)]
    [InlineData("1 cup half-and-half", AllergenCode.Milk)]
    [InlineData("½ cup crème fraîche", AllergenCode.Milk)]
    [InlineData("2 large eggs", AllergenCode.Egg)]
    [InlineData("3 egg yolks", AllergenCode.Egg)]
    [InlineData("¼ cup mayonnaise", AllergenCode.Egg)]
    [InlineData("4 anchovies", AllergenCode.Fish)]
    [InlineData("1 Tbsp Worcestershire sauce", AllergenCode.Fish)]
    [InlineData("1 lb shrimp, peeled", AllergenCode.Shellfish)]
    [InlineData("½ cup chopped walnuts", AllergenCode.TreeNuts)]
    [InlineData("¼ cup pine nuts", AllergenCode.TreeNuts)]
    [InlineData("2 Tbsp peanut butter", AllergenCode.Peanuts)]
    [InlineData("2 cups all-purpose flour", AllergenCode.Wheat)]
    [InlineData("8 oz spaghetti", AllergenCode.Wheat)]
    [InlineData("1 cup pearl barley", AllergenCode.Gluten)]
    [InlineData("1 Tbsp white miso", AllergenCode.Soy)]
    [InlineData("¼ cup tahini", AllergenCode.Sesame)]
    [InlineData("1 tsp toasted sesame oil", AllergenCode.Sesame)]
    public void Common_lines_are_detected(string line, AllergenCode code) => Contains(line).ShouldContain(code);

    [Theory]
    [InlineData("1 can coconut milk", AllergenCode.Milk)]
    [InlineData("1 cup almond milk", AllergenCode.Milk)]
    [InlineData("2 Tbsp peanut butter", AllergenCode.Milk)]
    [InlineData("1 eggplant", AllergenCode.Egg)]
    [InlineData("½ tsp nutmeg", AllergenCode.TreeNuts)]
    [InlineData("1 cup shredded coconut", AllergenCode.TreeNuts)]
    [InlineData("1 cup roasted chestnuts", AllergenCode.TreeNuts)]
    [InlineData("1 lb butternut squash", AllergenCode.Milk)]
    [InlineData("2 cups almond flour", AllergenCode.Wheat)]
    [InlineData("8 corn tortillas", AllergenCode.Wheat)]
    [InlineData("4 oz rice noodles", AllergenCode.Gluten)]
    [InlineData("1 tsp buckwheat groats", AllergenCode.Wheat)]
    [InlineData("1 tsp cream of tartar", AllergenCode.Milk)]
    [InlineData("1 cup vegan butter", AllergenCode.Milk)]
    [InlineData("1 cup dairy-free yogurt", AllergenCode.Milk)]
    [InlineData("2 cups shellfish stock", AllergenCode.Fish)]
    [InlineData("1 bunch swiss chard", AllergenCode.Milk)]
    public void Look_alikes_are_not_detected(string line, AllergenCode code) => Contains(line).ShouldNotContain(code);

    [Theory]
    [InlineData("1 cup rolled oats", AllergenCode.Gluten)]
    [InlineData("4 cups chicken broth", AllergenCode.Gluten)]
    [InlineData("½ cup chocolate chips", AllergenCode.Milk)]
    [InlineData("2 Tbsp red curry paste", AllergenCode.Shellfish)]
    [InlineData("1 cup granola", AllergenCode.TreeNuts)]
    [InlineData("½ cup mixed nuts", AllergenCode.TreeNuts)]
    public void Vague_lines_may_contain(string line, AllergenCode code) =>
        AllergenRules.Detect(line)[code].ShouldBe(AllergenStatus.MayContain);

    [Fact]
    public void Gluten_free_oats_are_not_flagged_and_the_food_name_counts_too()
    {
        AllergenRules.Detect("1 cup gluten-free rolled oats").ShouldNotContainKey(AllergenCode.Gluten);
        Contains("1 cup AP", "all-purpose flour").ShouldContain(AllergenCode.Wheat);
        AllergenRules.Detect(null).ShouldBeEmpty();
        AllergenRules.Detect("  ").ShouldBeEmpty();
        AllergenRules.Detect("2 cups water", "water").ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "BR-52")]
    public void A_recipe_merges_lines_and_manual_rows_win()
    {
        var auto = AllergenRules.ForRecipe([("½ cup chocolate chips", null), ("1 cup milk", "whole milk"), ("2 Tbsp soy sauce", null), ("salt", null)]);
        auto[AllergenCode.Milk].ShouldBe(AllergenStatus.Contains); // Contains beats May contain
        auto[AllergenCode.Wheat].ShouldBe(AllergenStatus.Contains);

        // AC-56: an editor removes Wheat (gluten-free tamari) and adds Sesame.
        var manual = new Dictionary<AllergenCode, AllergenStatus> { [AllergenCode.Wheat] = AllergenStatus.NotPresent, [AllergenCode.Sesame] = AllergenStatus.MayContain };
        var shown = AllergenRules.Effective(auto, manual);
        shown.ShouldNotContainKey(AllergenCode.Wheat);
        shown[AllergenCode.Sesame].ShouldBe(AllergenStatus.MayContain);
        shown[AllergenCode.Soy].ShouldBe(AllergenStatus.Contains);
    }

    [Fact]
    public void Labels_and_normalizing()
    {
        AllergenRules.All.Count.ShouldBe(10);
        AllergenRules.All.Select(AllergenRules.Label).ShouldAllBe(l => l.Length > 2);
        AllergenRules.Label(AllergenCode.Shellfish).ShouldBe("Crustacean shellfish");
        AllergenRules.Normalize("Half-and-Half, CRÈME!").ShouldBe(" half and half creme ");
        AllergenRules.Normalize(null).ShouldBe(string.Empty);
    }
}
