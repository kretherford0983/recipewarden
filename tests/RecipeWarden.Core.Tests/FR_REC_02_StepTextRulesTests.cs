using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-REC-02 (W2 step 4): pasted multi-line steps split into steps; numbering removed; section headings kept.</summary>
[Trait("Req", "FR-REC-02")]
public sealed class FR_REC_02_StepTextRulesTests
{
    [Fact]
    public void Each_non_blank_line_is_a_step_and_numbering_is_removed()
    {
        var steps = StepTextRules.Split("1. Preheat the oven.\n\n2) Mix the flour.\r\nStep 3: Bake 20 minutes.\n- Cool.\n");

        steps.Select(s => s.Text).ShouldBe(["Preheat the oven.", "Mix the flour.", "Bake 20 minutes.", "Cool."]);
        steps.ShouldAllBe(s => s.SectionName == null);
    }

    [Fact]
    public void A_short_line_ending_in_a_colon_names_the_next_steps()
    {
        var steps = StepTextRules.Split("For the sauce:\nSimmer.\nStir.\nTo serve:\nPlate.");

        steps.Select(s => (s.SectionName, s.Text)).ShouldBe(
        [
            ("For the sauce", "Simmer."),
            ("For the sauce", "Stir."),
            ("To serve", "Plate."),
        ]);
    }

    [Fact]
    public void Numbers_that_start_the_sentence_stay()
    {
        StepTextRules.Split("350 degrees is hot enough.").Single().Text.ShouldBe("350 degrees is hot enough.");
        StepTextRules.Split("2 eggs go in first.").Single().Text.ShouldBe("2 eggs go in first.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n \n")]
    [InlineData("1.\n2)")]
    public void Nothing_to_split_gives_no_steps(string? text)
    {
        StepTextRules.Split(text).ShouldBeEmpty();
    }

    [Fact]
    public void Steps_are_joined_back_with_their_headings()
    {
        var text = "For the sauce:\nSimmer.\nStir.\nPlate.";
        StepTextRules.Join(StepTextRules.Split(text)).ShouldBe("For the sauce:\nSimmer.\nStir.\nPlate.");
    }

    [Fact]
    public void Heading_change_back_to_none_is_not_representable_so_join_keeps_sections_in_order()
    {
        var steps = new[] { new StepText(null, "A."), new StepText("Sauce", "B."), new StepText("Serve", "C.") };
        StepTextRules.Join(steps).ShouldBe("A.\nSauce:\nB.\nServe:\nC.");
    }

    [Fact]
    public void Long_steps_are_limited()
    {
        StepTextRules.Validate([new StepText(null, new string('a', StepTextRules.MaxStepLength))]).ShouldBeNull();
        StepTextRules.Validate([new StepText(null, new string('a', StepTextRules.MaxStepLength + 1))])
            .ShouldBe("Each step is limited to 4000 characters");
        StepTextRules.Validate(Enumerable.Repeat(new StepText(null, "x"), StepTextRules.MaxSteps + 1).ToArray())
            .ShouldBe("A recipe can have at most 200 steps");
    }
}
