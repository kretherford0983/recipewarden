using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeWarden.Infrastructure.Python;

namespace RecipeWarden.Cli.Python;

/// <summary>
/// FR-OPS-06, AC-106, AC-107: "recipewarden python-selftest" runs the bundled worker's own offline check and prints what
/// it found. Exit 0 when it passed, 1 otherwise. Reads only the config's "Python" section; touches no data.
/// </summary>
internal static class PythonSelftestCommand
{
    public static async Task<int> RunAsync(IConfiguration config, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var options = new PythonOptions();
        config.GetSection(PythonOptions.SectionName).Bind(options);
        if (options.Resolve() is not { } runtime)
        {
            await error.WriteLineAsync("error: no Python runtime was found (expected python/ next to cli/ in the release).");
            return 1;
        }

        await output.WriteLineAsync("Python worker: " + runtime.Program);
        if (!options.Enabled)
        {
            await output.WriteLineAsync("Note: Python:Enabled is false in the config, so the app does not use it.");
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var worker = new PythonWorker(options, new PythonWorkerHealth(TimeProvider.System), NullLogger<PythonWorker>.Instance);
        var call = await worker.SelftestAsync(cancellationToken);
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        if (call.Engine is { } engine)
        {
            await output.WriteLineAsync($"Engine: worker {engine.Worker}, Python {engine.Python}, {engine.Describe()}");
        }

        if (call.Result is { } result)
        {
            var c = result.Checks;
            await output.WriteLineAsync($"Checks: network blocked {Yes(c.NetworkBlocked)}, parse {Yes(c.Parse)} ({c.ParseMs} ms), "
                                        + $"USDA match {Yes(c.Fdc)}, scrape {Yes(c.Scrape)} ({c.ScrapeMs} ms)");
        }

        if (call.Ok)
        {
            await output.WriteLineAsync($"Self-test: passed in {seconds} s");
            return 0;
        }

        await error.WriteLineAsync($"Self-test: FAILED in {seconds} s ({call.Failure}: {call.Detail})");
        return 1;
    }

    private static string Yes(bool value) => value ? "yes" : "NO";
}
