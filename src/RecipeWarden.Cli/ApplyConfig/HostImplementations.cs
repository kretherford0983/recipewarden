using System.Diagnostics;
using RecipeWarden.Core.Backup;

namespace RecipeWarden.Cli.ApplyConfig;

internal sealed class HostFileSystem : IHostFileSystem
{
    public PathKind Probe(string path) =>
        Directory.Exists(path) ? PathKind.Directory : File.Exists(path) ? PathKind.File : PathKind.Missing;

    public string? ReadAllTextOrNull(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public void WriteAllTextAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "." + Path.GetFileName(path) + ".tmp");
        File.WriteAllText(temp, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

internal sealed class Systemctl(string executable = "systemctl") : ISystemd
{
    public Task<bool> DaemonReloadAsync(CancellationToken cancellationToken) => RunAsync(["daemon-reload"], cancellationToken);

    public Task<bool> RestartAsync(string unit, CancellationToken cancellationToken) => RunAsync(["restart", unit], cancellationToken);

    public Task<bool> StopAsync(string unit, CancellationToken cancellationToken) => RunAsync(["stop", unit], cancellationToken);

    public Task<bool> StartAsync(string unit, CancellationToken cancellationToken) => RunAsync(["start", unit], cancellationToken);

    private async Task<bool> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a); // no shell, no interpolation (SEC-24)
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // systemctl not found
        }
    }
}

/// <summary>Polls the service's own /health on the configured local address (read from the root-owned config).</summary>
internal sealed class HttpHealthProbe(Uri healthUri, TimeProvider clock) : IServiceHealthProbe
{
    public async Task<bool> WaitHealthyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = clock.GetUtcNow() + timeout;
        while (clock.GetUtcNow() < deadline)
        {
            try
            {
                var body = await http.GetStringAsync(healthUri, cancellationToken);
                if (string.Equals(body.Trim(), "ok", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // not up yet
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // per-request timeout
            }

            await Task.Delay(TimeSpan.FromSeconds(1), clock, cancellationToken);
        }

        return false;
    }
}
