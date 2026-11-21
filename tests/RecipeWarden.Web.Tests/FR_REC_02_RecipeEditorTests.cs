using System.Net;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>FR-REC-02, W2, AC-01, AC-02, UX-08: the recipe editor end to end over HTTP.</summary>
[Trait("Req", "FR-REC-02")]
public sealed class FR_REC_02_RecipeEditorTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-01")]
    public async Task Complete_recipe_publishes_gets_a_slug_and_is_found_by_search_at_once()
    {
        using var editor = await factory.SignInAsync("ed-ac01@example.com");

        var response = await editor.CreateRecipeAsync("Saffron Paella", "2 cups bomba rice", "Toast the rice.");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/recipes/saffron-paella");

        using var reader = factory.CreateHttpsClient();
        var search = await reader.GetStringAsync("/search?q=saffron", Ct);
        search.ShouldContain("href=\"/recipes/saffron-paella\"");
    }

    [Fact]
    [Trait("Req", "AC-02")]
    [Trait("Req", "BR-01")]
    public async Task Missing_steps_saves_a_draft_and_the_editor_says_why()
    {
        using var editor = await factory.SignInAsync("ed-ac02@example.com");

        var response = await editor.CreateRecipeAsync("Unfinished Stew", "1 onion", steps: "");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.ShouldEndWith("/edit?saved=draft");

        var page = await editor.GetStringAsync(location, Ct);
        page.ShouldContain("Saved as a draft.");
        page.ShouldContain("Add at least one step to publish");

        using var reader = factory.CreateHttpsClient();
        (await reader.GetAsync("/recipes/unfinished-stew", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "UX-08")]
    public async Task Invalid_fields_are_summarized_and_nothing_is_saved()
    {
        using var editor = await factory.SignInAsync("ed-invalid@example.com");

        var response = await editor.CreateRecipeAsync(new string('t', 201), extra: [("Input.Servings", "abc")]);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.TextAsync();
        html.ShouldContain("Please fix the following");
        html.ShouldContain(RecipeWarden.Core.Recipes.ServingsRules.Unreadable); // ADR 0010: the hint shows what is accepted
        html.ShouldContain("href=\"#Input_Servings\"");

        var second = await editor.CreateRecipeAsync(new string('t', 201));
        (await second.TextAsync()).ShouldContain("Title must be 200 characters or fewer");
    }

    [Fact]
    public async Task Edit_form_shows_the_saved_text_and_saving_publishes_changes()
    {
        using var editor = await factory.SignInAsync("ed-edit@example.com");
        var slug = await editor.AddRecipeAsync("Weeknight Chili", "For the chili:\n1 lb beef\n1 can beans", "Brown.\nSimmer.", "dinner, spicy");
        var id = await factory.RecipeIdAsync(slug);

        var form = await editor.GetStringAsync($"/manage/recipes/{id}/edit", Ct);
        form.ShouldContain("For the chili:");
        form.ShouldContain("dinner, spicy");

        var response = await editor.PostFormAsync($"/manage/recipes/{id}/edit",
            ("Input.Title", "Weeknight Chili"), ("Input.Ingredients", "1 can beans\n1 lb beef"), ("Input.Steps", "Simmer.\nBrown."),
            ("Input.SourceType", "Manual"), ("Input.Version", "1"), ("Action", "publish"));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var page = await editor.GetStringAsync("/recipes/" + slug, Ct);
        page.IndexOf("1 can beans", StringComparison.Ordinal).ShouldBeLessThan(page.IndexOf("1 lb beef", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "FR-REC-03")]
    public async Task Live_parse_returns_chips_and_flags_doubtful_lines()
    {
        using var editor = await factory.SignInAsync("ed-parse@example.com");
        var token = await RecipeWardenFactory.GetAntiforgeryTokenAsync(editor, "/manage");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/manage/recipes/new?handler=Parse")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Ingredients"] = "Sauce:\n1 ½ cups all-purpose flour, sifted\n1 cup",
            }),
        };
        request.Headers.Add("HX-Request", "true");
        request.Headers.Add("RequestVerificationToken", token);

        var response = await editor.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.TextAsync();
        html.ShouldContain("chip-heading\">Sauce");
        html.ShouldContain("<span class=\"chip qty\">1½</span>");
        html.ShouldContain("<span class=\"chip name\">all-purpose flour</span>");
        html.ShouldContain("<span class=\"chip prep\">sifted</span>");
        html.ShouldContain("chip-line review");
        html.ShouldNotContain("<html"); // a partial, not a page
    }

    [Fact]
    public async Task Editor_pages_for_missing_recipes_are_404()
    {
        using var editor = await factory.SignInAsync("ed-404@example.com");
        (await editor.GetAsync("/manage/recipes/999999/edit", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await editor.PostFormAsync("/manage/recipes/999999/edit", ("Input.Title", "x"), ("Action", "publish")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Req", "BR-11")]
    public async Task Editor_cannot_take_a_web_recipe_off_web_but_admin_can()
    {
        using var editor = await factory.SignInAsync("ed-web@example.com");
        var response = await editor.CreateRecipeAsync("Borrowed Bread", extra:
            [("Input.SourceType", "Web"), ("Input.SourceName", "Example Kitchen"), ("Input.SourceUrl", "https://example.com/bread")]);
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];
        var id = await factory.RecipeIdAsync(slug);

        var refused = await editor.PostFormAsync($"/manage/recipes/{id}/edit",
            ("Input.Title", "Borrowed Bread"), ("Input.Ingredients", "1 cup flour"), ("Input.Steps", "Mix."),
            ("Input.SourceType", "Personal"), ("Input.Version", "1"), ("Action", "publish"));
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refused.TextAsync()).ShouldContain("Only an Admin can change the source of a web recipe");

        using var admin = await factory.SignInAsync("admin-web@example.com", AppRoles.Admin);
        var allowed = await admin.PostFormAsync($"/manage/recipes/{id}/edit",
            ("Input.Title", "Borrowed Bread"), ("Input.Ingredients", "1 cup flour"), ("Input.Steps", "Mix."),
            ("Input.SourceType", "Personal"), ("Input.Version", "1"), ("Action", "publish"));
        allowed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("Req", "BR-10")]
    public async Task Web_source_without_url_and_name_is_refused()
    {
        using var editor = await factory.SignInAsync("ed-web2@example.com");
        var response = await editor.CreateRecipeAsync("No Credit", extra: [("Input.SourceType", "Web")]);

        var html = await response.TextAsync();
        html.ShouldContain("A web recipe needs the address it came from");
        html.ShouldContain("A web recipe needs the source&#x27;s name");
    }

    [Fact]
    [Trait("Req", "SEC-23")]
    public async Task Editor_and_reading_pages_have_no_inline_script_or_handlers()
    {
        using var editor = await factory.SignInAsync("ed-csp@example.com");
        var slug = await editor.AddRecipeAsync("Csp Check");
        var id = await factory.RecipeIdAsync(slug);

        foreach (var path in new[] { "/manage/recipes/new", $"/manage/recipes/{id}/edit", "/recipes/" + slug, "/recipes", "/search?q=csp",
                     "/manage", "/manage/trash", "/manage/tags", "/manage/collections", "/collections" })
        {
            var html = await editor.GetStringAsync(path, Ct);
            System.Text.RegularExpressions.Regex.IsMatch(html, @"<script(?![^>]*\bsrc=)[^>]*>").ShouldBeFalse(path);
            System.Text.RegularExpressions.Regex.IsMatch(html, @"\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ShouldBeFalse(path);
            System.Text.RegularExpressions.Regex.IsMatch(html, @"\sstyle\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ShouldBeFalse(path);
        }
    }
}
