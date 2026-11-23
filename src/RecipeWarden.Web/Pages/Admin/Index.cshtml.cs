using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Health;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>Section 7.2 /admin: backup status, disk, version and recent audit, with links to every admin page.</summary>
public sealed class IndexModel(HealthService health, AppDbContext db, AuditLog audit, BackupKeyService keys, Infrastructure.Ai.AiParserService ai) : PageModel
{
    public Core.AiParser.AiSettings Ai { get; private set; } = Core.AiParser.AiSettings.Off;

    /// <summary>FR-IMP-10: the monthly token total on the dashboard.</summary>
    public Infrastructure.Ai.AiUsage AiUsage { get; private set; } = new(0, 0, 0, null);

    public HealthReport Report { get; private set; } = null!;

    public BackupRecord? LastBackup { get; private set; }

    public DateTimeOffset? NextScheduled { get; private set; }

    public bool Encrypted { get; private set; }

    public IReadOnlyList<AuditEntry> Recent { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Report = await health.EvaluateAsync(store: false, ct);
        LastBackup = await db.BackupRecords.AsNoTracking().Where(r => r.Status != BackupStatus.Running).OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        NextScheduled = (await db.BackupSchedules.AsNoTracking().Where(s => s.IsEnabled && s.NextRunAt != null).Select(s => s.NextRunAt).ToListAsync(ct)).Min();
        Encrypted = (await keys.StatusAsync(ct)).HasKey;
        Recent = await audit.RecentAsync(10, ct);
        Ai = await ai.GetSettingsAsync(ct);
        AiUsage = await ai.UsageThisMonthAsync(ct);
    }
}
