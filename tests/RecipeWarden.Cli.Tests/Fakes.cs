using RecipeWarden.Cli.ApplyConfig;
using RecipeWarden.Core.Backup;

namespace RecipeWarden.Cli.Tests;

internal sealed class FakeFileSystem : IHostFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, PathKind> Paths { get; } = new(StringComparer.Ordinal);

    public List<string> Writes { get; } = [];

    public PathKind Probe(string path) => Paths.TryGetValue(path, out var kind) ? kind : PathKind.Directory;

    public string? ReadAllTextOrNull(string path) => Files.TryGetValue(path, out var c) ? c : null;

    public void WriteAllTextAtomic(string path, string content)
    {
        Writes.Add(path);
        Files[path] = content;
    }

    public void Delete(string path)
    {
        Writes.Add("delete:" + path);
        Files.Remove(path);
    }
}

internal sealed class FakeSystemd : ISystemd
{
    public List<string> Calls { get; } = [];

    public bool ReloadSucceeds { get; set; } = true;

    public bool StartSucceeds { get; set; } = true;

    public Task<bool> StopAsync(string unit, CancellationToken cancellationToken)
    {
        Calls.Add("stop " + unit);
        return Task.FromResult(true);
    }

    public Task<bool> StartAsync(string unit, CancellationToken cancellationToken)
    {
        Calls.Add("start " + unit);
        return Task.FromResult(StartSucceeds);
    }

    public Task<bool> DaemonReloadAsync(CancellationToken cancellationToken)
    {
        Calls.Add("daemon-reload");
        return Task.FromResult(ReloadSucceeds);
    }

    public Task<bool> RestartAsync(string unit, CancellationToken cancellationToken)
    {
        Calls.Add("restart " + unit);
        return Task.FromResult(true);
    }
}

internal sealed class FakeHealth(params bool[] results) : IServiceHealthProbe
{
    private int call;

    public Task<bool> WaitHealthyAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromResult(call < results.Length ? results[call++] : true);
}
