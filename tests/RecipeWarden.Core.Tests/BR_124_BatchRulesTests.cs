using RecipeWarden.Core.Import;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-124 to BR-127: batch sizes, step headings from bold lines and Markdown nutrition sections.</summary>
[Trait("Req", "FR-IMP-04")]
public sealed class BR_124_BatchRulesTests
{
    private static ParsedIngredient L(decimal? qty, string? unit, string name) =>
        new() { RawText = $"{qty} {unit} {name}".Replace("  ", " ", StringComparison.Ordinal).Trim(), Quantity = qty, UnitCode = unit, Name = name };

    [Theory]
    [Trait("Req", "BR-124")]
    [InlineData("Kansas Corn Bake (Double Batch)", "Kansas Corn Bake", "Double Batch")]
    [InlineData("Kansas Corn Bake (Single Batch)", "Kansas Corn Bake", "Single Batch")]
    [InlineData("Chili - Large Batch", "Chili", "Large Batch")]
    [InlineData("Pancakes x2", "Pancakes", "x2")]
    [InlineData("Pancakes 2x", "Pancakes", "2x")]
    [InlineData("Soup: for 12", "Soup", "for 12")]
    [InlineData("Lemon Bars (Gluten-Free)", "Lemon Bars (Gluten-Free)", null)]
    [InlineData("Rich Chocolate Custard Pudding", "Rich Chocolate Custard Pudding", null)]
    [InlineData(null, "", null)]
    public void Batch_words_split_off_the_title(string? title, string expectedBase, string? qualifier)
    {
        BatchRules.SplitTitle(title).ShouldBe((expectedBase, qualifier));
    }

    [Fact]
    [Trait("Req", "BR-124")]
    public void A_consistent_ratio_is_found_and_lines_off_it_are_listed()
    {
        var single = new[] { L(2m, "cup", "flour"), L(2m, null, "eggs"), L(12m, "oz", "cheese"), L(1m, "tsp", "salt") };
        var dbl = new[] { L(4m, "cup", "flour"), L(4m, null, "eggs"), L(28m, "oz", "cheese"), L(2m, "tsp", "salt"), L(1m, "cup", "parsley") };
        BatchRules.Ratio(single, dbl, out var off).ShouldBe(2m);
        off.ShouldBe(["28 oz cheese", "1 cup parsley"]);

        BatchRules.Ratio([L(1m, "cup", "rice")], [L(3m, "pinch", "rice")], out _).ShouldBeNull(); // vague units cannot be compared
        BatchRules.Ratio([L(1m, "cup", "rice")], [L(1m, "lb", "rice")], out _).ShouldBeNull(); // volume against weight
        BatchRules.Ratio([L(1m, "cup", "rice")], [L(1m, "cup", "beans")], out _).ShouldBeNull(); // different recipes
        BatchRules.Ratio([L(1m, "cup", "rice"), L(2m, "cup", "milk")], [L(1.5m, "cup", "rice"), L(3m, "cup", "milk")], out _).ShouldBe(1.5m);
        BatchRules.Ratio([L(3m, "cup", "rice")], [L(1m, "cup", "rice")], out _).ShouldBe(0.33m);
        BatchRules.Ratio([L(null, null, "rice")], [L(null, null, "rice")], out _).ShouldBeNull(); // no amounts
    }

    [Fact]
    [Trait("Req", "BR-125")]
    public void A_half_batch_without_steps_folds_with_only_what_differs()
    {
        var full = new ImportedRecipe { Title = "Rolls", Ingredients = "4 cup flour\n2 tsp salt", Steps = "1. Mix.", Servings = "12", Notes = "Freeze well." };
        var half = new ImportedRecipe { Title = "Rolls (Half Batch)", Ingredients = "2 cup flour\n1 tsp salt", Steps = "1. Mix.", Servings = "6", Notes = "Freeze well." };
        IReadOnlyList<ParsedIngredient> Parse(string? text) => (text ?? string.Empty).Split('\n')
            .Select(l => l.Split(' ', 3)).Select(p => L(decimal.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p[1], p[2])).ToList();

        var folded = BatchRules.Fold([full, half], Parse).ShouldHaveSingleItem();
        folded.Title.ShouldBe("Rolls");
        folded.Notes.ShouldBe("Freeze well.\n\nHalf Batch (from import, 0.5×):");
        BatchRules.Fold([full], Parse).ShouldBe([full]);

        // Two plain titles are never a batch group (BR-126).
        BatchRules.Fold([full, full with { }], Parse).Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "BR-127")]
    public void Two_versions_of_one_ingredient_group_are_named()
    {
        BatchRules.AlternativeGroups(["Corn:", "1 cup corn", "Sauce (Side Dish):", "1 Tbsp mayo", "Sauce (Taco Topping):", "2 Tbsp mayo", "Topping (Optional):"])
            .ShouldBe(["Sauce"]);
        BatchRules.AlternativeGroupsRemark(["Sauce"]).ShouldStartWith("Two versions of “Sauce” are listed");
        BatchRules.AlternativeGroups(["For the crust:", "For the filling:"]).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "BR-127")]
    public void A_bold_line_before_a_numbered_item_heads_it_and_indented_bullets_stay_in_the_step()
    {
        var steps = StepTextRules.Split("**Char Corn**\n1. Heat the skillet.\n\n**Finish**\n2. Add cheese.\n    - *As side dish:* Serve.\n    - Or spoon over tacos.\n\n**Alone**");
        steps.ShouldBe(
        [
            new StepText(null, "Heat the skillet.", "Char Corn"),
            new StepText(null, "Add cheese. *As side dish:* Serve. Or spoon over tacos.", "Finish"),
            new StepText(null, "Alone"),
        ]);
    }

    [Fact]
    [Trait("Req", "BR-127")]
    public void A_nutrition_section_is_read_from_a_table_or_label_lines()
    {
        var table = NutritionText.FromLabelLines(["| | |", "|---|---|", "|**Calories:**|250 kcal|", "|**Saturated Fat:**|4.5g|", "|**Sodium:**|145mg|",
            "|**Carbohydrates:**|39g|", "|**Fiber:**|0g|", "|**Sugar:**|34g|", "|**Protein:**|6g|", "|**Fat:**|8g|", "|Cholesterol|0.1 g|", "|Vitamin C|3 mg|"]);
        table.ShouldBe(new NutrientValues(250m, 8m, 4.5m, 100m, 145m, 39m, 0m, 34m, 6m));

        NutritionText.FromLabelLines(["- Calories: 410", "Total Fat: 14 g", "Dietary fiber: 6g", "no colon here", ": empty"])!
            .ShouldBe(new NutrientValues(410m, 14m, FiberG: 6m));
        NutritionText.FromLabelLines(["Protein: 6 g"]).ShouldBeNull();
    }
}
