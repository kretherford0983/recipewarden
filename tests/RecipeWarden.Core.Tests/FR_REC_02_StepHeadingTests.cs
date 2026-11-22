using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>ADR 0015, FR-REC-02: numbered items with continuation lines stay one step; bold lead lines become headings.</summary>
[Trait("Req", "FR-REC-02")]
public sealed class FR_REC_02_StepHeadingTests
{
    [Fact]
    public void The_owner_paste_example_is_one_step_with_a_heading_and_no_asterisks()
    {
        var steps = StepTextRules.Split("1. **Preheat oven**\n   Preheat oven to **375°F**.\n2. **Mix**\n   Mix the flour and sugar.");

        steps.ShouldBe(
        [
            new StepText(null, "Preheat oven to 375°F.", "Preheat oven"),
            new StepText(null, "Mix the flour and sugar.", "Mix"),
        ]);
    }

    [Fact]
    public void Continuation_lines_need_not_be_indented_when_no_blank_line_separates_them()
    {
        var steps = StepTextRules.Split("1. Brown the beef.\nDrain the fat.\n2. Add the beans.");
        steps.Select(s => s.Text).ShouldBe(["Brown the beef. Drain the fat.", "Add the beans."]);
    }

    [Fact]
    public void Blank_lines_between_numbered_items_and_their_indented_text_are_allowed()
    {
        var steps = StepTextRules.Split("1. **Sear**\n\n   Brown the meat on all sides.\n\n2. **Braise**\n\n   Cover and cook 2 hours.");
        steps.Select(s => (s.Title, s.Text)).ShouldBe([("Sear", "Brown the meat on all sides."), ("Braise", "Cover and cook 2 hours.")]);
    }

    [Fact]
    public void A_bold_lead_in_ending_in_a_full_stop_or_colon_is_a_heading()
    {
        StepTextRules.Split("1. **Sear.** Brown the meat.").Single().ShouldBe(new StepText(null, "Brown the meat.", "Sear"));
        StepTextRules.Split("1. **Rest**: Let it stand 10 minutes.").Single().ShouldBe(new StepText(null, "Let it stand 10 minutes.", "Rest"));
    }

    [Fact]
    public void Bold_words_inside_a_sentence_are_not_a_heading()
    {
        StepTextRules.Split("1. **Do not** overmix the batter.").Single().ShouldBe(new StepText(null, "Do not overmix the batter."));
    }

    [Fact]
    public void A_bold_line_on_its_own_is_just_the_step_text()
    {
        StepTextRules.Split("1. **Serve warm.**\n2. Enjoy.").Select(s => (s.Title, s.Text)).ShouldBe([(null, "Serve warm."), (null, "Enjoy.")]);
    }

    [Fact]
    public void Unnumbered_text_keeps_one_step_per_line()
    {
        StepTextRules.Split("Preheat oven.\n  Mix.\nBake.").Select(s => s.Text).ShouldBe(["Preheat oven.", "Mix.", "Bake."]);
    }

    [Fact]
    public void Text_after_a_blank_line_that_is_not_indented_starts_a_new_step()
    {
        StepTextRules.Split("1. Bake 20 minutes.\n\nServe warm.").Select(s => s.Text).ShouldBe(["Bake 20 minutes.", "Serve warm."]);
    }

    [Fact]
    public void Sections_still_work_in_numbered_lists()
    {
        var steps = StepTextRules.Split("For the sauce:\n1. **Simmer**\n   Simmer 10 minutes.\nTo serve:\n2. Plate.");
        steps.ShouldBe([new StepText("For the sauce", "Simmer 10 minutes.", "Simmer"), new StepText("To serve", "Plate.")]);
    }

    [Fact]
    public void An_indented_line_ending_in_a_colon_continues_the_step()
    {
        StepTextRules.Split("1. Whisk together:\n   the eggs and milk.").Single().Text.ShouldBe("Whisk together: the eggs and milk.");
    }

    [Fact]
    public void Steps_with_headings_join_as_a_numbered_list_and_split_back_the_same()
    {
        var steps = new[]
        {
            new StepText(null, "Preheat oven to 375°F.", "Preheat oven"),
            new StepText(null, "Mix."),
            new StepText("Sauce", "Simmer 10 minutes.", "Simmer"),
        };

        var text = StepTextRules.Join(steps);

        text.ShouldBe("1. **Preheat oven**\n   Preheat oven to 375°F.\n2. Mix.\nSauce:\n3. **Simmer**\n   Simmer 10 minutes.");
        StepTextRules.Split(text).ShouldBe(steps);
    }

    [Fact]
    public void Long_headings_are_refused()
    {
        StepTextRules.Validate([new StepText(null, "x", new string('h', StepTextRules.MaxTitleLength))]).ShouldBeNull();
        StepTextRules.Validate([new StepText(null, "x", new string('h', StepTextRules.MaxTitleLength + 1))])
            .ShouldBe("A step heading is limited to 120 characters");
    }
}
