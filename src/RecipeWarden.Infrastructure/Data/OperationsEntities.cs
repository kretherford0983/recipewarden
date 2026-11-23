using RecipeWarden.Core.Backup;
using RecipeWarden.Core.Health;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 8.5, FR-ADM-01, ADR 0041: one write, append-only. Details hold changed property names, never values.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string? UserId { get; set; }

    /// <summary>The account email (or "system", "cli:&lt;os user&gt;") at the time, kept when the account is deleted.</summary>
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Ip { get; set; }
    public string? DetailsJson { get; set; }
}

/// <summary>Section 8.5: the result of a backup run (FR-BKP-05). Retention reads only its own rows (BR-61).</summary>
public sealed class BackupRecord
{
    public int Id { get; set; }
    public BackupKind Kind { get; set; }
    public int? ScheduleId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The allowed folder the archive was written to (SEC-74).</summary>
    public string DestinationFolder { get; set; } = string.Empty;

    /// <summary>The archive's file name inside <see cref="DestinationFolder"/> (null when the run failed before writing).</summary>
    public string? FileName { get; set; }
    public long Bytes { get; set; }
    public string? Sha256 { get; set; }
    public bool Encrypted { get; set; }
    public string? KeyFingerprint { get; set; }
    public BackupStatus Status { get; set; }
    public string? Error { get; set; }
    public string StartedBy { get; set; } = string.Empty;

    /// <summary>Set when retention or an Admin deleted the file; the row stays as history.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public string? FullPath => FileName is null ? null : Path.Combine(DestinationFolder, FileName);
}

public enum BackupStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>Section 8.5, FR-BKP-02: one schedule writing to one allowed folder.</summary>
public sealed class BackupSchedule
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Cron { get; set; } = string.Empty;

    /// <summary>SEC-74: the normalized matching entry of Backup:AllowedFolders, checked again at run time.</summary>
    public string DestinationFolder { get; set; } = string.Empty;
    public int KeepLast { get; set; }
    public int KeepDaily { get; set; }
    public int KeepWeekly { get; set; }
    public int KeepMonthly { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? NextRunAt { get; set; }
    public int RetryAttempt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public RetentionPolicy Retention => new(KeepLast, KeepDaily, KeepWeekly, KeepMonthly);

    public ScheduleTiming Timing => new(LastRunAt, NextRunAt, RetryAttempt, NextRetryAt);

    public void Apply(ScheduleTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        LastRunAt = timing.LastRunAt;
        NextRunAt = timing.NextRunAt;
        RetryAttempt = timing.RetryAttempt;
        NextRetryAt = timing.NextRetryAt;
    }
}

/// <summary>Section 8.5: settings changed on /admin/settings and /admin/backups (key/value).</summary>
public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Section 8.5, FR-OPS-03: one per day, kept 400 days; drives the growth chart and projection.</summary>
public sealed class StorageSnapshot
{
    public int Id { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public long DbBytes { get; set; }
    public long WalBytes { get; set; }
    public long FreePages { get; set; }
    public long MediaBytes { get; set; }
    public long ImportBytes { get; set; }
    public long BackupBytes { get; set; }
    public long LogBytes { get; set; }
    public long DataVolumeTotal { get; set; }
    public long DataVolumeFree { get; set; }
    public long BackupVolumeTotal { get; set; }
    public long BackupVolumeFree { get; set; }
}

/// <summary>Section 8.5: one evaluated check; the newest per name is shown, 90 days kept.</summary>
public sealed class HealthCheckResult
{
    public int Id { get; set; }
    public string CheckName { get; set; } = string.Empty;
    public DateTimeOffset RanAt { get; set; }
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int DurationMs { get; set; }
}

/// <summary>FR-IMP-10: one AI call — provider, model, tokens, time and result. Never the key, the text or the reply.</summary>
public sealed class AiCallLog
{
    public int Id { get; set; }
    public DateTimeOffset At { get; set; }
    public Core.AiParser.AiPurpose Purpose { get; set; }
    public Core.AiParser.AiProvider Provider { get; set; }
    public string Model { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public int DurationMs { get; set; }
    public Core.AiParser.AiCallResult Result { get; set; }
    public string? Error { get; set; }
    public string? UserId { get; set; }
}
