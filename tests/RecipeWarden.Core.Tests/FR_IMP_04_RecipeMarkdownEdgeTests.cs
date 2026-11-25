using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-IMP-04: recipe-md written by hand or by an AI assistant, with the usual variations.</summary>
[Trait("Req", "FR-IMP-04")]
public sealed class FR_IMP_04_RecipeMarkdownEdgeTests
{
    private const string Head = "---\nformat: recipe-md/1\n";

    [Fact]
    public void Directions_or_method_headings_and_list_styles_are_read()
    {
        var r = RecipeMarkdown.Read(Head + "title: \"Front Title\"\nsource:\n  type: something\n---\n\n# Body Title\n\n## Ingredients\n\n* 1 egg\n+ salt\n1) 2 cups water\n![photo](x.jpg)\n\n## Directions\n\n### Prep\n\n- Crack the egg.\n  Whisk it well.\n\n3) Boil the water.\n![step](y.jpg)\n").ShouldHaveSingleItem();
        r.Title.ShouldBe("Front Title");
        r.SourceType.ShouldBeNull();
        r.Ingredients.ShouldBe("1 egg\nsalt\n2 cups water");
        StepTextRules.Split(r.Steps).ShouldBe(
        [
            new StepText("Prep", "Crack the egg. Whisk it well."),
            new StepText("Prep", "Boil the water."),
        ]);
    }

    [Fact]
    public void Method_section_heading_only_steps_and_text_before_any_item()
    {
        var r = RecipeMarkdown.Read(Head + "---\n# T\n## Method\nStir first.\n1. **Rest**\n2. Serve.\n").ShouldHaveSingleItem();
        StepTextRules.Split(r.Steps).Select(s => (s.Title, s.Text)).ShouldBe([((string?)null, "Stir first."), (null, "Rest"), (null, "Serve.")]);
        r.Ingredients.ShouldBeNull();
        r.Description.ShouldBeNull();
    }

    [Theory]
    [InlineData("manual", SourceType.Manual)]
    [InlineData("file", SourceType.File)]
    [InlineData("WEB", SourceType.Web)]
    public void Source_types_are_read(string type, SourceType expected)
    {
        RecipeMarkdown.Read(Head + $"source:\n  type: {type}\n---\n# X\n").ShouldHaveSingleItem().SourceType.ShouldBe(expected);
    }

    [Fact]
    public void Broken_front_matter_and_empty_sections_are_skipped()
    {
        RecipeMarkdown.Read(Head + "title: never closed\n# X").ShouldBeEmpty();
        var r = RecipeMarkdown.Read(Head + "---\n# Only Title\n## Ingredients\n\n## Steps\n\n## Notes\n\n").ShouldHaveSingleItem();
        r.Ingredients.ShouldBeNull();
        r.Steps.ShouldBeNull();
        r.Notes.ShouldBeNull();
        r.HasContent.ShouldBeFalse();
        RecipeMarkdown.IsRecipeMarkdown("\n\n" + Head + "---\n").ShouldBeTrue();
        RecipeMarkdown.IsRecipeMarkdown("   ").ShouldBeFalse();
    }

    [Fact]
    public void The_writer_skips_empty_parts_and_writes_the_source_line()
    {
        var md = RecipeMarkdown.Write(new RecipeEdit { Title = "Bare", SourceType = SourceType.Web, SourceUrl = "https://example.com/bare" });
        md.ShouldContain("## Source\n\n[https://example.com/bare](https://example.com/bare)\n");
        md.ShouldNotContain("## Notes");
        md.ShouldNotContain("servings:");
        md.ShouldNotContain("tags:");

        var named = RecipeMarkdown.Write(new RecipeEdit { Title = "N", SourceName = "Grandma", SourceType = SourceType.Personal });
        named.ShouldContain("## Source\n\nGrandma\n");
        Should.Throw<ArgumentNullException>(() => RecipeMarkdown.Write(null!));
        Should.Throw<ArgumentNullException>(() => RecipeMarkdown.Read(null!));
    }

    [Fact]
    public void Front_matter_ignores_list_items_without_a_key_and_odd_lines()
    {
        var map = FrontMatter.Parse("- orphan\n: no key\nkey:value-without-space\n\"quoted key\": v\nlist: []\nempty_list:\nnext: 1");
        map.ContainsKey("orphan").ShouldBeFalse();
        map.Text("quoted key").ShouldBe("v");
        map.List("list").ShouldBeEmpty();
        map.Child("empty_list").ShouldNotBeNull();
        map.Text("next").ShouldBe("1");
        FrontMatter.Scalar("'single'").ShouldBe("single");
        FrontMatter.Scalar("null").ShouldBe(string.Empty);
        FrontMatter.Scalar("\"a\\tb\"").ShouldBe("a\tb");
        Should.Throw<ArgumentNullException>(() => FrontMatter.Parse(null!));
        Should.Throw<ArgumentNullException>(() => FrontMatter.Quote(null!));
    }
}
