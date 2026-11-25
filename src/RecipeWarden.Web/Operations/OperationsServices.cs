using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Health;

namespace RecipeWarden.Web.Operations;

/// <summary>
/// FR-BKP-02, FR-BKP-14, section 9.6: evaluates the backup schedules every minute. At start it closes runs a stopped
/// process left "running", keeps the built-in folder's marker, and runs anything missed while stopped. Skips while a
/// restore runs; one backup at a time.
/// </summary>
internal sealed partial class BackupSchedulerService(IServiceScopeFactory scopes, BackupGate gate, MaintenanceMode maintenance, BackupFolders folders,
    TimeProvider clock, ILogger<BackupSchedulerService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            folders.EnsureLocalMarker();
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BackupScheduleService>().CloseInterruptedAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
        }

        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (maintenance.IsActive)
        {
            return;
        }

        try
        {
            using var held = await gate.TryEnterAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            if (held is null)
            {
                return; // a manual backup or a restore is running; try again next minute
            }

            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<BackupScheduleService>().RunDueAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex); // never stops the host; the next minute tries again
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Backup scheduler run failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Section 9.6 health monitor: evaluates the checks every 15 minutes (posting to the webhook on a status change),
/// takes the daily storage snapshot, and runs quick_check daily and integrity_check weekly (BR-83).
/// </summary>
internal sealed partial class HealthMonitorService(IServiceScopeFactory scopes, MaintenanceMode maintenance, HealthOptions options,
    TimeProvider clock, ILogger<HealthMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.IntervalMinutes)), clock);
        do
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (maintenance.IsActive)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var health = scope.ServiceProvider.GetRequiredService<HealthService>();
            var (quick, full) = await health.ChecksDueAsync(cancellationToken).ConfigureAwait(false);
            if (quick || full)
            {
                await health.RunIntegrityCheckAsync(full, cancellationToken).ConfigureAwait(false);
            }

            await health.TakeDailySnapshotAsync(cancellationToken).ConfigureAwait(false);
            await health.EvaluateAsync(store: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Health monitor run failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
