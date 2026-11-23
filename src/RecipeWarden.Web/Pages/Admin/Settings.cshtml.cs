using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Health;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Infrastructure.Settings;
using RecipeWarden.Web.Hosting;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>
/// Section 7.2 /admin/settings, ADR 0037: settings people change in the app (site name, disk thresholds BR-81, the
/// health webhook FR-OPS-05). Network, proxy trust, rate limits and backup folders belong to the server owner's
/// config files and are shown read-only with how to change them.
/// </summary>
public sealed class SettingsModel(SettingsStore settings, HealthService health, AuditLog audit, NetworkOptions network,
    IConfiguration configuration, BackupFolders folders) : PageModel
{
    private const long GB = 1024L * 1024 * 1024;
    private const long MB = 1024L * 1024;

    /// <summary>SEC-13: only these fields bind.</summary>
    public sealed class SettingsInput
    {
        [StringLength(SiteSettings.MaxSiteNameLength)]
        public string? SiteName { get; set; }

        [Range(0, 99)]
        public decimal WarningPercent { get; set; }

        [Range(0, 100_000)]
        public decimal WarningGb { get; set; }

        [Range(0, 99)]
        public decimal CriticalPercent { get; set; }

        [Range(0, 10_000_000)]
        public decimal CriticalMb { get; set; }

        [StringLength(UrlRules.MaxUrlLength)]
        public string? WebhookUrl { get; set; }

        public WebhookFormat WebhookFormat { get; set; }

        public bool WebhookAllowLan { get; set; }
    }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public NetworkOptions Network => network;

    public RateLimitOptions Limits { get; } = Bind(configuration);

    public IReadOnlyList<string> BackupFolders => folders.Allowed();

    public async Task OnGetAsync(string? done)
    {
        Message = done == "saved" ? "Settings saved." : null;
        Fill(await settings.GetSiteAsync(HttpContext.RequestAborted));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (site, error) = Read();
        if (error is not null)
        {
            Error = error;
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

        await settings.SaveSiteAsync(site!, HttpContext.RequestAborted);
        await audit.RecordAsync("Settings.Update", "Settings", "site", new { site!.SiteName, webhook = site.WebhookUrl is not null }, HttpContext.RequestAborted);
        return Redirect("/admin/settings?done=saved");
    }

    /// <summary>Sends a test message with the form's values (nothing is saved).</summary>
    public async Task<IActionResult> OnPostTestWebhookAsync()
    {
        var (site, error) = Read();
        if (error is null && string.IsNullOrWhiteSpace(site!.WebhookUrl))
        {
            error = "Enter a webhook address first.";
        }

        error ??= await health.SendWebhookAsync(site!, null, HealthStatus.OK,
            [new HealthCheck("Test", HealthStatus.OK, "This is a test message from RecipeWarden.")], HttpContext.RequestAborted);
        if (error is null)
        {
            Message = "Test message sent.";
        }
        else
        {
            Error = "The test message was not delivered: " + error;
            Response.StatusCode = StatusCodes.Status400BadRequest;
        }

        return Page();
    }

    private (SiteSettings? Site, string? Error) Read()
    {
        if (!ModelState.IsValid)
        {
            return (null, "Check the values: percentages 0–99 and sizes of 0 or more.");
        }

        var thresholds = new DiskThresholds(Input.WarningPercent, (long)(Input.WarningGb * GB), Input.CriticalPercent, (long)(Input.CriticalMb * MB));
        if (thresholds.Validate() is { } bad)
        {
            return (null, bad);
        }

        var url = string.IsNullOrWhiteSpace(Input.WebhookUrl) ? null : Input.WebhookUrl.Trim();
        if (url is not null && OutboundEndpointRules.Parse(url, Input.WebhookAllowLan).Error is { } urlError)
        {
            return (null, "Webhook: " + urlError + (Input.WebhookAllowLan ? string.Empty : " (tick \"on my home network\" for a LAN address such as ntfy on 192.168.1.x)"));
        }

        var name = string.IsNullOrWhiteSpace(Input.SiteName) ? SiteSettings.DefaultSiteName : Input.SiteName.Trim();
        return (new SiteSettings(name, thresholds, url, Input.WebhookFormat, Input.WebhookAllowLan), null);
    }

    private void Fill(SiteSettings site)
    {
        Input = new SettingsInput
        {
            SiteName = site.SiteName,
            WarningPercent = site.Thresholds.WarningPercent,
            WarningGb = Math.Round((decimal)site.Thresholds.WarningBytes / GB, 2),
            CriticalPercent = site.Thresholds.CriticalPercent,
            CriticalMb = Math.Round((decimal)site.Thresholds.CriticalBytes / MB, 0),
            WebhookUrl = site.WebhookUrl,
            WebhookFormat = site.WebhookFormat,
            WebhookAllowLan = site.WebhookAllowLan,
        };
    }

    private static RateLimitOptions Bind(IConfiguration configuration)
    {
        var o = new RateLimitOptions();
        configuration.GetSection(RateLimitOptions.SectionName).Bind(o);
        return o;
    }
}
