using RecipeWarden.Core.Import;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Import;

namespace RecipeWarden.Importers.Tests;

/// <summary>FR-IMP-01, FR-IMP-03, AC-10, AC-12: reading recipes from saved pages (JSON-LD, microdata, headings).</summary>
[Trait("Req", "AC-10")]
[Trait("Req", "FR-IMP-01")]
public sealed class AC_10_HtmlImportTests
{
    private static readonly Uri Page = new("https://www.example.com/recipes/lemon-lentil-soup?utm_source=x");

    private static ImportedRecipe Read(string fixture, Uri? url = null)
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", fixture));
        var result = ImportReader.ReadPage(html, url ?? Page);
        result.Error.ShouldBeNull(fixture);
        return result.Recipes.ShouldHaveSingleItem();
    }

    [Fact]
    public void Json_ld_in_a_graph_gives_every_field()
    {
        var r = Read("jsonld-graph.html");

        r.Method.ShouldBe(ExtractionMethod.JsonLd);
        r.Title.ShouldBe("Lemon Lentil Soup");
        r.Description.ShouldBe("A bright, & quick soup.");
        r.Servings.ShouldBe("6");
        r.PrepTime.ShouldBe("15");
        r.CookTime.ShouldBe("40");
        r.TotalTime.ShouldBe("55");
        r.Ingredients.ShouldBe("1 cup red lentils\n1 yellow onion, diced\n6 cups vegetable broth\n1 lemon, juiced");
        StepTextRules.Split(r.Steps).ShouldBe(
        [
            new StepText("Soup", "Soften the onion in oil."),
            new StepText("Soup", "Add the lentils and broth and simmer 30 minutes.", "Simmer"),
            new StepText("To finish", "Stir in the lemon juice."),
        ]);
        r.ImageUrl.ShouldBe("https://www.example.com/images/lentil-soup.jpg");
        r.Tags.ShouldBe(["Soup", "Mediterranean"]);
        r.SourceAuthor.ShouldBe("Jane Doe");
    }

    [Fact]
    [Trait("Req", "AC-12")]
    [Trait("Req", "BR-10")]
    public void The_page_becomes_the_web_source()
    {
        var r = Read("jsonld-graph.html");
        r.SourceType.ShouldBe(SourceType.Web);
        r.SourceUrl.ShouldBe(Page.AbsoluteUri);
        r.SourceName.ShouldBe("Example Kitchen");
        SourceRules.Validate(SourceType.Web, r.SourceUrl, r.SourceName, r.SourceAuthor).ShouldBeEmpty();
    }

    [Fact]
    public void Json_ld_arrays_strings_and_html_instructions()
    {
        var r = Read("jsonld-array-strings.html");
        r.Title.ShouldBe("Buttered Toast");
        r.Servings.ShouldBe("2");
        r.PrepTime.ShouldBe("5");
        r.CookTime.ShouldBe("3");
        r.SourceName.ShouldBe("Toast Co");
        r.SourceAuthor.ShouldBe("Sam Cook");
        r.ImageUrl.ShouldBe("https://cdn.example.com/toast.jpg");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(["Toast the bread.", "Spread with butter."]);
    }

    [Fact]
    public void Broken_json_ld_falls_back_to_microdata()
    {
        var r = Read("jsonld-broken-then-microdata.html");
        r.Method.ShouldBe(ExtractionMethod.Microdata);
        r.Title.ShouldBe("Fluffy Pancakes");
        r.Description.ShouldBe("Weekend pancakes.");
        r.PrepTime.ShouldBe("10");
        r.CookTime.ShouldBe("20");
        r.Servings.ShouldBeNull();
        r.Notes.ShouldBe("Makes 8 pancakes");
        r.Ingredients.ShouldBe("1 1/2 cups all-purpose flour\n2 eggs\n1 1/4 cups milk");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(["Whisk everything.", "Cook on a hot griddle."]);
        r.ImageUrl.ShouldBe("https://www.example.com/recipes/pancakes.jpg");
        r.SourceAuthor.ShouldBe("Pat Baker");
        r.SourceName.ShouldBe("example.com");
    }

    [Fact]
    public void Pages_without_structured_data_use_headings()
    {
        var r = Read("heuristic.html");
        r.Method.ShouldBe(ExtractionMethod.Heuristic);
        r.Title.ShouldBe("Grandma's Biscuits");
        r.Servings.ShouldBe("8");
        r.Ingredients.ShouldBe("2 cups all-purpose flour\n1 Tbsp baking powder\n1/2 cup cold butter\n3/4 cup buttermilk");
        StepTextRules.Split(r.Steps).Select(s => s.Text).ShouldBe(
            ["Heat the oven to 450°F.", "Cut the butter into the flour.", "Stir in the buttermilk and bake 12 minutes."]);
        r.Notes.ShouldBe("Freeze unbaked biscuits.");
        r.Description.ShouldBe("These are the best biscuits.");
        r.ImageUrl.ShouldBe("https://www.example.com/wp-content/biscuits.jpg");
    }

    [Fact]
    public void A_page_without_a_recipe_says_so()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "no-recipe.html"));
        ImportReader.ReadPage(html, Page).Error.ShouldBe(ImportReader.NoRecipe);
    }

    [Fact]
    [Trait("Req", "BR-13")]
    [Trait("Req", "AC-76")]
    public void Markup_in_fields_becomes_inert_text()
    {
        var r = Read("script-in-fields.html");
        r.Title.ShouldBe("Soup");
        r.Description.ShouldBe("<img src=x onerror=alert(1)>");
        r.Ingredients.ShouldBe("1 cup water\nsalt");
        StepTextRules.Split(r.Steps).ShouldHaveSingleItem().Text.ShouldBe("Boil it.");
        r.ImageUrl.ShouldBeNull();
    }

    [Fact]
    public void Pasted_html_and_pasted_text_are_read_too()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "jsonld-graph.html"));
        ImportReader.ReadPaste(html, Page).Recipes.ShouldHaveSingleItem().Title.ShouldBe("Lemon Lentil Soup");

        var text = "Pasted Soup\n\nIngredients\n1 cup water\nSalt\n\nDirections\nBoil the water.";
        var pasted = ImportReader.ReadPaste(text, Page).Recipes.ShouldHaveSingleItem();
        pasted.Title.ShouldBe("Pasted Soup");
        pasted.SourceType.ShouldBe(SourceType.Web);
        pasted.SourceName.ShouldBe("example.com");
        ImportReader.ReadPaste(text, null).Recipes.ShouldHaveSingleItem().SourceUrl.ShouldBeNull();
        ImportReader.ReadPaste("hello", null).Error.ShouldBe(ImportReader.NoRecipe);
        ImportReader.ReadPaste("<div>hello</div>", null).Error.ShouldBe(ImportReader.NoRecipe);
    }

    [Fact]
    [Trait("Req", "FR-IMP-06")]
    public void Warnings_flag_lines_to_check()
    {
        var r = new ImportedRecipe { Title = "x", Ingredients = "1 cup flour\n1 cup flour (sifted", Steps = "Mix." };
        ImportReader.Warnings(r).ShouldContain(w => w.Line == "1 cup flour (sifted");
        ImportReader.IngredientNames("1 cup flour\n2 eggs").ShouldBe(["flour", "eggs"]);
    }
}
