using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-IMP-04, AC-14, ADR 0025: recipe-md v1 written then read back gives the same fields.</summary>
[Trait("Req", "AC-14")]
[Trait("Req", "FR-IMP-04")]
public sealed class AC_14_RecipeMarkdownTests
{
    public static TheoryData<string> Fixtures => [.. RoundTripSet.Keys];

    private static readonly Dictionary<string, RecipeEdit> RoundTripSet = new()
    {
        ["web soup"] = new RecipeEdit
        {
            Title = "Chicken Tortilla Soup",
            Description = "A brothy soup.\n\nGreat with lime.",
            Servings = 6,
            PrepMinutes = 15,
            CookMinutes = 40,
            SourceType = SourceType.Web,
            SourceName = "Example Kitchen",
            SourceAuthor = "Jane Doe",
            SourceUrl = "https://example.com/chicken-tortilla-soup",
            Tags = ["dinner", "soup", "cold weather"],
            IngredientsText = "For the soup:\n1 1/2 lb boneless chicken breast\n2 cups diced yellow onion\n1 pinch cayenne (optional)\nTo serve:\n1 lime, cut in wedges",
            StepsText = "Brown the chicken.\nAdd the onion and simmer 30 minutes.",
            Notes = "Freezes well.",
        },
        ["ranges and headings"] = new RecipeEdit
        {
            Title = "Brownies: \"the best\" # ever",
            Servings = 6,
            ServingsMax = 8,
            PrepMinutes = 20,
            PrepMinutesMax = 25,
            CookMinutes = 75,
            TotalMinutes = 95,
            TotalMinutesMax = 100,
            IsPrecision = true,
            PanText = "9x13",
            YieldText = "24 fl oz",
            FinishedDensityGPerMl = 1.1m,
            SourceType = SourceType.Personal,
            IngredientsText = "1 cup flour\n2 eggs",
            StepsText = "1. **Preheat oven**\n   Heat to 350°F.\n2. Mix everything.\nBaking:\n3. **Bake**\n   Bake 25 minutes.",
            Notes = "# not a heading\n---\nkeep these lines",
        },
        ["plain manual"] = new RecipeEdit
        {
            Title = "Toast",
            Servings = 2.5m,
            SourceType = SourceType.Manual,
            IngredientsText = "2 slices bread",
            StepsText = "Toast the bread.",
        },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Written_then_read_gives_the_same_fields(string name)
    {
        var edit = RoundTripSet[name];
        var md = RecipeMarkdown.Write(edit, slug: "x", imported: new DateOnly(2026, 11, 17));

        RecipeMarkdown.IsRecipeMarkdown(md).ShouldBeTrue();
        var read = RecipeMarkdown.Read(md).ShouldHaveSingleItem();

        read.Method.ShouldBe(ExtractionMethod.RecipeMarkdown);
        read.Title.ShouldBe(edit.Title);
        read.Description.ShouldBe(edit.Description);
        Servings(read.Servings).ShouldBe((edit.Servings, edit.ServingsMax));
        Time(read.PrepTime).ShouldBe((edit.PrepMinutes, edit.PrepMinutesMax));
        Time(read.CookTime).ShouldBe((edit.CookMinutes, edit.CookMinutesMax));
        Time(read.TotalTime).ShouldBe((edit.TotalMinutes, edit.TotalMinutesMax));
        read.IsPrecision.ShouldBe(edit.IsPrecision);
        read.Pan.ShouldBe(edit.PanText);
        read.Yield.ShouldBe(edit.YieldText);
        (read.FinishedDensity is null ? (decimal?)null : decimal.Parse(read.FinishedDensity, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(edit.FinishedDensityGPerMl);
        read.Tags.ShouldBe(edit.Tags);
        read.SourceType.ShouldBe(edit.SourceType);
        read.SourceName.ShouldBe(edit.SourceName);
        read.SourceAuthor.ShouldBe(edit.SourceAuthor);
        read.SourceUrl.ShouldBe(edit.SourceUrl);
        read.Ingredients.ShouldBe(edit.IngredientsText);
        StepTextRules.Split(read.Steps).ShouldBe(StepTextRules.Split(edit.StepsText));
        read.Notes.ShouldBe(edit.Notes);
    }

    [Fact]
    public void The_spec_example_reads()
    {
        const string md = """
            ---
            format: recipe-md/1
            title: Chicken Tortilla Soup
            slug: chicken-tortilla-soup
            servings: 6
            yield: about 10 cups
            prep_minutes: 15
            cook_minutes: 40
            tags: [dinner, soup, cold weather]   # comment
            allergens: [milk]
            image: images/finish.jpg
            source:
              type: web
              name: Example Kitchen
              author: Jane Doe
              url: https://example.com/chicken-tortilla-soup
              imported: 2026-11-17
            nutrition:
              source: calculated
              completeness_pct: 92
              per_serving: { calories: 410, fat_g: 14, protein_g: 32 }
            scale: 1
            ---

            # Chicken Tortilla Soup

            One-paragraph description.

            ## Ingredients

            ### For the soup
            - 1 1/2 lb boneless chicken breast
            - 2 cups diced yellow onion
            - 1 pinch cayenne (optional)

            ## Steps

            1. Brown the chicken...
               ![Step 1](images/step-01.jpg)
            2. Add the onion...

            ## Notes

            Free text.

            ## Changes

            - Less garlic, because it was strong.

            ## Source

            [Example Kitchen](https://example.com/chicken-tortilla-soup) by Jane Doe
            """;

        var r = RecipeMarkdown.Read(md).ShouldHaveSingleItem();
        r.Title.ShouldBe("Chicken Tortilla Soup");
        r.Description.ShouldBe("One-paragraph description.");
        r.Servings.ShouldBe("6");
        r.Yield.ShouldBe("about 10 cups");
        r.PrepTime.ShouldBe("15");
        r.Tags.ShouldBe(["dinner", "soup", "cold weather"]);
        r.SourceType.ShouldBe(SourceType.Web);
        r.SourceUrl.ShouldBe("https://example.com/chicken-tortilla-soup");
        r.Ingredients.ShouldBe("For the soup:\n1 1/2 lb boneless chicken breast\n2 cups diced yellow onion\n1 pinch cayenne (optional)");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(["Brown the chicken...", "Add the onion..."]);
        r.Notes.ShouldBe("Free text.\n\nChanges:\n- Less garlic, because it was strong.");
    }

    [Fact]
    public void Several_recipes_in_one_file_are_read_separately()
    {
        var a = RecipeMarkdown.Write(RoundTripSet["plain manual"]);
        var b = RecipeMarkdown.Write(RoundTripSet["web soup"]);
        RecipeMarkdown.Read(a + "\n" + b).Select(r => r.Title).ShouldBe(["Toast", "Chicken Tortilla Soup"]);
    }

    [Theory]
    [InlineData("# Just markdown\n\n---\n\nText")]
    [InlineData("---\ntitle: x\n---\n# x")]
    [InlineData("---\nformat: recipe-md/2\n---\n")]
    [InlineData("")]
    public void Other_markdown_is_not_recipe_md(string text)
    {
        RecipeMarkdown.IsRecipeMarkdown(text).ShouldBeFalse();
    }

    [Fact]
    public void Front_matter_handles_quotes_lists_and_maps()
    {
        var map = FrontMatter.Parse("""
            title: 'Mom''s pie'
            quoted: "a \"b\" \\ c\nd"
            empty: ~
            tags:
              - one
              - "two, three"
            inline: [a, "b, c", 'd']
            source:
              name: X # trailing comment
            map: { a: 1, b: [x, y] }
            not a key line
            """);
        map.Text("title").ShouldBe("Mom's pie");
        map.Text("quoted").ShouldBe("a \"b\" \\ c\nd");
        map.Text("empty").ShouldBe(string.Empty);
        map.List("tags").ShouldBe(["one", "two, three"]);
        map.List("inline").ShouldBe(["a", "b, c", "d"]);
        map.Child("source")!.Text("name").ShouldBe("X");
        map.Child("map")!.List("b").ShouldBe(["x", "y"]);
        map.List("title").ShouldBe(["Mom's pie"]);
        map.List("missing").ShouldBeEmpty();
        FrontMatter.Parse(new string('a', FrontMatter.MaxLength + 1)).ShouldBeEmpty();
        FrontMatter.Scalar(FrontMatter.Quote("tab\there \"q\" \\")).ShouldBe("tab\there \"q\" \\");
    }

    private static (decimal?, decimal?) Servings(string? text)
    {
        if (text is null)
        {
            return (null, null);
        }

        var s = ServingsRules.ParseText(text);
        s.Error.ShouldBeNull();
        return (s.Min, s.Max);
    }

    private static (int?, int?) Time(string? text)
    {
        if (text is null)
        {
            return (null, null);
        }

        var t = TimeText.Parse(text);
        t.Error.ShouldBeNull();
        return (t.Min, t.Max);
    }
}
