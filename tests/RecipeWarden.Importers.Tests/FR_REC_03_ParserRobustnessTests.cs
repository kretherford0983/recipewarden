using System.Text;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-REC-03, BR-04: the parser never throws, always keeps the raw text, and scores within 0..1.</summary>
[Trait("Req", "FR-REC-03")]
[Trait("Req", "BR-04")]
public sealed class FR_REC_03_ParserRobustnessTests
{
    [Fact]
    public void Random_lines_never_throw_and_keep_raw_text()
    {
        var random = new Random(20261121);
        const string alphabet = "0123456789 /-.,()½¼⅓–abcdeflmnoprstuxyzCTg:+*•\t\"'%\u0000́‮𐀀🍲";
        for (var i = 0; i < 10_000; i++)
        {
            var sb = new StringBuilder();
            var length = random.Next(0, 80);
            for (var j = 0; j < length; j++)
            {
                sb.Append(alphabet[random.Next(alphabet.Length)]);
            }

            var input = sb.ToString();
            var p = IngredientLineParser.Parse(input);

            p.Confidence.ShouldBeInRange(0m, 1m);
            p.RawText.ShouldNotBeNull();
            HasOnlyPairs(p.RawText).ShouldBeTrue(); // lone surrogates replaced, so the text can be stored
            p.Name.ShouldNotBeNull();
            if (p.QuantityMax is not null)
            {
                p.Quantity.ShouldNotBeNull();
            }
        }
    }

    [Fact]
    public void Very_long_lines_are_parsed_from_their_start_and_kept_whole()
    {
        var line = "2 cups flour " + new string('x', 10_000);
        var p = IngredientLineParser.Parse(line);

        p.RawText.ShouldBe(line);
        p.Quantity.ShouldBe(2m);
        p.UnitCode.ShouldBe("cup");
        p.NeedsReview.ShouldBeTrue();
    }

    [Fact]
    public void Markup_in_a_line_stays_plain_text()
    {
        var p = IngredientLineParser.Parse("1 cup <script>alert(1)</script> sugar");
        p.Name.ShouldContain("<script>");
        p.NeedsReview.ShouldBeTrue(); // rendered encoded by Razor; flagged so a person looks at it
    }

    [Fact]
    public void Low_confidence_threshold_is_the_spec_value()
    {
        ParsedIngredient.ReviewThreshold.ShouldBe(0.8m);
    }

    private static bool HasOnlyPairs(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(s[i]))
            {
                return false;
            }
        }

        return true;
    }
}
