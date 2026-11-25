using RecipeWarden.Core.Import;
using RecipeWarden.Core.Python;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Tests;

/// <summary>FR-IMP-12, BR-117, ADR 0059: when the site reader is asked, and how its reading becomes an import.</summary>
[Trait("Req", "FR-IMP-12")]
[Trait("Req", "BR-117")]
public sealed class FR_IMP_12_SiteReaderRulesTests
{
    private static readonly Uri Page = new("https://www.allrecipes.com/recipe/123/lentil-soup/");

    private static ImportedRecipe Ours(ExtractionMethod method, bool complete = true) => new()
    {
        Title = "Lentil Soup",
        Ingredients = "1 cup lentils",
        Steps = complete ? "Simmer." : null,
        Method = method,
        SourceName = "Allrecipes",
    };

    private static SiteRecipeReading Site(bool supported = true, bool found = true, string? title = "Lentil Soup", IReadOnlyList<string>? steps = null) =>
        new(supported, found, title, ["1 cup lentils", "  4 cups stock ", ""], steps ?? ["Rinse the lentils.", "Simmer 30 minutes."],
            "6 servings", 45, 10, 35, "Pat Cook", "A weeknight soup.", "https://images.example/soup.jpg", "Allrecipes");

    [Fact]
    [Trait("Req", "AC-111")]
    public void Complete_structured_data_is_never_replaced()
    {
        SiteReaderRules.ShouldTry(Ours(ExtractionMethod.JsonLd)).ShouldBeFalse();
        SiteReaderRules.ShouldTry(Ours(ExtractionMethod.Microdata)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "AC-110")]
    public void Missing_incomplete_or_heuristic_readings_ask_the_site_reader()
    {
        SiteReaderRules.ShouldTry(null).ShouldBeTrue();
        SiteReaderRules.ShouldTry(Ours(ExtractionMethod.JsonLd, complete: false)).ShouldBeTrue();
        SiteReaderRules.ShouldTry(Ours(ExtractionMethod.Heuristic)).ShouldBeTrue();
        SiteReaderRules.ShouldTry(Ours(ExtractionMethod.Headings)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Req", "AC-110")]
    [Trait("Req", "BR-10")]
    public void A_complete_reading_on_a_supported_host_becomes_a_credited_web_import()
    {
        var r = SiteReaderRules.ToImported(Site(), Page, Ours(ExtractionMethod.Heuristic)).ShouldNotBeNull();
        r.Method.ShouldBe(ExtractionMethod.SiteReader);
        r.Title.ShouldBe("Lentil Soup");
        r.Ingredients.ShouldBe("1 cup lentils\n4 cups stock");
        r.Steps.ShouldBe(StepTextRules.Join([new StepText(null, "Rinse the lentils."), new StepText(null, "Simmer 30 minutes.")]));
        r.Servings.ShouldBe("6");
        r.TotalTime.ShouldBe("45");
        r.PrepTime.ShouldBe("10");
        r.CookTime.ShouldBe("35");
        r.Description.ShouldBe("A weeknight soup.");
        r.ImageUrl.ShouldBe("https://images.example/soup.jpg");
        r.SourceType.ShouldBe(SourceType.Web);
        r.SourceUrl.ShouldBe(Page.AbsoluteUri);
        r.SourceName.ShouldBe("Allrecipes");
        r.SourceAuthor.ShouldBe("Pat Cook");
    }

    [Fact]
    public void Unsupported_not_found_or_incomplete_readings_are_not_used()
    {
        SiteReaderRules.ToImported(Site(supported: false), Page, null).ShouldBeNull();
        SiteReaderRules.ToImported(Site(found: false), Page, null).ShouldBeNull();
        SiteReaderRules.ToImported(Site(title: " "), Page, null).ShouldBeNull();
        SiteReaderRules.ToImported(Site(steps: []), Page, null).ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "SEC-22")]
    public void The_reading_is_plain_bounded_text_and_only_web_image_addresses_are_kept()
    {
        var long_ = new string('x', SiteReaderRules.MaxLineLength + 50);
        var many = Enumerable.Range(1, SiteReaderRules.MaxLines + 20).Select(i => $"{i} g thing\u0007").ToList();
        var site = Site() with { Ingredients = many, Instructions = [long_], ImageUrl = "javascript:alert(1)", Yields = "Makes about 2 dozen", SiteName = null };

        var r = SiteReaderRules.ToImported(site, Page, null).ShouldNotBeNull();

        r.Ingredients!.Split('\n').Length.ShouldBe(SiteReaderRules.MaxLines);
        r.Ingredients.ShouldNotContain("\u0007");
        r.Steps!.Length.ShouldBeLessThanOrEqualTo(SiteReaderRules.MaxLineLength + 10);
        r.ImageUrl.ShouldBeNull();
        r.Servings.ShouldBeNull("no leading number in the yield");
        r.SourceName.ShouldBe("allrecipes.com", "no site name: the host without www");
        SiteReaderRules.ToImported(Site() with { Yields = "4-6" }, Page, null)!.Servings.ShouldBe("4-6");
    }

    [Theory]
    [InlineData(ExtractionMethod.JsonLd, "Recipe data (JSON-LD)")]
    [InlineData(ExtractionMethod.Microdata, "Recipe data (microdata)")]
    [InlineData(ExtractionMethod.SiteReader, "Site reader (recipe-scrapers)")]
    [InlineData(ExtractionMethod.Headings, "Page headings")]
    public void The_review_page_names_the_method(ExtractionMethod method, string label) =>
        SiteReaderRules.MethodLabel(method).ShouldBe(label);
}
