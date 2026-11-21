using RecipeWarden.Web.Content;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// AC-76, SEC-22: user content renders as inert text. Markdown goes through Markdig (raw HTML off) and an
/// HtmlSanitizer allow-list; everything else is Razor-encoded.
/// </summary>
[Trait("Req", "AC-76")]
[Trait("Req", "SEC-22")]
public sealed class AC_76_RecipeContentEncodingTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Hostile() => new(
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "[click](javascript:alert(1))",
        "[click](JaVaScRiPt:alert(1))",
        "[click](data:text/html;base64,PHNjcmlwdD4=)",
        "<a href=\"https://example.com\" onclick=\"alert(1)\">x</a>",
        "<svg onload=alert(1)>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<style>body{display:none}</style>",
        "![x](https://evil.example/pixel.png)",
        "<div style=\"background:url(javascript:alert(1))\">x</div>");

    [Theory]
    [MemberData(nameof(Hostile))]
    public void Markdown_renderer_output_is_inert(string input)
    {
        var html = MarkdownRenderer.Render(input).ToString()!;

        html.ShouldNotContain("<script", Case.Insensitive);
        html.ShouldNotContain("<img", Case.Insensitive);
        html.ShouldNotContain("<svg", Case.Insensitive);
        html.ShouldNotContain("<iframe", Case.Insensitive);
        html.ShouldNotContain("<style", Case.Insensitive);
        // Escaped text such as "&lt;div style=...javascript:...&gt;" is inert; no tag may carry the payload.
        System.Text.RegularExpressions.Regex.IsMatch(html, @"<[^>]*javascript:", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ShouldBeFalse(html);
        html.ShouldNotContain("href=\"data:", Case.Insensitive);
        System.Text.RegularExpressions.Regex.IsMatch(html, @"<[^>]+\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ShouldBeFalse(html);
        System.Text.RegularExpressions.Regex.IsMatch(html, @"<[^>]+\sstyle\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase).ShouldBeFalse(html);
    }

    [Fact]
    public void Ordinary_markdown_still_works_and_links_are_safe()
    {
        var html = MarkdownRenderer.Render("Use **fresh** basil. See [the site](https://example.com/x).\n\n- one\n- two").ToString()!;

        html.ShouldContain("<strong>fresh</strong>");
        html.ShouldContain("<li>one</li>");
        html.ShouldContain("href=\"https://example.com/x\"");
        html.ShouldContain("rel=\"nofollow noopener noreferrer\"");
    }

    [Fact]
    public void Empty_markdown_renders_nothing()
    {
        MarkdownRenderer.Render(null).ToString().ShouldBeEmpty();
        MarkdownRenderer.Render("   ").ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task Script_in_every_recipe_field_is_inert_on_the_page()
    {
        using var editor = await factory.SignInAsync("xss@example.com");
        const string evil = "<script>alert(1)</script><img src=x onerror=alert(2)>";
        var response = await editor.CreateRecipeAsync("Evil " + evil, "1 cup " + evil, "Stir " + evil, "publish", "tag" + "<b>x</b>",
            ("Input.Description", evil), ("Input.Notes", evil), ("Input.SourceType", "Personal"), ("Input.SourceName", evil),
            ("Input.SourceAuthor", evil));
        var slug = response.Headers.Location!.ToString()["/recipes/".Length..];

        using var reader = factory.CreateHttpsClient();
        foreach (var path in new[] { "/recipes/" + slug, "/recipes", "/search?q=evil", "/" })
        {
            var html = await reader.GetStringAsync(path, Ct);
            html.ShouldNotContain("<script>alert", Case.Insensitive, path);
            html.ShouldNotContain("<img src=x", Case.Insensitive, path);
            html.ShouldNotContain("<b>x</b>", Case.Insensitive, path);
        }

        (await reader.GetStringAsync("/recipes/" + slug, Ct)).ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Fact]
    [Trait("Req", "BR-10")]
    public async Task Javascript_source_urls_are_refused_so_links_are_always_http()
    {
        using var editor = await factory.SignInAsync("xss-url@example.com");
        var response = await editor.CreateRecipeAsync("Bad Link", extra:
            [("Input.SourceType", "Web"), ("Input.SourceName", "x"), ("Input.SourceUrl", "javascript:alert(1)")]);

        (await response.TextAsync()).ShouldContain("The source address must start with http:// or https://");
    }
}
