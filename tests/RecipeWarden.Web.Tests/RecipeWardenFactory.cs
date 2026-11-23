using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.Web.Tests;

/// <summary>
/// In-memory host over a real temp SQLite database, in a production-like environment ("Testing").
/// Requests are HTTPS (the scheme the browser sees through Cloudflare) so __Host- cookies work.
/// </summary>
public sealed partial class RecipeWardenFactory : WebApplicationFactory<Program>
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";
    public const string Password = "correct horse battery staple";

    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "rw-web-" + Guid.NewGuid().ToString("N"));

    /// <summary>ADR 0024: the pretend internet every import fetch goes to (no real network in tests).</summary>
    internal FakeWeb Web { get; } = new();

    /// <summary>Extra settings a test class can add before the host starts.</summary>
    public Dictionary<string, string?> Settings { get; } = new();

    /// <summary>Extra service registrations a test class can add before the host starts (e.g. a fake disk, AC-99).</summary>
    public List<Action<IServiceCollection>> ExtraServices { get; } = [];

    public string DataDirectory => dataDirectory;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestDatabaseTemplate.CopyTo(dataDirectory); // migrated once per test process (docs/testing.md)
        builder.UseEnvironment("Testing");
        builder.UseSetting("Data:Directory", dataDirectory);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Operations:Background", "false"); // M7: tests run the scheduler and health monitor by hand
        // SEC-63: test classes sign in many times from one test IP; SEC_63_SignInRateLimitTests checks the limit itself.
        builder.UseSetting("RateLimits:SignInsPerMinute", "100000");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(s =>
        {
            s.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>();
            s.UseFastPasswordHashing();
            s.AddSingleton<IDnsResolver>(Web);
            s.AddSingleton<ISocketConnector>(Web);
            s.AddSingleton<Infrastructure.Health.IVolumeProbe>(new RoomyDisk()); // tests never depend on the build machine's disk
            foreach (var extra in ExtraServices)
            {
                extra(s);
            }
        });
    }

    public HttpClient CreateHttpsClient(bool allowRedirects = false) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = allowRedirects,
            HandleCookies = true,
        });

    public async Task EnsureUserAsync(string email, string? role)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, Password)).Succeeded.ShouldBeTrue();
        if (role is not null)
        {
            (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        }
    }

    /// <summary>Signs in through the real login form (antiforgery included). Returns the signed-in client.</summary>
    public async Task<HttpClient> SignInAsync(string email, string? role = AppRoles.Editor)
    {
        await EnsureUserAsync(email, role);
        var client = CreateHttpsClient();
        var response = await PostLoginAsync(client, email, Password);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, "sign-in should succeed");
        return client;
    }

    public static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/account/login");
        return await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token,
        }), TestContext.Current.CancellationToken);
    }

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);
        var match = TokenInput().Match(html);
        if (match.Success)
        {
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        var meta = CsrfMeta().Match(html);
        meta.Success.ShouldBeTrue($"no antiforgery token on {path}");
        return WebUtility.HtmlDecode(meta.Groups[1].Value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Web.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenInput();

    [GeneratedRegex("<meta name=\"rw-csrf\" content=\"([^\"]+)\"")]
    private static partial Regex CsrfMeta();

    /// <summary>A 100 GB volume with 60 GB free.</summary>
    private sealed class RoomyDisk : Infrastructure.Health.IVolumeProbe
    {
        public (long Total, long Free) Measure(string path) => (100L * 1024 * 1024 * 1024, 60L * 1024 * 1024 * 1024);
    }

    /// <summary>TestServer has no socket; give each request a remote IP (loopback unless a test overrides it).</summary>
    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextMiddleware) =>
            {
                var header = ctx.Request.Headers[RemoteIpHeader].ToString();
                ctx.Connection.RemoteIpAddress = header.Length > 0 ? IPAddress.Parse(header) : IPAddress.Loopback;
                ctx.Request.Headers.Remove(RemoteIpHeader);
                return nextMiddleware(ctx);
            });
            next(app);
        };
    }
}
