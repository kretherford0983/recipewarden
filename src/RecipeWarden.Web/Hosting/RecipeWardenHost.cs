using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeWarden.Core.Health;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Web.Operations;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Web.Security;
using RecipeWarden.Web.Theme;
using Serilog;

namespace RecipeWarden.Web.Hosting;

/// <summary>Service registration and middleware pipeline for the web app.</summary>
public static class RecipeWardenHost
{
    /// <summary>Environment variable that points at the server config file (installer default below).</summary>
    public const string ConfigPathVariable = "RECIPEWARDEN_CONFIG";
    public const string DefaultConfigPath = "/etc/recipewarden/appsettings.Production.json";

    public const string AuthCookieName = "__Host-rw-auth";
    public const string AntiforgeryCookieName = "__Host-rw-af";
    public const string AntiforgeryHeaderName = "RequestVerificationToken";

    /// <summary>network.json in the same folder as the main config (written by install.sh, ADR 0007).</summary>
    public static string NetworkConfigPath(string configPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? "/", "network.json");

    public static WebApplicationBuilder AddRecipeWarden(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Environment.IsProduction())
        {
            var path = Environment.GetEnvironmentVariable(ConfigPathVariable) ?? DefaultConfigPath;
            builder.Configuration.AddJsonFile(path, optional: true, reloadOnChange: false);
            // Installer-owned network settings (bind, port, trusted proxies, allowed IPs) beside the main config.
            builder.Configuration.AddJsonFile(NetworkConfigPath(path), optional: true, reloadOnChange: false);
        }

        // FR-OPS-01, SEC-72: structured logs; request logging never includes bodies, cookies or tokens.
        builder.Services.AddSerilog((services, lc) => lc
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

        builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

        var services = builder.Services;
        services.AddRecipeWardenData(builder.Configuration);

        // SEC-71: Data Protection keys persisted to the data directory.
        var data = new DataOptions();
        builder.Configuration.GetSection(DataOptions.SectionName).Bind(data);
        services.AddDataProtection()
            .SetApplicationName("RecipeWarden")
            .PersistKeysToFileSystem(Directory.CreateDirectory(data.KeysDirectory));

        AddIdentityAndAuth(services);

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = AntiforgeryCookieName; // SEC-30
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
            o.HeaderName = AntiforgeryHeaderName; // htmx and fetch send the token in this header
        });

        // UX-05: fractions and accented names are sent as text (½, crème), not &#xBD; entities.
        // The encoder still escapes < > & " ' everywhere (SEC-22).
        services.AddWebEncoders(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
        services.AddRouting(o => o.LowercaseUrls = true);
        services.AddRazorPages(o =>
        {
            // SEC-10: anonymous access is granted explicitly, page by page (section 7.2).
            o.Conventions.AllowAnonymousToPage("/Index");
            o.Conventions.AllowAnonymousToPage("/Error");
            o.Conventions.AllowAnonymousToPage("/Account/Login");
            o.Conventions.AllowAnonymousToPage("/Account/LoginTwoFactor"); // SEC-04: the second step; the page needs the first step's cookie
            o.Conventions.AddPageApplicationModelConvention("/Account/Login", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SignInPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Account/LoginTwoFactor", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SignInPolicy)));
            o.Conventions.AllowAnonymousToPage("/Recipes/Index");
            o.Conventions.AllowAnonymousToPage("/Recipes/Details");
            o.Conventions.AllowAnonymousToPage("/Search");
            o.Conventions.AllowAnonymousToPage("/Tags/Details");
            o.Conventions.AllowAnonymousToPage("/Collections/Index");
            o.Conventions.AllowAnonymousToPage("/Collections/Details");
            o.Conventions.AllowAnonymousToPage("/Media"); // ADR 0022: the page itself decides (published recipes only)
            o.Conventions.AllowAnonymousToPage("/Recipes/Print"); // ADR 0031: print, cook mode and export follow the recipe
            o.Conventions.AllowAnonymousToPage("/Recipes/Cook");
            o.Conventions.AllowAnonymousToPage("/Recipes/Export");
            o.Conventions.AllowAnonymousToPage("/Recipes/BulkExport"); // readers get published recipes only
            o.Conventions.AllowAnonymousToPage("/Collections/Export");
            o.Conventions.AllowAnonymousToPage("/Recipes/ExportAi"); // ADR 0033: GET only, readers get published recipes
            o.Conventions.AllowAnonymousToPage("/Recipes/ExportAiAll");
            o.Conventions.AllowAnonymousToPage("/Collections/ExportAi");
            o.Conventions.AllowAnonymousToPage("/Shopping/Index"); // D-10: anyone views lists and ticks items (ADR 0028)
            o.Conventions.AllowAnonymousToPage("/Shopping/Details"); // editing handlers check the Editor role themselves
            o.Conventions.AuthorizePage("/Shopping/New", AppPolicies.CanEditRecipes);
            o.Conventions.AuthorizePage("/Shopping/Staples", AppPolicies.CanEditRecipes); // FR-SHOP-05: adds items
            // SEC-63: search and browse are limited per IP.
            o.Conventions.AddPageApplicationModelConvention("/Search", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Recipes/Index", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Manage/Import/Index", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.UrlImportPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Recipes/BulkExport", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Collections/Export", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Recipes/ExportAiAll", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Collections/ExportAi", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
            o.Conventions.AddPageApplicationModelConvention("/Shopping/Details", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.ShoppingCheckPolicy)));
            o.Conventions.AuthorizeFolder("/Manage", AppPolicies.CanEditRecipes);
            o.Conventions.AuthorizeFolder("/Admin", AppPolicies.CanAdminister);
            o.Conventions.AllowAnonymousToPage("/Setup"); // SEC-15: the page itself requires the one-time token, and 404s once set up
            o.Conventions.AddPageApplicationModelConvention("/Setup", m => m.EndpointMetadata.Add(new EnableRateLimitingAttribute(RateLimits.SearchPolicy)));
        });

        services.AddScoped<ThemeResolver>();
        services.AddScoped<Permissions>();
        services.AddRecipeWardenRateLimits(builder.Configuration);
        services.AddHostedService<TrashPurgeService>(); // BR-05

        // M7 Operations (ADR 0037 to 0041): the request's user and IP are the audit actor; backups and health run in the background.
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IAuditActor, HttpAuditActor>());
        if (builder.Configuration.GetValue("Operations:Background", defaultValue: true))
        {
            services.AddHostedService<BackupSchedulerService>();
            services.AddHostedService<HealthMonitorService>();
            services.AddHostedService<PythonStartupCheck>(); // FR-OPS-06, ADR 0051
        }

        var network = new NetworkOptions();
        builder.Configuration.GetSection(NetworkOptions.SectionName).Bind(network);
        services.AddSingleton(network);
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // SEC-61: only X-Forwarded-Proto is used (client IP comes from CF-Connecting-IP), only from trusted proxies.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var proxy in network.ParseTrustedProxies())
            {
                o.KnownProxies.Add(proxy);
            }

            o.ForwardLimit = 1;
        });

        return builder;
    }

    private static void AddIdentityAndAuth(IServiceCollection services)
    {
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddRecipeWardenIdentityCore()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(o =>
        {
            // SEC-03
            o.Cookie.Name = AuthCookieName;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = AccountRules.SessionSlidingExpiry;
            o.SlidingExpiration = true;
            o.LoginPath = "/account/login";
            o.LogoutPath = "/account/logout";
            o.AccessDeniedPath = "/account/login";
            o.Events = new CookieAuthenticationEvents
            {
                OnSigningIn = ctx =>
                {
                    // SEC-03: "remember me" lasts at most 30 days and does not slide; otherwise 8 hours, sliding.
                    if (ctx.Properties.IsPersistent)
                    {
                        ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow + AccountRules.RememberMeMaximum;
                        ctx.Properties.AllowRefresh = false;
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToLogin = ctx =>
                {
                    if (HtmxRequest.WantsStatusCode(ctx.Request))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; // AC-70
                    }
                    else
                    {
                        ctx.Response.Redirect(ctx.RedirectUri);
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden; // AC-71: a 403, never a redirect
                    return Task.CompletedTask;
                },
            };
        });

        // SEC-03: password or role changes end other sessions within 5 minutes.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = AccountRules.SecurityStampValidationInterval);

        services.AddAuthorizationBuilder()
            .AddPolicy(AppPolicies.CanEditRecipes, p => p.RequireAuthenticatedUser().RequireRole(AppPolicies.EditorRoles))
            .AddPolicy(AppPolicies.CanAdminister, p => p.RequireAuthenticatedUser().RequireRole(AppPolicies.AdminRoles))
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build()); // SEC-10: deny by default
    }

    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public static WebApplication UseRecipeWarden(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var network = app.Services.GetRequiredService<NetworkOptions>();

        // Order matters: allow-list and scheme are decided on the connection IP (the proxy);
        // only after that is the client IP replaced from CF-Connecting-IP.
        app.UseMiddleware<SourceIpAllowListMiddleware>(network.ParseAllowedNetworks());
        app.UseForwardedHeaders();
        app.UseMiddleware<CloudflareClientIpMiddleware>(network.ParseTrustedProxies());

        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<MaintenanceMiddleware>(); // BR-63: a restore blocks every other request

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/error"); // SEC-73: generic page with a correlation ID
        }

        app.UseStatusCodePagesWithReExecute("/error", "?code={0}");

        // Static assets are public and served before routing, so they never hit the fallback policy.
        app.UseStaticFiles();
        app.UseSerilogRequestLogging();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter(); // after authentication: the write limit is per user (SEC-63)

        // FR-OPS-01: liveness only; says "ok" and nothing else (section 7.2).
        app.MapGet("/health", () => Results.Text("ok", "text/plain"))
            .AllowAnonymous()
            .WithName("Health");

        // FR-OPS-05: readiness for Uptime Kuma or similar; 503 only when Critical, and no details (section 7.2).
        app.MapGet("/health/ready", (HealthState health) => health.Status == HealthStatus.Critical
                ? Results.Text("critical", "text/plain", statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Text(health.Status == HealthStatus.Warning ? "warning" : "ok", "text/plain"))
            .AllowAnonymous()
            .WithName("Ready");

        // UX-15, ADR 0060: the web app manifest, named from the site name setting (anonymous, like the icons it lists).
        app.MapGet("/manifest.webmanifest", async (RecipeWarden.Infrastructure.Settings.SettingsStore settings, CancellationToken ct) =>
                Results.Json(RecipeWarden.Core.Presentation.AppManifest.For(await settings.GetAsync(RecipeWarden.Infrastructure.Settings.SettingKeys.SiteName, ct)),
                    contentType: "application/manifest+json"))
            .AllowAnonymous()
            .WithName("Manifest");

        app.MapRazorPages();
        return app;
    }
}
