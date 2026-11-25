using System.Text.RegularExpressions;
using RecipeWarden.Importers.Ingredients;

namespace RecipeWarden.Importers.Tests;

/// <summary>
/// FR-REC-03, NFR-05: a regex that runs out of time (a busy host pauses the thread) never fails a save. The heading check
/// then treats the line as an ingredient, which keeps its text; it was the one place a timeout escaped (CI, 2026-11-25).
/// </summary>
[Trait("Req", "FR-REC-03")]
[Trait("Req", "NFR-05")]
public sealed class FR_REC_03_ParserTimeoutTests
{
    private static bool TimesOut(Regex regex, string text) => throw new RegexMatchTimeoutException(text, regex.ToString(), TimeSpan.FromSeconds(2));

    [Fact]
    public void A_heading_check_that_times_out_reads_the_line_as_an_ingredient()
    {
        IngredientLineParser.AsHeading("For the frosting:", TimesOut).ShouldBeNull();
        IngredientLineParser.AsHeading("For the frosting:", static (r, t) => r.IsMatch(t)).ShouldBe("For the frosting");
        IngredientLineParser.AsHeading("2 cups flour", static (r, t) => r.IsMatch(t)).ShouldBeNull();
    }

    [Fact]
    public void The_parser_allows_a_busy_host_two_seconds()
    {
        IngredientLineParser.MatchTimeout.ShouldBe(TimeSpan.FromSeconds(2));
        var list = IngredientLineParser.ParseList("For the crust:\n1 1/2 cups flour");
        list[0].GroupHeading.ShouldBe("For the crust");
        list[1].Ingredient!.RawText.ShouldBe("1 1/2 cups flour");
    }
}
