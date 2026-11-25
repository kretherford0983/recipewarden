using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-06: the second of two concurrent saves is rejected with a field-level diff.</summary>
[Trait("Req", "BR-06")]
public sealed class BR_06_RecipeDiffTests
{
    private static RecipeSnapshot Base() => new()
    {
        Title = "Soup",
        Description = "Warm.",
        Servings = 4m,
        PrepMinutes = 10,
        CookMinutes = 20,
        TotalMinutes = 30,
        IsPrecision = false,
        Notes = null,
        SourceType = SourceType.Manual,
        SourceName = null,
        SourceUrl = null,
        SourceAuthor = null,
        Ingredients = ["1 onion", "2 cups stock"],
        Steps = ["Chop.", "Simmer."],
        Tags = ["dinner"],
    };

    [Fact]
    public void Identical_snapshots_have_no_differences()
    {
        RecipeDiff.Compare(Base(), Base()).ShouldBeEmpty();
    }

    [Fact]
    public void Changed_scalar_fields_are_listed_with_both_values()
    {
        var mine = Base() with { Title = "Better soup", Servings = 6m };
        var theirs = Base() with { Title = "Soup v2" };

        var diff = RecipeDiff.Compare(mine, theirs);

        diff.ShouldBe(
        [
            new FieldDifference(RecipeField.Title, "Better soup", "Soup v2"),
            new FieldDifference(RecipeField.Servings, "6", "4"),
        ]);
    }

    [Fact]
    public void List_fields_compare_in_order_and_show_one_line_per_item()
    {
        var mine = Base() with { Ingredients = ["2 cups stock", "1 onion"] };

        var diff = RecipeDiff.Compare(mine, Base()).Single();

        diff.Field.ShouldBe(RecipeField.Ingredients);
        diff.Mine.ShouldBe("2 cups stock\n1 onion");
        diff.Theirs.ShouldBe("1 onion\n2 cups stock");
    }

    [Fact]
    public void Tags_compare_without_regard_to_order_or_case()
    {
        var mine = Base() with { Tags = ["Soup", "dinner"] };
        var theirs = Base() with { Tags = ["dinner", "soup"] };

        RecipeDiff.Compare(mine, theirs).ShouldBeEmpty();
    }

    [Fact]
    public void Null_and_empty_text_are_the_same()
    {
        var mine = Base() with { Notes = "" };
        RecipeDiff.Compare(mine, Base()).ShouldBeEmpty();
    }

    [Fact]
    public void Every_field_is_compared()
    {
        var mine = Base() with
        {
            Description = "x",
            PrepMinutes = 1,
            CookMinutes = 2,
            TotalMinutes = 3,
            IsPrecision = true,
            Notes = "n",
            SourceType = SourceType.Web,
            SourceName = "Site",
            SourceUrl = "https://example.com/",
            SourceAuthor = "Jane",
            Steps = ["Only step."],
            Tags = ["other"],
        };

        RecipeDiff.Compare(mine, Base()).Select(d => d.Field).ShouldBe(
        [
            RecipeField.Description, RecipeField.PrepMinutes, RecipeField.CookMinutes, RecipeField.TotalMinutes,
            RecipeField.IsPrecision, RecipeField.Notes, RecipeField.SourceType, RecipeField.SourceName,
            RecipeField.SourceUrl, RecipeField.SourceAuthor, RecipeField.Steps, RecipeField.Tags,
        ]);
    }

    [Fact]
    public void Missing_numbers_show_as_empty()
    {
        var mine = Base() with { PrepMinutes = null };
        RecipeDiff.Compare(mine, Base()).Single().ShouldBe(new FieldDifference(RecipeField.PrepMinutes, "", "10"));
    }

    [Fact]
    public void Conflict_message_is_the_spec_text()
    {
        RecipeDiff.ConflictMessage.ShouldContain("changed by someone else");
    }
}
