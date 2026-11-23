using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Backup;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Import;

namespace RecipeWarden.Web.Pages.Admin;

/// <summary>
/// W7, FR-BKP-01 to 05, 10, 11, 15, BR-61 to 63, SEC-54, SEC-74, SEC-75: backups, schedules, retention, the encryption
/// key and restore. Folders are offered from Backup:AllowedFolders only and the saved value is the config entry. A
/// restore is two requests: the archive is checked in a quarantine folder and a report is shown; typing RESTORE then
/// applies it under maintenance mode after a pre-restore backup.
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public sealed class BackupsModel(
    AppDbContext db,
    BackupService backups,
    BackupScheduleService schedules,
    BackupKeyService keys,
    BackupFolders folders,
    BackupOptions options,
    RestoreService restore,
    BackupGate gate,
    MaintenanceMode maintenance,
    DataOptions data) : PageModel
{
    /// <summary>Kestrel's cap for this page; the configured upload cap (Backup:MaxUploadBytes, 100 MB) is checked as the file streams.</summary>
    public const long MaxRequestBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxKitBytes = 64 * 1024;
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(5);

    public BackupKeyStatus Key { get; private set; } = new(false, null, null);
    public IReadOnlyList<string> Folders { get; private set; } = [];
    public IReadOnlyList<BackupSchedule> Schedules { get; private set; } = [];
    public IReadOnlyList<BackupRecord> Records { get; private set; } = [];
    public string LocalFolder => folders.Local;
    public long MaxUploadBytes => options.MaxUploadBytes;
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public BackupSchedule? Editing { get; private set; }
    public PreparedRestore? Prepared { get; private set; }

    public async Task OnGetAsync(string? done, int? edit)
    {
        Message = done switch
        {
            "backup" => "Backup finished.",
            "schedule" => "Schedule saved.",
            "schedule-deleted" => "Schedule deleted. Its archives stay until you delete them.",
            "archive-deleted" => "Archive deleted.",
            "restored" => "Restore finished. The site is back in service; if your account was not in the backup, sign in with an account from it.",
            _ => null,
        };
        await LoadAsync(edit);
    }

    public async Task<IActionResult> OnPostCreateKeyAsync(string? passphrase, string? confirm, bool replace)
    {
        var (kit, fingerprint, error) = await keys.CreateAsync(passphrase, confirm, replace, HttpContext.RequestAborted);
        if (error is not null)
        {
            return await FailAsync(error);
        }

        // FR-BKP-15: the kit is given once, as a download, and not kept.
        Response.Headers.CacheControl = "no-store";
        return File(Encoding.UTF8.GetBytes(kit!), "text/plain; charset=utf-8", BackupKeyService.KitFileName(fingerprint!));
    }

    public async Task<IActionResult> OnPostBackupNowAsync(string? folder)
    {
        var target = folders.Resolve(folder ?? folders.Local);
        if (target is null)
        {
            return await FailAsync("Choose one of the allowed backup folders.");
        }

        using var held = await gate.TryEnterAsync(GateWait, HttpContext.RequestAborted);
        if (held is null)
        {
            return await FailAsync("Another backup or a restore is running. Try again in a minute.");
        }

        var outcome = await backups.RunAsync(new BackupRequest(BackupKind.Manual, target), HttpContext.RequestAborted);
        return outcome.Ok ? Redirect("/admin/backups?done=backup") : await FailAsync("The backup failed: " + outcome.Error);
    }

    public async Task<IActionResult> OnPostTestAsync(string? folder)
    {
        var target = folders.Resolve(folder);
        if (target is null)
        {
            return await FailAsync("Choose one of the allowed backup folders.");
        }

        var check = folders.Test(target);
        if (!check.Ok)
        {
            return await FailAsync(target + ": " + check.Message);
        }

        Message = target + ": " + check.Message;
        await LoadAsync(null);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveScheduleAsync(int? id, string? name, ScheduleFrequency frequency, string? time, DayOfWeek day, string? cron,
        string? folder, int keepLast, int keepDaily, int keepWeekly, int keepMonthly, bool enabled)
    {
        var at = TimeOnly.TryParse(time, CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(2, 30);
        var saved = await schedules.SaveAsync(id, new ScheduleEdit(name, frequency, at, day, cron, folder,
            new RetentionPolicy(keepLast, keepDaily, keepWeekly, keepMonthly), enabled), HttpContext.RequestAborted);
        return saved.Error is null ? Redirect("/admin/backups?done=schedule") : await FailAsync(saved.Error, id);
    }

    public async Task<IActionResult> OnPostDeleteScheduleAsync(int id)
    {
        await schedules.DeleteAsync(id, HttpContext.RequestAborted);
        return Redirect("/admin/backups?done=schedule-deleted");
    }

    public async Task<IActionResult> OnPostRunScheduleAsync(int id)
    {
        using var held = await gate.TryEnterAsync(GateWait, HttpContext.RequestAborted);
        if (held is null)
        {
            return await FailAsync("Another backup or a restore is running. Try again in a minute.");
        }

        var outcome = await schedules.RunNowAsync(id, HttpContext.RequestAborted);
        return outcome is null ? NotFound()
            : outcome.Ok ? Redirect("/admin/backups?done=backup")
            : await FailAsync("The backup failed: " + outcome.Error);
    }

    /// <summary>Downloads a listed archive, by its record only (never a path from the request).</summary>
    public async Task<IActionResult> OnGetDownloadAsync(int id)
    {
        var record = await db.BackupRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null && r.FileName != null, HttpContext.RequestAborted);
        if (record?.FullPath is not { } path || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, "application/octet-stream", record.FileName);
    }

    public async Task<IActionResult> OnPostDeleteArchiveAsync(int id)
    {
        var error = await backups.DeleteArchiveAsync(id, HttpContext.RequestAborted);
        return error is null ? Redirect("/admin/backups?done=archive-deleted") : await FailAsync(error);
    }

    /// <summary>W7 step 1: check a listed or uploaded archive and show the validation report. Nothing changes.</summary>
    public async Task<IActionResult> OnPostPrepareRestoreAsync(int? recordId, IFormFile? archive, IFormFile? kitFile, string? kitText, string? passphrase)
    {
        string? path = null;
        string? uploaded = null;
        try
        {
            if (archive is { Length: > 0 })
            {
                uploaded = Path.Combine(data.WorkDirectory, "upload-" + Guid.NewGuid().ToString("N") + ".bin");
                await using var stream = archive.OpenReadStream();
                var error = await UploadValidator.SaveArchiveAsync(stream, archive.FileName, archive.Length, ArchiveUploadKind.Backup, options.MaxUploadBytes,
                    uploaded, HttpContext.RequestAborted);
                if (error is not null)
                {
                    return await FailAsync(error);
                }

                path = uploaded;
            }
            else if (recordId is { } id)
            {
                var record = await db.BackupRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null && r.FileName != null, HttpContext.RequestAborted);
                path = record?.FullPath;
            }

            if (path is null || !System.IO.File.Exists(path))
            {
                return await FailAsync("Choose a listed archive or upload one.");
            }

            var kit = kitText;
            if (kitFile is { Length: > 0 })
            {
                if (kitFile.Length > MaxKitBytes)
                {
                    return await FailAsync("That file is too large to be a recovery kit.");
                }

                using var reader = new StreamReader(kitFile.OpenReadStream());
                kit = await reader.ReadToEndAsync(HttpContext.RequestAborted);
            }

            var key = string.IsNullOrWhiteSpace(kit) ? null : new RecoveryKey(kit, passphrase ?? string.Empty);
            var prepared = await restore.PrepareAsync(path, key, cancellationToken: HttpContext.RequestAborted);
            if (prepared.Error is not null)
            {
                return await FailAsync("Restore refused: " + prepared.Error);
            }

            Prepared = prepared.Prepared;
            await LoadAsync(null);
            return Page();
        }
        finally
        {
            if (uploaded is not null)
            {
                BackupService.TryDelete(uploaded);
            }
        }
    }

    /// <summary>W7 step 2: the typed confirmation word, then maintenance mode, pre-restore backup, restore, migrations.</summary>
    public async Task<IActionResult> OnPostConfirmRestoreAsync(string? restoreId, string? confirmation)
    {
        var reopened = await restore.ReopenAsync(restoreId, HttpContext.RequestAborted);
        if (reopened.Error is not null)
        {
            return await FailAsync(reopened.Error);
        }

        var prepared = reopened.Prepared!;
        if (!RestoreRules.IsConfirmed(confirmation))
        {
            Error = $"Type {RestoreRules.ConfirmationWord} to confirm.";
            Prepared = prepared;
            await LoadAsync(null);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return Page();
        }

        // A wrong confirmation word keeps the checked archive waiting; from here on it is used once.
        using var done = prepared;
        using var held = await gate.TryEnterAsync(GateWait, HttpContext.RequestAborted);
        if (held is null)
        {
            return await FailAsync("Another backup or a restore is running. Try again in a minute.");
        }

        string? error;
        using (maintenance.Enter("restore"))
        {
            error = await restore.ApplyAsync(prepared, new RestoreOptions(PreRestoreBackup: true, Origin: "web"), HttpContext.RequestAborted);
        }

        return error is null ? Redirect("/admin/backups?done=restored") : await FailAsync(error);
    }

    public string Describe(BackupSchedule s) => BackupScheduleRules.Describe(s.Cron) + " → " + s.DestinationFolder;

    private async Task<IActionResult> FailAsync(string error, int? edit = null)
    {
        Error = error;
        await LoadAsync(edit);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return Page();
    }

    private async Task LoadAsync(int? edit)
    {
        Key = await keys.StatusAsync(HttpContext.RequestAborted);
        Folders = folders.Allowed();
        Schedules = await schedules.ListAsync(HttpContext.RequestAborted);
        Records = await db.BackupRecords.AsNoTracking().OrderByDescending(r => r.Id).Take(100).ToListAsync(HttpContext.RequestAborted);
        Editing = edit is { } id ? Schedules.FirstOrDefault(s => s.Id == id) : null;
    }
}
