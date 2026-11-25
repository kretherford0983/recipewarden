using RecipeWarden.Core.AiExchange;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-EXP-05, FR-EXP-06, AC-25, AC-27, BR-92, BR-93, BR-95, ADR 0033: reading an AI reply.</summary>
[Trait("Req", "FR-EXP-05")]
public sealed class FR_EXP_05_AiReplyReaderTests
{
    private const string Block = """
        ---
        format: recipe-md/1
        title: "Stuffed Shells"
        slug: "stuffed-shells"
        servings: 6
        source:
          type: web
          name: "Evil Kitchen"
          url: "https://evil.example/x"
        ---

        # Stuffed Shells

        ## Ingredients

        - 2 cloves garlic
        - 30 oz ricotta

        ## Steps

        1. Mix the filling.
        2. Bake 25 minutes.

        ## Changes

        - Halved the garlic: it was too strong.
        """;

    [Fact]
    [Trait("Req", "AC-27")]
    public void A_block_inside_chat_text_and_a_code_fence_is_found_with_its_changes()
    {
        var reply = "Sure! Here is the revised recipe:\n\n```markdown\n" + Block + "\n```\n\nLet me know how it turns out!";
        var block = AiReplyReader.Read(reply).ShouldHaveSingleItem();
        block.IsValid.ShouldBeTrue(string.Join("; ", block.Problems));
        block.Recipe!.Slug.ShouldBe("stuffed-shells");
        block.Recipe.Changes.ShouldBe("- Halved the garlic: it was too strong.");
        block.Recipe.Notes!.ShouldContain("Changes:");
        block.Markdown.ShouldNotContain("Let me know");
        block.Markdown.ShouldNotContain("```");
    }

    [Fact]
    public void Several_bare_blocks_are_read_separately()
    {
        var second = Block.Replace("stuffed-shells", "baked-ziti", StringComparison.Ordinal).Replace("Stuffed Shells", "Baked Ziti", StringComparison.Ordinal);
        var blocks = AiReplyReader.Read("Two recipes:\n" + Block + "\nAnd the next:\n" + second);
        blocks.Count.ShouldBe(2);
        blocks.Select(b => b.Recipe!.Slug).ShouldBe(["stuffed-shells", "baked-ziti"]);
        blocks[1].Index.ShouldBe(1);
        AiReplyReader.Blocks("~~~\n" + Block + "\n~~~\n```\nno recipe here\n```").Count.ShouldBe(1);
        AiReplyReader.Blocks("```\n" + Block).Count.ShouldBe(1); // an unclosed fence runs to the end
    }

    [Fact]
    [Trait("Req", "AC-27")]
    public void A_reply_without_a_block_or_with_missing_sections_says_what_is_missing()
    {
        AiReplyReader.Read("I think you should add more salt.").ShouldBeEmpty();
        AiReplyReader.Read(null).ShouldBeEmpty();
        AiReplyReader.Read("   ").ShouldBeEmpty();

        var noSteps = Block[..Block.IndexOf("## Steps", StringComparison.Ordinal)];
        var block = AiReplyReader.Read(noSteps).ShouldHaveSingleItem();
        block.IsValid.ShouldBeFalse();
        block.Problems.ShouldContain("The \"## Steps\" section is missing or empty.");

        var empty = AiReplyReader.Read("---\nformat: recipe-md/1\n---\n").ShouldHaveSingleItem();
        empty.Problems.ShouldContain("The recipe has no title (\"title:\" or \"# Title\").");
        empty.Problems.ShouldContain("The \"## Ingredients\" section is missing or empty.");
    }

    [Theory]
    [Trait("Req", "FR-EXP-06")]
    [InlineData("format: recipe-md/2", "\"format\" must be recipe-md/1.")]
    [InlineData("slug: \"Not A Slug\"", "\"slug\" must be lower-case letters, digits and hyphens.")]
    [InlineData("servings: lots", "\"servings\" must be a number or a range such as 6-8.")]
    [InlineData("prep_minutes: soon", "\"prep_minutes\" must be whole minutes or a range such as 20-25.")]
    [InlineData("precision: maybe", "\"precision\" must be true or false.")]
    [InlineData("finished_density: heavy", "\"finished_density\" must be a number (g/ml).")]
    [InlineData("scale: 1000", "\"scale\" must be a number from 0.1 to 100.")]
    [InlineData("image: ../../etc/passwd", "\"image\" must be a relative path inside the export, e.g. images/finish.webp.")]
    [InlineData("image: \"https://x.example/a.jpg\"", "\"image\" must be a relative path inside the export, e.g. images/finish.webp.")]
    [InlineData("source: web", "\"source\" must be a block with type, name, author and url.")]
    [InlineData("source:\n  type: stolen", "\"source.type\" must be web, file, manual or personal.")]
    [InlineData("source:\n  url: \"javascript:alert(1)\"", "\"source.url\" must be an http or https address.")]
    [InlineData("title: \"\"", "\"title\" must be 1 to 200 characters.")]
    public void Front_matter_breaking_the_schema_is_reported(string line, string problem)
    {
        var text = "---\n" + (line.StartsWith("format", StringComparison.Ordinal) ? line : "format: recipe-md/1\n" + line) + "\n---\n# T\n";
        RecipeMarkdownValidator.Validate(text).ShouldContain(problem);
    }

    [Fact]
    [Trait("Req", "AC-25")]
    public void The_writers_output_passes_the_schema_rules_and_bad_documents_are_refused()
    {
        var edit = new RecipeEdit
        {
            Title = "Bread", IngredientsText = "3 cups flour", StepsText = "Bake.", Servings = 6m, ServingsMax = 8m, PrepMinutes = 20,
            CookMinutes = 30, CookMinutesMax = 35, IsPrecision = true, FinishedDensityGPerMl = 0.5m, Tags = ["baking"],
            SourceType = SourceType.Web, SourceName = "Ex", SourceUrl = "https://example.com/b", PanText = "9x5",
        };
        RecipeMarkdownValidator.Validate(RecipeMarkdown.Write(edit, "bread", new DateOnly(2026, 11, 1), 2m, "images/finish.webp")).ShouldBeEmpty();
        RecipeMarkdownValidator.Validate("# no front matter").ShouldHaveSingleItem().ShouldContain("must start with front matter");
        RecipeMarkdownValidator.Validate("---\nformat: recipe-md/1\n").ShouldHaveSingleItem().ShouldContain("no closing");
        RecipeMarkdownValidator.Validate("").ShouldHaveSingleItem();
    }

    [Fact]
    [Trait("Req", "BR-92")]
    public void Saving_keeps_the_originals_source_adds_the_adapted_note_and_marks_a_same_titled_variant()
    {
        var original = new RecipeEdit
        {
            Title = "Stuffed Shells", IngredientsText = "4 cloves garlic", StepsText = "Mix.", Servings = 4m,
            SourceType = SourceType.Web, SourceName = "Example Kitchen", SourceUrl = "https://example.com/shells", SourceAuthor = "Jane",
            Tags = ["pasta"], PrepMinutes = 10,
        };
        var reply = AiReplyReader.Read(Block).Single().Recipe!;
        var date = new DateOnly(2026, 11, 23);

        var variant = AiVariantRules.ToEdit(reply, original, date, asVariant: true, publish: false);
        variant.Title.ShouldBe("Stuffed Shells (AI variant)");
        variant.SourceName.ShouldBe("Example Kitchen");
        variant.SourceUrl.ShouldBe("https://example.com/shells");
        variant.SourceAuthor.ShouldBe("Jane");
        variant.Servings.ShouldBe(6m);
        variant.PrepMinutes.ShouldBe(10);
        variant.Tags.ShouldBe(["pasta"]);
        variant.IngredientsText.ShouldBe("2 cloves garlic\n30 oz ricotta");
        variant.Notes!.ShouldContain("Changes:\n- Halved the garlic");
        variant.Notes.ShouldEndWith("Adapted with AI assistance on 2026-11-23.");
        variant.WantsPublish.ShouldBeFalse();

        var replace = AiVariantRules.ToEdit(reply with { Title = null, Notes = null, Servings = "lots" }, original, date, asVariant: false, publish: true);
        replace.Title.ShouldBe("Stuffed Shells");
        replace.Servings.ShouldBe(4m);
        replace.Notes.ShouldBe("Adapted with AI assistance on 2026-11-23.");
        replace.WantsPublish.ShouldBeTrue();
    }

    [Fact]
    public void Line_diff_marks_removed_and_added_lines()
    {
        var diff = AiVariantRules.Lines(["4 cloves garlic", "15 oz ricotta", "1 egg"], ["2 cloves garlic", "15 oz ricotta", "1 egg", "salt"]);
        diff.Select(d => (d.Change, d.Text)).ShouldBe([
            (LineChange.Removed, "4 cloves garlic"), (LineChange.Added, "2 cloves garlic"), (LineChange.Same, "15 oz ricotta"),
            (LineChange.Same, "1 egg"), (LineChange.Added, "salt"),
        ]);
        AiVariantRules.Lines(["a", "b"], []).ShouldAllBe(d => d.Change == LineChange.Removed);
        AiVariantRules.SplitLines("a\r\n\n  \nb  ").ShouldBe(["a", "b"]);
        AiVariantRules.SplitLines(null).ShouldBeEmpty();
    }
}
