using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Web.Hosting;

/// <summary>
/// BR-05: purges recipes 30 days after they were moved to the trash. Also removes import jobs and raw payloads after
/// 30 days (FR-IMP-08) and images nothing uses (ADR 0022). Runs at start and then every 6 hours.
/// </summary>
internal sealed partial class TrashPurgeService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TrashPurgeService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var purged = await scope.ServiceProvider.GetRequiredService<RecipeService>().PurgeDueAsync(cancellationToken).ConfigureAwait(false);
            if (purged > 0)
            {
                LogPurged(logger, purged);
            }

            await scope.ServiceProvider.GetRequiredService<ImportService>().PurgeAsync(cancellationToken).ConfigureAwait(false);
            await scope.ServiceProvider.GetRequiredService<ImageStore>().PurgeOrphansAsync(cancellationToken).ConfigureAwait(false);

            return purged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed run must not stop the host; the next run tries again.
            LogFailed(logger, ex);
            return 0;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Count} recipe(s) from the trash after 30 days")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Trash purge failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
