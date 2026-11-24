using RecipeWarden.Infrastructure.Python;

namespace RecipeWarden.Web.Hosting;

/// <summary>
/// FR-OPS-06: runs the Python worker's self-test once after start, so the health page shows its state and a broken
/// runtime is skipped by every feature (BR-119) instead of being tried on each request. Never blocks start-up.
/// </summary>
public sealed class PythonStartupCheck(PythonOptions options, PythonWorker worker) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Enabled)
        {
            await worker.SelftestAsync(stoppingToken).ConfigureAwait(false);
        }
    }
}
