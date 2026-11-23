using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>ADR 0023: a step photo follows the same text, else the same position.</summary>
[Trait("Req", "ADR-0023")]
public sealed class ADR_0023_StepPhotoRulesTests
{
    [Fact]
    public void Same_text_wins_over_position()
    {
        StepPhotoRules.Follow(["A", "B", "C"], ["C", "A", "B"]).ShouldBe([1, 2, 0]);
    }

    [Fact]
    public void Reworded_steps_keep_their_position()
    {
        StepPhotoRules.Follow(["A", "B"], [" A ", "B2"]).ShouldBe([0, 1]);
    }

    [Fact]
    public void Removed_steps_lose_their_photo()
    {
        StepPhotoRules.Follow(["A", "B", "C"], ["A"]).ShouldBe([0, null, null]);
        StepPhotoRules.Follow([], ["A"]).ShouldBeEmpty();
    }

    [Fact]
    public void A_new_step_takes_at_most_one_photo()
    {
        StepPhotoRules.Follow(["A", "A"], ["A", "X"]).ShouldBe([0, 1]);
        StepPhotoRules.Follow(["X", "A"], ["A"]).ShouldBe([null, 0]);
    }
}
