using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Tests.Shared;

namespace RecipeWarden.E2E.Tests;

/// <summary>
/// The app on a real Kestrel port (loopback) with its own temp database, plus one browser for the test class.
/// The browser is Playwright's Chromium, or the executable in RW_E2E_BROWSER (e.g. a system Chrome).
/// localhost is a secure context, so the browser keeps the Secure __Host- cookies over plain HTTP here.
/// </summary>
public sealed class BrowserHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "correct horse battery staple";

    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "rw-e2e-" + Guid.NewGuid().ToString("N"));
    private IPlaywright? playwright;

    public IBrowser Browser { get; private set; } = null!;

    /// <summary>ADR 0024: the pretend internet URL imports fetch from.</summary>
    internal FakeWeb Web { get; } = new();
    public string BaseUrl { get; private set; } = string.Empty;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestDatabaseTemplate.CopyTo(dataDirectory);
        builder.UseEnvironment("Testing");
        builder.UseSetting("Data:Directory", dataDirectory);
        builder.UseSetting("Kestrel:Endpoints:Http:Url", "http://127.0.0.1:0");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("Operations:Background", "false"); // M7: no scheduler or monitor during browser tests
        builder.ConfigureServices(s =>
        {
            s.UseFastPasswordHashing();
            s.AddSingleton<RecipeWarden.Infrastructure.Health.IVolumeProbe>(new RoomyDisk());
            s.AddSingleton<IDnsResolver>(Web);
            s.AddSingleton<ISocketConnector>(Web);
        });
    }

    /// <summary>Browser tests never depend on the build machine's disk (BR-81).</summary>
    private sealed class RoomyDisk : RecipeWarden.Infrastructure.Health.IVolumeProbe
    {
        public (long Total, long Free) Measure(string path) => (100L * 1024 * 1024 * 1024, 60L * 1024 * 1024 * 1024);
    }

    public async ValueTask InitializeAsync()
    {
        UseKestrel();
        StartServer();
        var address = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        BaseUrl = address.Replace("127.0.0.1", "localhost", StringComparison.Ordinal).TrimEnd('/');

        playwright = await Playwright.CreateAsync();
        var options = new BrowserTypeLaunchOptions { Headless = true };
        if (Environment.GetEnvironmentVariable("RW_E2E_BROWSER") is { Length: > 0 } path)
        {
            options.ExecutablePath = path;
        }

        if (Environment.GetEnvironmentVariable("RW_E2E_NO_SANDBOX") == "1")
        {
            options.Args = ["--no-sandbox"]; // only for running as root in a container
        }

        Browser = await playwright.Chromium.LaunchAsync(options);
    }

    public async Task CreateUserAsync(string email, string role)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, Password)).Succeeded.ShouldBeTrue();
        (await users.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
    }

    /// <summary>A fresh browser context (cookies, viewport) per test.</summary>
    public Task<IBrowserContext> NewContextAsync(int width = 1280, int height = 900) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            // As in production: cloudflared (a trusted proxy, here loopback) tells the app the browser used HTTPS
            // (SEC-61), so antiforgery and the Secure __Host- cookies behave exactly as they do behind the tunnel.
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https" },
        });

    public static async Task SignInAsync(IPage page, string email)
    {
        await page.GotoAsync("/account/login");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/account/login", StringComparison.Ordinal));
    }

    public new async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        playwright?.Dispose();
        await base.DisposeAsync();
        await Web.DisposeAsync();
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
