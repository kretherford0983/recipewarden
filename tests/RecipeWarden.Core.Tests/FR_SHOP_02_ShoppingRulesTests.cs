using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Shopping;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-SHOP-02, FR-SHOP-03, BR-40, BR-41, BR-42, AC-50, AC-51, ADR 0029/0030: merging a list.</summary>
[Trait("Req", "FR-SHOP-02")]
public sealed class FR_SHOP_02_ShoppingRulesTests
{
    private const decimal Cup = 236.5882365m;
    private const decimal Tbsp = 14.78676478125m;
    private const decimal Oz = 28.349523125m;

    private static readonly ShoppingFood Butter = new(1, "butter", 0.9595m, "Dairy and Eggs", false, Cup / 2, UnitKind.Volume, "stick");
    private static readonly ShoppingFood Salt = new(2, "salt", 1.2m, "Spices", true);
    private static readonly ShoppingFood Beans = new(3, "black beans", null, "Canned Goods", false, 1m, UnitKind.Count, "can");
    private static readonly ShoppingFood Eggs = new(4, "egg", null, "Dairy and Eggs", false, 12m, UnitKind.Count, "carton");
    private static readonly ShoppingFood Flour = new(5, "all-purpose flour", 0.5283m, "Baking", false);
    private static readonly ShoppingFood Chicken = new(6, "chicken breast", null, "Meat and Seafood", false);
    private static readonly ShoppingFood Tomatoes = new(7, "diced tomatoes", 1.0m, "Canned Goods", false, 411m, UnitKind.Mass, "can");

    private static readonly Dictionary<int, ShoppingFood> Foods = new[] { Butter, Salt, Beans, Eggs, Flour, Chicken, Tomatoes }
        .ToDictionary(f => f.Id);

    private static int next;

    private static ShoppingLineInput Line(string name, UnitKind? kind, decimal? amount, int? foodId = null, string? recipe = "Cookies",
        decimal? max = null, string? countUnit = null) =>
        new(Interlocked.Increment(ref next), name, kind, amount, max, countUnit, foodId, $"{amount} {name}", recipe);

    private static ShoppingView Merge(params ShoppingLineInput[] lines) =>
        ShoppingRules.Merge(lines, Foods, new HashSet<string>());

    [Fact]
    [Trait("Req", "AC-50")]
    public void One_cup_and_four_tablespoons_of_butter_from_two_recipes_merge_into_one_line_with_sticks()
    {
        var view = Merge(Line("butter", UnitKind.Volume, Cup, 1, "Cookies"), Line("butter", UnitKind.Volume, 4 * Tbsp, 1, "Brownies"));

        var item = view.ToBuy.ShouldHaveSingleItem();
        item.AmountText.ShouldBe("1¼ cups");
        item.PackageText.ShouldBe("2½ sticks");
        item.Recipes.ShouldBe(["Cookies", "Brownies"]);
        ShoppingText.Describe(item, markdown: false).ShouldBe("butter — 1¼ cups (2½ sticks)");
    }

    [Fact]
    [Trait("Req", "BR-42")]
    public void An_item_keeps_every_source_line_and_each_recipe_once()
    {
        var view = Merge(Line("butter", UnitKind.Volume, Cup, 1, "Cookies"), Line("butter", UnitKind.Volume, Tbsp, 1, "Cookies"));
        var item = view.ToBuy.Single();
        item.Recipes.ShouldBe(["Cookies"]);
        item.Lines.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "BR-40")]
    public void Volume_and_mass_merge_when_the_food_has_a_density()
    {
        var view = Merge(Line("flour", UnitKind.Volume, Cup, 5), Line("flour", UnitKind.Mass, 125m, 5), Line("flour", UnitKind.Volume, Cup, 5));
        var item = view.ToBuy.ShouldHaveSingleItem();
        item.AmountText.ShouldNotBeNull().ShouldEndWith("cups");
        item.AmountText.ShouldNotContain("+");
    }

    [Fact]
    [Trait("Req", "BR-40")]
    public void Volume_and_mass_without_a_density_stay_separate_amounts_on_the_same_item()
    {
        var view = Merge(Line("chicken", UnitKind.Mass, 16 * Oz, 6), Line("chicken", UnitKind.Volume, 2 * Cup, 6));
        var item = view.ToBuy.ShouldHaveSingleItem();
        item.AmountText.ShouldBe("2 cups + 1 lb");
    }

    [Fact]
    [Trait("Req", "BR-40")]
    public void Lines_without_a_food_merge_by_name_only()
    {
        var view = Merge(Line("Fresh Basil", UnitKind.Volume, Cup / 4, null), Line("fresh basil", UnitKind.Volume, Cup / 4, null),
            Line("parsley", UnitKind.Volume, Cup / 4, null));
        view.ToBuy.Count().ShouldBe(2);
        view.ToBuy.Single(i => i.Key == "n:fresh basil").AmountText.ShouldBe("½ cup");
    }

    [Fact]
    [Trait("Req", "BR-40")]
    public void Counts_merge_by_count_unit_and_lines_without_amounts_keep_the_item()
    {
        var view = Merge(Line("garlic", UnitKind.Count, 2m, null, countUnit: "clove"), Line("garlic", UnitKind.Count, 3m, null, countUnit: "clove"),
            Line("garlic", UnitKind.Count, 1m, null), Line("pepper", null, null, null));
        view.ToBuy.Single(i => i.Name == "garlic").AmountText.ShouldBe("1 + 5 cloves");
        view.ToBuy.Single(i => i.Name == "pepper").AmountText.ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "BR-41")]
    public void Package_counts_round_up_unless_whole_or_half()
    {
        Merge(Line("beans", UnitKind.Count, 3m, 3, countUnit: "can")).ToBuy.Single().AmountText.ShouldBe("3 cans");
        Merge(Line("eggs", UnitKind.Count, 6m, 4)).ToBuy.Single().PackageText.ShouldBe("½ carton");
        Merge(Line("eggs", UnitKind.Count, 14m, 4)).ToBuy.Single().PackageText.ShouldBe("buy 2 cartons");
        Merge(Line("tomatoes", UnitKind.Mass, 500m, 7)).ToBuy.Single().PackageText.ShouldBe("buy 2 cans");
        Merge(Line("tomatoes", UnitKind.Volume, 411m, 7)).ToBuy.Single().PackageText.ShouldBe("1 can");
        Merge(Line("butter", UnitKind.Count, 3m, 1, countUnit: "stick")).ToBuy.Single().AmountText.ShouldBe("1½ cups");
        Merge(Line("butter", UnitKind.Mass, 60m, 1)).ToBuy.Single().PackageText.ShouldBe("buy 1 stick");
    }

    [Fact]
    [Trait("Req", "BR-41")]
    public void No_package_text_without_a_package_or_an_amount()
    {
        ShoppingRules.Packages(100m, UnitKind.Volume, null).ShouldBeNull();
        ShoppingRules.Packages(0m, UnitKind.Volume, Butter).ShouldBeNull();
        ShoppingRules.Packages(100m, UnitKind.Volume, Beans).ShouldBeNull();
        ShoppingRules.Packages(100m, UnitKind.Mass, Butter with { DensityGPerMl = null }).ShouldBeNull();
        ShoppingRules.Packages(100m, UnitKind.Volume, Butter with { PackageName = "" }).ShouldBeNull();
        ShoppingRules.Packages(24m, UnitKind.Count, Eggs).ShouldBe("2 cartons");
        ShoppingRules.Packages(2m, UnitKind.Count, Beans with { PackageName = "box" }).ShouldBe("2 boxes");
        ShoppingRules.Packages(2m, UnitKind.Count, Beans with { PackageName = "Pack" }).ShouldBe("2 Packs");
    }

    [Fact]
    public void Ranges_show_low_to_high_in_one_unit()
    {
        Merge(Line("butter", UnitKind.Volume, Cup / 2, 1, max: Cup)).ToBuy.Single().AmountText.ShouldBe("½–1 cup");
        Merge(Line("garlic", UnitKind.Count, 2m, null, max: 3m)).ToBuy.Single().AmountText.ShouldBe("2–3");
    }

    [Fact]
    [Trait("Req", "AC-51")]
    public void Pantry_staples_are_listed_apart_and_do_not_hold_the_list_open()
    {
        var keys = new HashSet<string> { ShoppingRules.KeyFor(1, "butter") };
        var view = ShoppingRules.Merge([Line("salt", UnitKind.Volume, 5m, 2), Line("butter", UnitKind.Volume, Cup, 1)], Foods, keys);

        view.Pantry.ShouldHaveSingleItem().Name.ShouldBe("salt");
        view.ToBuy.ShouldHaveSingleItem().IsChecked.ShouldBeTrue();
        view.IsFinished.ShouldBeTrue();
        ShoppingText.Plain("Saturday", view).ShouldContain(ShoppingRules.PantryHeading);
    }

    [Fact]
    [Trait("Req", "FR-SHOP-03")]
    public void An_empty_or_unticked_list_is_not_finished_and_sections_follow_the_store()
    {
        ShoppingRules.Merge([], Foods, new HashSet<string>()).IsFinished.ShouldBeFalse();
        var view = Merge(Line("tomatoes", UnitKind.Mass, 100m, 7), Line("butter", UnitKind.Volume, Cup, 1),
            Line("Mystery", null, null, null), Line("thing", null, null, 99), Line("chicken", UnitKind.Mass, 100m, 6),
            Line("sprinkles", null, null, null) with { FoodId = 50 });
        view.IsFinished.ShouldBeFalse();
        view.Sections.Select(s => s.Name).ShouldBe(["Meat and Seafood", "Dairy and Eggs", "Canned Goods", "Other"]);
    }

    [Fact]
    public void Unknown_sections_sort_before_other_by_name()
    {
        var foods = new Dictionary<int, ShoppingFood>
        {
            [1] = new(1, "a", null, "Zeta", false),
            [2] = new(2, "b", null, "Alpha", false),
            [3] = new(3, "c", null, "Produce", false),
        };
        var view = ShoppingRules.Merge([Line("x", null, null, 1), Line("y", null, null, 2), Line("z", null, null, 3), Line("q", null, null, null)],
            foods, new HashSet<string>());
        view.Sections.Select(s => s.Name).ShouldBe(["Produce", "Alpha", "Zeta", "Other"]);
    }

    [Fact]
    public void Blank_names_are_ignored_and_metric_lists_show_metric()
    {
        var view = ShoppingRules.Merge([Line(" ", UnitKind.Mass, 5m), Line("flour", UnitKind.Mass, 250m, 5)], Foods,
            new HashSet<string>(), UnitSystem.Metric);
        view.ToBuy.ShouldHaveSingleItem().AmountText.ShouldBe("250 g");
    }

    [Fact]
    [Trait("Req", "FR-SHOP-04")]
    public void Text_exports_skip_ticked_items_and_escape_markdown()
    {
        var keys = new HashSet<string> { ShoppingRules.KeyFor(1, "butter") };
        var view = ShoppingRules.Merge([Line("butter", UnitKind.Volume, Cup, 1), Line("[evil](http://x) *b*", null, null),
            Line("salt", null, null, 2)], Foods, keys);

        var plain = ShoppingText.Plain("Trip", view);
        plain.ShouldNotContain("butter");
        plain.ShouldContain("[ ] [evil](http://x) *b*");
        ShoppingText.Plain("Trip", view, includeChecked: true).ShouldContain("[x] butter — 1 cup (2 sticks)");

        var md = ShoppingText.Markdown("Trip #1", view);
        md.ShouldStartWith("# Trip \\#1\n");
        md.ShouldContain("- [ ] \\[evil\\]\\(http://x\\) \\*b\\*");
        md.ShouldContain("## Check before you buy");
        ShoppingText.Markdown("Trip", view, includeChecked: true).ShouldContain("- [x] butter");
        ShoppingText.Markdown("Trip", ShoppingRules.Merge([Line("butter", null, null, 1)], Foods, keys)).ShouldBe("# Trip\n");
    }
}
