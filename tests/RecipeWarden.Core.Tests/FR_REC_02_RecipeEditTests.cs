using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-REC-02, UX-08: field limits checked in Core so the editor and any later API share the same rules.</summary>
[Trait("Req", "FR-REC-02")]
[Trait("Req", "UX-08")]
public sealed class FR_REC_02_RecipeEditTests
{
    private static readonly RecipeEdit Valid = new()
    {
        Title = "Soup",
        IngredientsText = "1 onion",
        StepsText = "Cook.",
        Tags = ["dinner"],
    };

    [Fact]
    public void A_simple_recipe_has_no_errors()
    {
        Valid.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Each_limit_names_its_field()
    {
        var bad = Valid with
        {
            Description = new string('d', 4001),
            Servings = 0m,
            PrepMinutes = -1,
            CookMinutes = 20000,
            TotalMinutes = 10081,
            Notes = new string('n', 10001),
            SourceType = SourceType.Web,
            StepsText = new string('s', 4001),
            IngredientsText = new string('i', 501),
            Tags = ["bad\u0000tag"],
        };

        bad.Validate().Select(e => e.Field).ShouldBe(
        [
            RecipeField.Description, RecipeField.Servings, RecipeField.PrepMinutes, RecipeField.CookMinutes,
            RecipeField.TotalMinutes, RecipeField.Notes, RecipeField.SourceUrl, RecipeField.SourceName,
            RecipeField.Ingredients, RecipeField.Steps, RecipeField.Tags,
        ]);
    }

    [Fact]
    public void Too_many_ingredient_lines_or_tags()
    {
        var bad = Valid with
        {
            IngredientsText = string.Join('\n', Enumerable.Repeat("1 egg", 201)),
            Tags = Enumerable.Range(0, 31).Select(i => $"t{i}").ToArray(),
        };

        bad.Validate().Select(e => e.Message).ShouldBe(
            ["A recipe can have at most 200 ingredient lines", "A recipe can have at most 30 tags"]);
    }

    [Fact]
    public void Defaults_are_a_manual_draft()
    {
        var edit = new RecipeEdit();
        edit.SourceType.ShouldBe(SourceType.Manual);
        edit.WantsPublish.ShouldBeFalse();
        edit.Tags.ShouldBeEmpty();
    }
}
