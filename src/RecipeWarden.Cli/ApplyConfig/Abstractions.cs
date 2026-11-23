using RecipeWarden.Core.Backup;

namespace RecipeWarden.Cli.ApplyConfig;

/// <summary>The few file operations apply-config needs; faked in tests.</summary>
internal interface IHostFileSystem
{
    PathKind Probe(string path);

    string? ReadAllTextOrNull(string path);

    void WriteAllTextAtomic(string path, string content);

    void Delete(string path);
}

/// <summary>systemctl, invoked without a shell (SEC-24).</summary>
internal interface ISystemd
{
    Task<bool> DaemonReloadAsync(CancellationToken cancellationToken);

    Task<bool> RestartAsync(string unit, CancellationToken cancellationToken);

    Task<bool> StopAsync(string unit, CancellationToken cancellationToken);

    Task<bool> StartAsync(string unit, CancellationToken cancellationToken);
}

/// <summary>Waits for the service's /health to answer "ok".</summary>
internal interface IServiceHealthProbe
{
    Task<bool> WaitHealthyAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
