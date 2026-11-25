using RecipeWarden.Core.AiParser;
using RecipeWarden.Core.Import;

namespace RecipeWarden.Core.Tests;

/// <summary>
/// FR-IMP-09, BR-136, AC-143 (owner testing 2026-11-25): the AI parser is asked for the stated yield and for one
/// recipe-md document per recipe or version; "Re-parse with AI" keeps the version that matches the draft.
/// </summary>
[Trait("Req", "FR-IMP-09")]
public sealed class BR_136_AiParserVersionsTests
{
    [Fact]
    [Trait("Req", "AC-143")]
    [Trait("Req", "BR-136")]
    public void The_instructions_ask_for_the_stated_yield_and_one_document_per_version()
    {
        var text = AiParserRules.ParseInstructions;
        text.ShouldContain("\"yield:\"");
        text.ShouldContain("only when the text states it");
        text.ShouldContain("one recipe-md document for each recipe or version");
        text.ShouldContain("not a separate recipe");
        text.ShouldContain("do not invent ingredients, steps or amounts");
    }

    [Fact]
    [Trait("Req", "AC-143")]
    [Trait("Req", "BR-136")]
    public void Reparse_keeps_the_version_whose_title_matches_the_draft()
    {
        ImportedRecipe R(string title) => new() { Title = title };
        IReadOnlyList<ImportedRecipe> versions = [R("Wing Sauce (Oil Based)"), R("Wing Sauce (Butter Based)")];

        AiParserRules.PickForDraft(versions, "Wing Sauce (Butter Based)")!.Title.ShouldBe("Wing Sauce (Butter Based)");
        AiParserRules.PickForDraft(versions, "wing sauce  (butter based)")!.Title.ShouldBe("Wing Sauce (Butter Based)");
        AiParserRules.PickForDraft(versions, "Buffalo Wing Sauce (Homemade, Butter Based Sauce)")!.Title.ShouldBe("Wing Sauce (Butter Based)");
        AiParserRules.PickForDraft(versions, "Something else")!.Title.ShouldBe("Wing Sauce (Oil Based)");
        AiParserRules.PickForDraft(versions, null)!.Title.ShouldBe("Wing Sauce (Oil Based)");
        AiParserRules.PickForDraft([], "x").ShouldBeNull();
    }
}
