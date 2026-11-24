using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Shopping;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-SHOP-03 (revised), FR-SHOP-05, BR-43 to BR-47, AC-51, AC-52, AC-54: staples on a list.</summary>
[Trait("Req", "FR-SHOP-05")]
public sealed class FR_SHOP_05_StapleRulesTests
{
    private const decimal Tbsp = 14.78676478125m;
    private const decimal Cup = 236.5882365m;
    private static readonly ShoppingFood Butter = new(1, "butter", 0.9595m, "Dairy and Eggs", true, Cup / 2, UnitKind.Volume, "stick");
    private static readonly ShoppingFood Flour = new(2, "all-purpose flour", 0.5283m, "Baking", true);
    private static readonly ShoppingFood Eggs = new(3, "egg", null, "Dairy and Eggs", true, 12m, UnitKind.Count, "carton");
    private static readonly Dictionary<int, ShoppingFood> Foods = new[] { Butter, Flour, Eggs }.ToDictionary(f => f.Id);

    private static ShoppingLineInput Line(string name, int? food, decimal? amount, UnitKind? kind, string? recipe = "Roux", bool restock = false,
        string? section = null) =>
        new(0, name, kind, amount, null, null, food, $"{amount} {name}", recipe, false, restock, section);

    [Fact]
    [Trait("Req", "AC-51")]
    [Trait("Req", "BR-43")]
    [Trait("Req", "BR-44")]
    public void Recipe_staples_wait_to_be_checked_and_need_it_moves_one_to_its_section()
    {
        var lines = new[] { Line("butter", 1, 4 * Tbsp, UnitKind.Volume), Line("all-purpose flour", 2, Cup / 2, UnitKind.Volume) };
        var view = ShoppingRules.Merge(lines, Foods, new HashSet<string>());
        view.ToBuy.ShouldBeEmpty();
        view.Pantry.Select(i => i.Name).ShouldBe(["all-purpose flour", "butter"]);
        view.Pantry.Single(i => i.Name == "butter").AmountText.ShouldBe("¼ cup");
        view.Pantry.ShouldAllBe(i => i.Recipes.Single() == "Roux");
        view.ToCheck.ShouldBe(2);
        ShoppingRules.PantryHeading.ShouldBe("Check before you buy");

        var answers = new Dictionary<string, StapleAnswer> { ["f:1"] = StapleAnswer.Need };
        var after = ShoppingRules.Merge(lines, Foods, new HashSet<string>(), answers: answers);
        after.Sections.ShouldHaveSingleItem().Name.ShouldBe("Dairy and Eggs");
        after.ToBuy.ShouldHaveSingleItem().Answer.ShouldBe(StapleAnswer.Need);
        after.Pantry.ShouldHaveSingleItem().Name.ShouldBe("all-purpose flour");
        after.ToCheck.ShouldBe(1);
    }

    [Fact]
    [Trait("Req", "AC-52")]
    [Trait("Req", "BR-46")]
    public void Have_it_stays_checked_off_below_and_unanswered_staples_do_not_stop_a_list_finishing()
    {
        var lines = new[] { Line("butter", 1, 4 * Tbsp, UnitKind.Volume), Line("all-purpose flour", 2, Cup / 2, UnitKind.Volume), Line("lemon", null, 2m, UnitKind.Count) };
        var answers = new Dictionary<string, StapleAnswer> { ["f:2"] = StapleAnswer.Have };
        var view = ShoppingRules.Merge(lines, Foods, new HashSet<string> { "n:lemon" }, answers: answers);
        view.Pantry.Select(i => (i.Name, i.Answer)).ShouldBe([("butter", (StapleAnswer?)null), ("all-purpose flour", StapleAnswer.Have)]);
        view.ToCheck.ShouldBe(1);
        view.IsFinished.ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "AC-54")]
    [Trait("Req", "BR-45")]
    public void A_restock_and_a_recipe_need_merge_into_one_item_with_both_sources()
    {
        var lines = new[]
        {
            Line("butter", 1, 4 * Tbsp, UnitKind.Volume),
            Line("butter", 1, 4 * Cup / 2, UnitKind.Volume, recipe: null, restock: true),
            Line("aluminum foil", null, null, null, recipe: null, restock: true, section: "Other"),
            Line("dish soap", null, null, null, recipe: null, restock: true, section: "Condiments"),
        };
        var view = ShoppingRules.Merge(lines, Foods, new HashSet<string>());
        view.Pantry.ShouldBeEmpty();
        var butter = view.ToBuy.Single(i => i.Name == "butter");
        butter.Recipes.ShouldBe(["Roux", ShoppingRules.RestockSource]);
        butter.IsRestock.ShouldBeTrue();
        butter.AmountText.ShouldBe("2¼ cups");
        view.ToBuy.Single(i => i.Name == "dish soap").Section.ShouldBe("Condiments");
    }

    [Theory]
    [InlineData("eggs", "Fridge", "1 dozen", null, null)]
    [InlineData("", "Fridge", null, null, StapleRules.NameRequired)]
    [InlineData("eggs", "Garage", null, null, StapleRules.BadLocation)]
    [InlineData("eggs", null, null, null, StapleRules.BadLocation)]
    [InlineData("eggs", "Fridge", "a very long usual amount that goes on and on", null, StapleRules.RestockTooLong)]
    [InlineData("eggs", "Fridge", null, "Aisle 9", ShoppingRules.BadSection)]
    [InlineData("eggs", "Fridge", null, "Dairy and Eggs", null)]
    public void Staples_are_checked(string name, string? location, string? restock, string? section, string? error)
    {
        StapleRules.Validate(name, location, restock, section).ShouldBe(error);
        StapleRules.Validate(new string('x', 81), "Fridge", null, null).ShouldBe(StapleRules.NameTooLong);
    }

    [Theory]
    [Trait("Req", "BR-45")]
    [InlineData("1 dozen", "12")]
    [InlineData("a dozen", "12")]
    [InlineData("half a dozen", "6")]
    [InlineData("2 Dozen", "24")]
    [InlineData("1.5 dozen", "18")]
    [InlineData("some dozen", "some dozen")]
    [InlineData(" 5 lb ", "5 lb")]
    [InlineData("dozen", "dozen")]
    public void Dozens_are_counted(string restock, string expected) => StapleRules.RestockAmount(restock).ShouldBe(expected);

    [Fact]
    [Trait("Req", "BR-47")]
    public void The_walk_goes_by_location_and_says_when_each_was_last_bought()
    {
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        var groups = StapleRules.ByLocation(
        [
            new StapleInfo(1, null, "foil", "Under the sink", null, null, null),
            new StapleInfo(2, 3, "egg", "Fridge", "1 dozen", null, now.AddDays(-21)),
            new StapleInfo(3, 2, "flour", "Pantry", null, null, null),
            new StapleInfo(4, null, "butter", "Fridge", null, null, null),
            new StapleInfo(5, null, "odd", "Shed", null, null, null),
        ]);
        groups.Select(g => g.Key).ShouldBe(["Fridge", "Pantry", "Under the sink", "Shed"]);
        groups[0].Select(s => s.Name).ShouldBe(["butter", "egg"]);

        StapleRules.LastBought(null, now).ShouldBeNull();
        StapleRules.LastBought(now.AddHours(-3), now).ShouldBe("last bought today");
        StapleRules.LastBought(now.AddDays(-1), now).ShouldBe("last bought yesterday");
        StapleRules.LastBought(now.AddDays(-5), now).ShouldBe("last bought 5 days ago");
        StapleRules.LastBought(now.AddDays(-21), now).ShouldBe("last bought 3 weeks ago");
        StapleRules.LastBought(now.AddDays(-95), now).ShouldBe("last bought 3 months ago");
    }
}
