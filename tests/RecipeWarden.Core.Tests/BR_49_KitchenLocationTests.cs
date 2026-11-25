using RecipeWarden.Core.Shopping;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-49, AC-133: the staples' kitchen locations can be added, renamed, reordered and removed.</summary>
[Trait("Req", "FR-SHOP-05")]
[Trait("Req", "BR-49")]
public sealed class BR_49_KitchenLocationTests
{
    private static readonly IReadOnlyList<string> Start = StapleRules.Locations;

    [Fact]
    [Trait("Req", "AC-133")]
    public void Rows_rename_reorder_add_and_remove_and_staples_follow()
    {
        var (list, moves, error) = StapleRules.EditLocations(Start,
        [
            new("Pantry", "Pantry cupboard"),
            new("Fridge", "Fridge"),
            new("Freezer", "Freezer", Remove: true),
            new("Spice rack", "Spice drawer"),
            new("Under the sink", "Under the sink"),
            new(null, "Garage freezer"),
            new(null, "  "),
        ]);
        error.ShouldBeNull();
        list.ShouldBe(["Pantry cupboard", "Fridge", "Spice drawer", "Under the sink", "Garage freezer", "Other"]);
        moves.ShouldBe(new Dictionary<string, string>
        {
            ["Pantry"] = "Pantry cupboard", ["Freezer"] = "Other", ["Spice rack"] = "Spice drawer",
        }, ignoreOrder: true);
    }

    [Theory]
    [InlineData("", StapleRules.LocationRequired)]
    [InlineData("A location name that is far too long", StapleRules.LocationTooLong)]
    [InlineData("fridge", StapleRules.LocationTwice)]
    [InlineData("other", StapleRules.LocationTwice)]
    public void Bad_rows_are_refused(string name, string error)
    {
        StapleRules.EditLocations(Start, [new("Fridge", "Fridge"), new("Pantry", name)]).Error.ShouldBe(error);
    }

    [Fact]
    public void The_list_has_at_most_twenty_and_other_cannot_be_edited()
    {
        var many = Enumerable.Range(1, 20).Select(i => new LocationEdit(null, "Shelf " + i)).ToList();
        StapleRules.EditLocations(Start, many).Error.ShouldBe(StapleRules.TooManyLocations);

        var (list, moves, _) = StapleRules.EditLocations(Start, [new("Other", "Elsewhere"), new("Fridge", "Fridge")]);
        list.ShouldBe(["Elsewhere", "Fridge", "Other"]); // a row claiming to be Other is a new location
        moves.ShouldBeEmpty();
    }

    [Fact]
    public void Stored_locations_are_read_with_other_last()
    {
        StapleRules.ParseLocations(null).ShouldBe(Start);
        StapleRules.ParseLocations("Fridge\nOther\nPantry\nfridge\n").ShouldBe(["Fridge", "Pantry", "Other"]);
        StapleRules.ParseLocations("Other").ShouldBe(["Other"]);
        StapleRules.DefaultFor(["Fridge", "Other"]).ShouldBe("Other");
        StapleRules.DefaultFor(Start).ShouldBe("Pantry");
        StapleRules.Validate("eggs", "Garage", null, null, ["Garage", "Other"]).ShouldBeNull();
        StapleRules.ByLocation([new StapleInfo(1, null, "a", "Garage", null, null, null), new StapleInfo(2, null, "b", "Fridge", null, null, null)],
            ["Garage", "Fridge", "Other"]).Select(g => g.Key).ShouldBe(["Garage", "Fridge"]);
    }
}
