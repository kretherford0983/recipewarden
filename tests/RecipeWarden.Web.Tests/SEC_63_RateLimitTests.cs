using System.Net;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// SEC-63: search is limited per IP and writes per user; over the limit the answer is 429 with Retry-After.
/// This host lowers the limits (they are configurable) so the test needs only a few requests.
/// </summary>
[Trait("Req", "SEC-63")]
public sealed class SEC_63_RateLimitTests : IClassFixture<SEC_63_RateLimitTests.LowLimitFactory>
{
    public sealed class LowLimitFactory : IDisposable
    {
        public LowLimitFactory()
        {
            Factory = new RecipeWardenFactory();
            Factory.Settings["RateLimits:SearchPerMinute"] = "3";
            Factory.Settings["RateLimits:WritesPerMinute"] = "4";
            Factory.Settings["RateLimits:ShoppingChecksPerMinute"] = "3";
            Factory.Settings["Network:AllowedSourceIps:0"] = "203.0.113.0/24"; // SEC-60 allow-list
        }

        public RecipeWardenFactory Factory { get; }

        public void Dispose() => Factory.Dispose();
    }

    private readonly RecipeWardenFactory factory;

    public SEC_63_RateLimitTests(LowLimitFactory fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        factory = fixture.Factory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_is_limited_per_ip()
    {
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 3; i++)
        {
            (await Get(client, "/search?q=x", "203.0.113.7")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var limited = await Get(client, "/search?q=x", "203.0.113.7");
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();

        (await Get(client, "/search?q=x", "203.0.113.8")).StatusCode.ShouldBe(HttpStatusCode.OK); // another IP
        (await Get(client, "/", "203.0.113.7")).StatusCode.ShouldBe(HttpStatusCode.OK);           // not a search page
    }

    [Fact]
    public async Task Writes_are_limited_per_user()
    {
        using var editor = await factory.SignInAsync("limits@example.com");
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            codes.Add((await editor.CreateRecipeAsync($"Limited {i}")).StatusCode);
        }

        codes.ShouldContain(HttpStatusCode.TooManyRequests);
        codes.Take(3).ShouldAllBe(c => c == HttpStatusCode.Redirect);

        using var other = await factory.SignInAsync("limits-2@example.com");
        (await other.CreateRecipeAsync("Other user")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("Req", "ADR-0028")]
    public async Task Anonymous_ticks_are_limited_per_ip_and_other_handlers_are_not_counted()
    {
        await factory.SeedSamplesAsync();
        using var client = factory.CreateHttpsClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/shopping/1?handler=Check") { Content = new StringContent("") };
            request.Headers.Add(RecipeWardenFactory.RemoteIpHeader, "203.0.113.20");
            codes.Add((await client.SendAsync(request, Ct)).StatusCode);
        }

        codes.Take(3).ShouldAllBe(c => c == HttpStatusCode.BadRequest); // no token: refused, but counted
        codes[3].ShouldBe(HttpStatusCode.TooManyRequests);
        (await Get(client, "/shopping/1", "203.0.113.20")).StatusCode.ShouldBe(HttpStatusCode.OK); // viewing is not a tick
    }

    [Fact]
    [Trait("Req", "FR-EXP-03")]
    public async Task Bulk_export_shares_the_search_limit()
    {
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 3; i++)
        {
            (await Get(client, "/recipes/export.zip", "203.0.113.30")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Get(client, "/recipes/export.zip", "203.0.113.30")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(RecipeWardenFactory.RemoteIpHeader, ip);
        return await client.SendAsync(request, Ct);
    }
}
