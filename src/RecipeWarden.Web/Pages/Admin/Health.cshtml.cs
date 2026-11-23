using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Shopping;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>
/// FR-OPS-02 to 04, W9, AC-98: health and capacity. Opening the page evaluates the checks now; the 90-day growth chart
/// is an SVG drawn on the server (NFR-09: no scripts). Maintenance actions: integrity check, compact (VACUUM), purge
/// expired imports and trash; and the "move to a new host" guide.
/// </summary>
public sealed class HealthModel(HealthService health, AuditLog audit, IServiceProvider services, BackupGate gate) : PageModel
{
    public HealthReport Report { get; private set; } = null!;

    public string? Message { get; private set; }

    public string Version => Hosting.AppInfo.Version;

    public TimeSpan Uptime => DateTimeOffset.UtcNow - HealthService.StartedAt;

    public long MemoryBytes => GC.GetTotalMemory(false);

    public long WorkingSetBytes => Environment.WorkingSet;

    public async Task OnGetAsync(string? done)
    {
        Message = done;
        Report = await health.EvaluateAsync(store: false, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostIntegrityAsync()
    {
        var result = await health.RunIntegrityCheckAsync(full: true, HttpContext.RequestAborted);
        await audit.RecordAsync("Maintenance.IntegrityCheck", null, null, new { result }, HttpContext.RequestAborted);
        return Redirect("/admin/health?done=" + Uri.EscapeDataString(result == "ok" ? "Integrity check passed." : "Integrity check FAILED: " + result));
    }

    public async Task<IActionResult> OnPostCompactAsync()
    {
        using var held = await gate.TryEnterAsync(TimeSpan.FromSeconds(5), HttpContext.RequestAborted);
        if (held is null)
        {
            return Redirect("/admin/health?done=" + Uri.EscapeDataString("A backup is running; try again in a minute."));
        }

        var saved = await health.CompactAsync(HttpContext.RequestAborted);
        await audit.RecordAsync("Maintenance.Compact", null, null, new { reclaimedBytes = saved }, HttpContext.RequestAborted);
        return Redirect("/admin/health?done=" + Uri.EscapeDataString("Compacted; " + Core.Health.HealthRules.FormatBytes(saved) + " reclaimed."));
    }

    /// <summary>The same work as the 6-hourly purge (BR-05, FR-IMP-08, ADR 0022, ADR 0028), now.</summary>
    public async Task<IActionResult> OnPostPurgeAsync()
    {
        var ct = HttpContext.RequestAborted;
        var recipes = await services.GetRequiredService<RecipeService>().PurgeDueAsync(ct);
        await services.GetRequiredService<ImportService>().PurgeAsync(ct);
        await services.GetRequiredService<ImageStore>().PurgeOrphansAsync(ct);
        await services.GetRequiredService<ShoppingService>().PurgeFinishedAsync(ct);
        await audit.RecordAsync("Maintenance.Purge", null, null, new { recipes }, ct);
        return Redirect("/admin/health?done=" + Uri.EscapeDataString("Expired imports, trash and unused images were removed."));
    }

    /// <summary>The 90-day growth of used space on the data volume, as SVG polyline points (x 0–600, y 0–160).</summary>
    public string ChartPoints()
    {
        var points = Report.History;
        if (points.Count < 2)
        {
            return string.Empty;
        }

        var used = points.Select(p => (double)(p.DataVolumeTotal - p.DataVolumeFree)).ToList();
        var min = used.Min();
        var max = Math.Max(used.Max(), min + 1);
        var first = points[0].TakenAt;
        var span = Math.Max(1, (points[^1].TakenAt - first).TotalDays);
        return string.Join(' ', points.Select((p, i) => string.Create(CultureInfo.InvariantCulture,
            $"{(p.TakenAt - first).TotalDays / span * 600:0.#},{160 - ((used[i] - min) / (max - min) * 150):0.#}")));
    }

    public static string CssStatus(Core.Health.HealthStatus s) => "status-" + s.ToString().ToLowerInvariant();
}
