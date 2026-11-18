using System.Net;

namespace RecipeWarden.Web.Tests;

[Trait("Req", "SEC-23")]
[Trait("Req", "SEC-62")]
[Trait("Req", "AC-76")]
public sealed class SEC_62_SecurityHeadersTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    public static TheoryData<string> Paths() => new("/", "/health", "/account/login", "/does-not-exist", "/css/site.css", "/manage");

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Header(response, "Content-Security-Policy").ShouldBe(
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; " +
            "base-uri 'self'; form-action 'self'; frame-ancestors 'none'");
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header(response, "Permissions-Policy").ShouldBe("camera=(), microphone=(), geolocation=()");
        Header(response, "Cross-Origin-Opener-Policy").ShouldBe("same-origin");
        Header(response, "Strict-Transport-Security").ShouldBe("max-age=31536000");
    }

    [Fact]
    public async Task No_HSTS_over_plain_http_from_an_untrusted_source()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "SEC-61")]
    public async Task Trusted_proxy_forwarded_proto_turns_on_https_behaviour()
    {
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https"); // from 127.0.0.1, a trusted proxy by default
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Header(response, "Strict-Transport-Security").ShouldBe("max-age=31536000");
    }

    [Fact]
    public async Task Pages_contain_no_inline_script_or_event_handlers()
    {
        using var client = factory.CreateHttpsClient();
        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        System.Text.RegularExpressions.Regex.IsMatch(html, @"<script(?![^>]*\bsrc=)[^>]*>").ShouldBeFalse("inline <script> found");
        System.Text.RegularExpressions.Regex.IsMatch(html, @"\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse("inline event handler found");
        System.Text.RegularExpressions.Regex.IsMatch(html, @"\sstyle\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .ShouldBeFalse("inline style attribute found");
    }

    [Fact]
    [Trait("Req", "SEC-73")]
    public async Task Errors_show_a_generic_page_with_a_reference()
    {
        using var client = await factory.SignInAsync("errors@example.com");
        var response = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        html.ShouldContain("Not found");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("Reference:");
        html.ShouldNotContain("Exception");
    }

    [Fact]
    [Trait("Req", "SEC-10")]
    public async Task Unknown_urls_are_denied_by_default_for_anonymous_visitors()
    {
        // The fallback policy also covers requests that match no endpoint, so anonymous probing
        // cannot tell which routes exist.
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task No_server_banner()
    {
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values)
        : response.Content.Headers.TryGetValues(name, out var cv) ? string.Join(",", cv)
        : throw new ShouldAssertException($"{name} header missing on {response.RequestMessage?.RequestUri}");
}
