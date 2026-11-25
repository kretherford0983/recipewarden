using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>BR-01, AC-02: a published recipe needs a title (1-200), an ingredient and a step; otherwise Draft.</summary>
[Trait("Req", "BR-01")]
[Trait("Req", "FR-REC-01")]
public sealed class BR_01_PublishRulesTests
{
    [Fact]
    [Trait("Req", "AC-01")]
    public void Complete_recipe_publishes_when_asked()
    {
        var check = PublishRules.Check("Chicken lime soup", ingredientCount: 1, stepCount: 1);

        check.CanPublish.ShouldBeTrue();
        check.Problems.ShouldBeEmpty();
        PublishRules.Decide(wantsPublish: true, check).ShouldBe(RecipeStatus.Published);
    }

    [Fact]
    public void Complete_recipe_saved_as_draft_stays_draft()
    {
        var check = PublishRules.Check("Soup", 1, 1);
        PublishRules.Decide(wantsPublish: false, check).ShouldBe(RecipeStatus.Draft);
    }

    [Fact]
    [Trait("Req", "AC-02")]
    public void Missing_steps_saves_as_draft_with_the_spec_message()
    {
        var check = PublishRules.Check("Soup", ingredientCount: 3, stepCount: 0);

        check.CanPublish.ShouldBeFalse();
        check.Problems.Single().ShouldBe(new PublishProblem(RecipeField.Steps, "Add at least one step to publish"));
        PublishRules.Decide(wantsPublish: true, check).ShouldBe(RecipeStatus.Draft);
    }

    [Fact]
    public void Missing_ingredients_blocks_publishing()
    {
        var check = PublishRules.Check("Soup", 0, 2);
        check.Problems.Single().ShouldBe(new PublishProblem(RecipeField.Ingredients, "Add at least one ingredient to publish"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_title_blocks_publishing_but_a_draft_can_be_saved(string? title)
    {
        var check = PublishRules.Check(title, 1, 1);

        check.CanPublish.ShouldBeFalse();
        check.CanSave.ShouldBeTrue();
        check.Problems.Single().ShouldBe(new PublishProblem(RecipeField.Title, "Add a title to publish"));
    }

    [Fact]
    public void All_missing_parts_are_listed_in_form_order()
    {
        var check = PublishRules.Check("", 0, 0);
        check.Problems.Select(p => p.Field).ShouldBe([RecipeField.Title, RecipeField.Ingredients, RecipeField.Steps]);
    }

    [Fact]
    public void Title_of_exactly_200_characters_is_allowed()
    {
        PublishRules.Check(new string('a', 200), 1, 1).CanPublish.ShouldBeTrue();
    }

    [Fact]
    public void Title_over_200_characters_cannot_be_saved_at_all()
    {
        var check = PublishRules.Check(new string('a', 201), 1, 1);

        check.CanSave.ShouldBeFalse();
        check.CanPublish.ShouldBeFalse();
        check.Errors.Single().ShouldBe(new PublishProblem(RecipeField.Title, "Title must be 200 characters or fewer"));
    }

    [Fact]
    public void Title_length_counts_trimmed_text()
    {
        PublishRules.Check("  " + new string('a', 200) + "  ", 1, 1).CanPublish.ShouldBeTrue();
    }

    [Fact]
    public void Deciding_with_a_title_that_cannot_be_saved_throws()
    {
        var check = PublishRules.Check(new string('a', 201), 1, 1);
        Should.Throw<InvalidOperationException>(() => PublishRules.Decide(true, check));
    }

    [Fact]
    public void Negative_counts_are_a_programming_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PublishRules.Check("x", -1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => PublishRules.Check("x", 0, -1));
    }
}
