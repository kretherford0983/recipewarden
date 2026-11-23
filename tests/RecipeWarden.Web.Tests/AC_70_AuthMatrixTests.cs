using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// SEC-10, AC-70: every registered endpoint has an expected access level from section 7.2, and anonymous
/// requests to anything not marked Anonymous are redirected to sign-in (pages) or get 401 (htmx/API).
/// Adding an endpoint without adding it here fails <see cref="Every_endpoint_is_in_the_access_table"/>.
/// </summary>
[Trait("Req", "SEC-10")]
[Trait("Req", "AC-70")]
[Trait("Req", "FR-AUTH-01")]
public sealed class AC_70_AuthMatrixTests(RecipeWardenFactory factory) : IClassFixture<RecipeWardenFactory>
{
    public enum Access
    {
        Anonymous,
        SignedIn,
        Editor,
        Admin,
    }

    /// <summary>Section 7.2, restricted to the routes that exist so far.</summary>
    public static readonly IReadOnlyDictionary<string, Access> Expected = new Dictionary<string, Access>(StringComparer.Ordinal)
    {
        ["/"] = Access.Anonymous,
        ["/health"] = Access.Anonymous,
        ["/error"] = Access.Anonymous,          // SEC-73 generic error page (not listed in 7.2; see traceability notes)
        ["/account/login"] = Access.Anonymous,
        ["/account/logout"] = Access.SignedIn,
        ["/account/theme"] = Access.SignedIn,   // FR-UI-01 profile theme (POST only)
        ["/manage"] = Access.Editor,
        ["/admin"] = Access.Admin,

        // M1 (section 7.2)
        ["/recipes"] = Access.Anonymous,
        ["/recipes/{slug}"] = Access.Anonymous,
        ["/search"] = Access.Anonymous,
        ["/tags/{slug}"] = Access.Anonymous,
        ["/collections"] = Access.Anonymous,
        ["/collections/{slug}"] = Access.Anonymous,
        ["/manage/recipes/new"] = Access.Editor,
        ["/manage/recipes/{id:int}/edit"] = Access.Editor,
        ["/manage/trash"] = Access.Editor,
        ["/manage/tags"] = Access.Editor,
        ["/manage/collections"] = Access.Editor,

        // M2 (section 7.2)
        ["/manage/conversions"] = Access.Editor,
        ["/account/preferences"] = Access.SignedIn, // FR-UI-02 profile units and default scale (POST only)

        // M3 (section 7.2; ADR 0017, ADR 0019: editor writes live under /manage)
        ["/manage/kitchen"] = Access.Editor,
        ["/manage/recipes/{id:int}/cooked"] = Access.Editor,
        ["/manage/recipes/{id:int}/suggestions"] = Access.Editor,

        // M4 (section 7.2; ADR 0022: /media is anonymous for published recipes' images only, checked by the page)
        ["/manage/import"] = Access.Editor,
        ["/manage/import/{id:int}/review"] = Access.Editor,
        ["/media/{id}/{variant?}"] = Access.Anonymous,

        // M5 (section 7.2; ADR 0028: lists are anonymous to view and tick, editing handlers need Editor; ADR 0031)
        ["/recipes/{slug}/cook"] = Access.Anonymous,
        ["/recipes/{slug}/print"] = Access.Anonymous,
        ["/recipes/{slug}/export.{format}"] = Access.Anonymous,
        ["/recipes/export.zip"] = Access.Anonymous,
        ["/collections/{slug}/export.zip"] = Access.Anonymous,
        ["/shopping"] = Access.Anonymous,
        ["/shopping/{id:int}"] = Access.Anonymous,
        ["/shopping/new"] = Access.Editor,
    };

    public static TheoryData<string> ProtectedRoutes() =>
        new(Expected.Where(e => e.Value != Access.Anonymous).Select(e => e.Key));

    public static TheoryData<string> AnonymousRoutes() =>
        new(Expected.Where(e => e.Value == Access.Anonymous).Select(e => e.Key));

    private static string Normalize(string raw)
    {
        var path = "/" + raw.Trim('/').ToLowerInvariant();
        if (path.EndsWith("/index", StringComparison.Ordinal))
        {
            path = path[..^"/index".Length];
        }

        return path.Length == 0 ? "/" : path;
    }

    private List<string> RegisteredRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => Normalize(e.RoutePattern.RawText ?? string.Empty))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Every_endpoint_is_in_the_access_table()
    {
        var missing = RegisteredRoutes().Where(r => !Expected.ContainsKey(r)).ToList();
        missing.ShouldBeEmpty($"Add these routes to the access table (section 7.2): {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_route_in_the_table_is_registered()
    {
        var registered = RegisteredRoutes();
        Expected.Keys.Where(k => !registered.Contains(k)).ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Anonymous_page_request_redirects_to_sign_in(string route)
    {
        await factory.SeedSamplesAsync();
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync(WebTestHelpers.SampleUrl(route), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldStartWith("https://localhost/account/login?ReturnUrl=");
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Anonymous_htmx_request_gets_401(string route)
    {
        await factory.SeedSamplesAsync();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, WebTestHelpers.SampleUrl(route));
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(ProtectedRoutes))]
    public async Task Anonymous_post_is_refused_before_anything_runs(string route)
    {
        await factory.SeedSamplesAsync();
        using var client = factory.CreateHttpsClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, WebTestHelpers.SampleUrl(route)) { Content = new StringContent("") };
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AnonymousRoutes))]
    public async Task Anonymous_routes_are_reachable_without_sign_in(string route)
    {
        await factory.SeedSamplesAsync();
        using var client = factory.CreateHttpsClient();
        var response = await client.GetAsync(WebTestHelpers.SampleUrl(route), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "AC-71")]
    [Trait("Req", "FR-AUTH-03")]
    public async Task Editor_gets_403_on_admin_routes()
    {
        using var editor = await factory.SignInAsync("editor-matrix@example.com", AppRoles.Editor);
        foreach (var route in Expected.Where(e => e.Value == Access.Admin).Select(e => e.Key))
        {
            var response = await editor.GetAsync(route, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, route);
        }
    }

    [Fact]
    [Trait("Req", "FR-AUTH-02")]
    public async Task Editor_can_open_editor_routes()
    {
        await factory.SeedSamplesAsync();
        using var editor = await factory.SignInAsync("editor-matrix2@example.com", AppRoles.Editor);
        foreach (var route in Expected.Where(e => e.Value == Access.Editor).Select(e => e.Key))
        {
            var response = await editor.GetAsync(WebTestHelpers.SampleUrl(route), TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, route);
        }
    }

    [Fact]
    public async Task Signed_in_user_without_a_role_cannot_open_editor_or_admin_routes()
    {
        using var reader = await factory.SignInAsync("norole@example.com", role: null);
        (await reader.GetAsync("/manage", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/admin", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Req", "FR-AUTH-03")]
    public async Task Admin_can_open_admin_and_editor_routes()
    {
        using var admin = await factory.SignInAsync("admin-matrix@example.com", AppRoles.Admin);
        (await admin.GetAsync("/admin", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync("/manage", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Req", "SEC-05")]
    public async Task There_is_no_registration_endpoint()
    {
        using var client = factory.CreateHttpsClient();
        foreach (var route in new[] { "/account/register", "/identity/account/register", "/register" })
        {
            var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldNotBe(HttpStatusCode.OK, route);
        }
    }
}
