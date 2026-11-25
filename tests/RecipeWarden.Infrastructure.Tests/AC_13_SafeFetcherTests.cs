using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>SEC-40 to SEC-42, AC-13, ADR 0024: what SafeFetcher fetches, and what it refuses before connecting.</summary>
[Trait("Req", "AC-13")]
[Trait("Req", "SEC-41")]
[Trait("Req", "SEC-42")]
public sealed class AC_13_SafeFetcherTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeWeb web = new();
    private readonly ListLogger logger = new();
    private readonly SafeFetcher fetcher;

    public AC_13_SafeFetcherTests()
    {
        fetcher = new SafeFetcher(new ImportOptions { TimeoutSeconds = 1, MaxPageBytes = 2000, MaxImageBytes = 3000 }, web, web, logger);
    }

    [Fact]
    public async Task A_public_page_is_fetched_from_the_vetted_address()
    {
        web.Html("http://recipes.example/soup", "<html>soup</html>");

        var result = await fetcher.FetchAsync("http://recipes.example/soup", FetchPurpose.Page, Ct);

        result.Ok.ShouldBeTrue(result.Error);
        Encoding.UTF8.GetString(result.Body!).ShouldBe("<html>soup</html>");
        result.ContentType.ShouldBe("text/html");
        result.Charset.ShouldBe("utf-8");
        web.Connections.ShouldBe([FakeWeb.PublicAddress]);
    }

    [Fact]
    [Trait("Req", "ADR-0024")]
    public async Task Requests_look_like_a_browser_and_send_no_cookies()
    {
        web.Html("http://recipes.example/", "<html></html>");
        await fetcher.FetchAsync("http://recipes.example/", FetchPurpose.Page, Ct);
        var request = web.Requests.ShouldHaveSingleItem();
        request.Headers["User-Agent"].ShouldStartWith("Mozilla/5.0");
        request.Headers.ShouldContainKey("Accept-Language");
        request.Headers.ShouldNotContainKey("Cookie");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    public async Task Names_that_resolve_to_private_addresses_are_refused_without_connecting(string address)
    {
        web.Host("internal.example", IPAddress.Parse(address));

        var result = await fetcher.FetchAsync("http://internal.example/admin", FetchPurpose.Page, Ct);

        result.Failure.ShouldBe(FetchFailure.Blocked);
        result.Error.ShouldBe(UrlRules.PrivateAddress);
        result.OfferPaste.ShouldBeFalse();
        web.Connections.ShouldBeEmpty();
        logger.Warnings.ShouldContain(m => m.Contains("internal.example", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/")]
    public async Task Literal_private_addresses_are_refused_without_dns_or_connecting(string url)
    {
        var result = await fetcher.FetchAsync(url, FetchPurpose.Page, Ct);
        result.Failure.ShouldBe(FetchFailure.Blocked);
        web.Lookups.ShouldBeEmpty();
        web.Connections.ShouldBeEmpty();
        logger.Warnings.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_name_with_one_private_answer_is_refused()
    {
        web.Host("mixed.example", FakeWeb.PublicAddress, IPAddress.Parse("10.0.0.5"));
        (await fetcher.FetchAsync("http://mixed.example/", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.Blocked);
        web.Connections.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("http://internal.example/")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://127.0.0.1:80/")]
    public async Task A_redirect_to_a_private_address_is_refused_before_connecting(string target)
    {
        web.Host("internal.example", IPAddress.Parse("192.168.1.10"));
        web.Redirect("http://recipes.example/go", target);

        var result = await fetcher.FetchAsync("http://recipes.example/go", FetchPurpose.Page, Ct);

        result.Failure.ShouldBe(FetchFailure.Blocked);
        web.Connections.ShouldBe([FakeWeb.PublicAddress]); // only the first hop
    }

    [Fact]
    public async Task Redirects_to_other_schemes_are_refused()
    {
        web.Redirect("http://recipes.example/go", "file:///etc/passwd");
        (await fetcher.FetchAsync("http://recipes.example/go", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.Refused);
    }

    [Fact]
    public async Task Three_redirects_are_followed_and_a_fourth_is_refused()
    {
        web.Redirect("http://a.example/1", "http://a.example/2", 301)
            .Redirect("http://a.example/2", "/3", 307)
            .Redirect("http://a.example/3", "http://b.example/4")
            .Html("http://b.example/4", "<html>ok</html>");
        var ok = await fetcher.FetchAsync("http://a.example/1", FetchPurpose.Page, Ct);
        ok.Ok.ShouldBeTrue(ok.Error);
        ok.FinalUri!.AbsoluteUri.ShouldBe("http://b.example/4");

        web.Redirect("http://b.example/4", "http://b.example/5");
        var tooMany = await fetcher.FetchAsync("http://a.example/1", FetchPurpose.Page, Ct);
        tooMany.Failure.ShouldBe(FetchFailure.TooManyRedirects);
    }

    [Theory]
    [InlineData("ftp://recipes.example/", FetchFailure.Refused)]
    [InlineData("https://recipes.example:8443/", FetchFailure.Refused)]
    [InlineData("http://user:pw@recipes.example/", FetchFailure.Refused)]
    public async Task Bad_addresses_are_refused_without_dns(string url, FetchFailure failure)
    {
        (await fetcher.FetchAsync(url, FetchPurpose.Page, Ct)).Failure.ShouldBe(failure);
        web.Lookups.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_pages_and_images_of_the_allowed_types_are_read()
    {
        web.Serve("http://recipes.example/file.pdf", 200, "application/pdf", "%PDF"u8.ToArray())
            .Serve("http://recipes.example/ld", 200, "application/ld+json", "{}"u8.ToArray())
            .Serve("http://recipes.example/pic.jpg", 200, "image/jpeg", [0xFF, 0xD8, 0xFF])
            .Serve("http://recipes.example/pic.svg", 200, "image/svg+xml", "<svg/>"u8.ToArray());

        (await fetcher.FetchAsync("http://recipes.example/file.pdf", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.WrongType);
        (await fetcher.FetchAsync("http://recipes.example/ld", FetchPurpose.Page, Ct)).Ok.ShouldBeTrue();
        (await fetcher.FetchAsync("http://recipes.example/pic.jpg", FetchPurpose.Image, Ct)).Ok.ShouldBeTrue();
        (await fetcher.FetchAsync("http://recipes.example/pic.jpg", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.WrongType);
        (await fetcher.FetchAsync("http://recipes.example/pic.svg", FetchPurpose.Image, Ct)).Failure.ShouldBe(FetchFailure.WrongType);
    }

    [Fact]
    public async Task Responses_over_the_cap_are_refused_with_or_without_a_length()
    {
        web.Serve("http://recipes.example/big", 200, "text/html", new byte[2001])
            .Serve("http://recipes.example/stream", 200, "text/html", new byte[2500], noLength: true)
            .Serve("http://recipes.example/fits", 200, "text/html", new byte[2000], noLength: true)
            .Serve("http://recipes.example/image", 200, "image/png", new byte[2500]);

        (await fetcher.FetchAsync("http://recipes.example/big", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.TooBig);
        (await fetcher.FetchAsync("http://recipes.example/stream", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.TooBig);
        (await fetcher.FetchAsync("http://recipes.example/fits", FetchPurpose.Page, Ct)).Ok.ShouldBeTrue();
        (await fetcher.FetchAsync("http://recipes.example/image", FetchPurpose.Image, Ct)).Ok.ShouldBeTrue(); // 10 MB cap for images
    }

    [Fact]
    public async Task The_cap_applies_after_decompression()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gz.Write(new byte[50_000]);
        }

        web.Serve("http://recipes.example/gz", 200, "text/html", ms.ToArray(), new Dictionary<string, string> { ["Content-Encoding"] = "gzip" });
        (await fetcher.FetchAsync("http://recipes.example/gz", FetchPurpose.Page, Ct)).Failure.ShouldBe(FetchFailure.TooBig);
    }

    [Theory]
    [InlineData(403, FetchFailure.SiteRefused, true)]
    [InlineData(429, FetchFailure.SiteRefused, true)]
    [InlineData(503, FetchFailure.SiteRefused, true)]
    [InlineData(404, FetchFailure.NotFound, false)]
    [InlineData(500, FetchFailure.Network, true)]
    public async Task Site_errors_say_what_happened(int status, FetchFailure failure, bool offerPaste)
    {
        web.Serve("http://recipes.example/x", status, "text/html", "no"u8.ToArray());
        var result = await fetcher.FetchAsync("http://recipes.example/x", FetchPurpose.Page, Ct);
        result.Failure.ShouldBe(failure);
        result.OfferPaste.ShouldBe(offerPaste);
    }

    [Fact]
    public async Task A_site_that_never_answers_times_out()
    {
        web.Host("slow.example");
        web.Hang = true;
        var result = await fetcher.FetchAsync("http://slow.example/", FetchPurpose.Page, Ct);
        result.Failure.ShouldBe(FetchFailure.Timeout);
        result.Error.ShouldBe(SafeFetcher.TimeoutMessage);
    }

    [Fact]
    public async Task Unknown_hosts_are_reported()
    {
        (await fetcher.FetchAsync("http://nowhere.example/", FetchPurpose.Page, Ct)).Error.ShouldBe(SafeFetcher.UnknownHostMessage);
    }

    public async ValueTask DisposeAsync()
    {
        fetcher.Dispose();
        await web.DisposeAsync();
    }

    private sealed class ListLogger : ILogger<SafeFetcher>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                lock (Warnings)
                {
                    Warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}
