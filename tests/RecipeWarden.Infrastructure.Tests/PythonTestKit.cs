using Microsoft.Extensions.Logging;
using RecipeWarden.Infrastructure.Python;
using RecipeWarden.Infrastructure.Settings;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0051 test helpers: the repository root, fake workers for fault cases, and a logger that keeps lines.</summary>
internal static class PythonTestKit
{
    public static string RepoRoot { get; } = FindRoot();

    public static string Contracts => Path.Combine(RepoRoot, "tests", "contracts");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RecipeWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    /// <summary>
    /// A fake worker: a Python script (system python3, isolated) whose body gets <c>command</c> (argv[1]),
    /// <c>request</c> (stdin bytes) and <c>work</c> (a folder for this fake) and writes its answer to stdout.
    /// </summary>
    public static FakeWorker Fake(string body)
    {
        var work = Directory.CreateTempSubdirectory("rw-fake-worker-").FullName;
        var script = Path.Combine(work, "worker.py");
        var indented = string.Join('\n', body.Split('\n').Select(l => "    " + l));
        File.WriteAllText(script, $$$"""
            #!/usr/bin/python3 -I
            import json, os, sys, time, subprocess
            command = sys.argv[1]
            request = sys.stdin.buffer.read()
            work = {{{System.Text.Json.JsonSerializer.Serialize(work)}}}
            ENGINE = {"worker": "1.0.0", "python": "3.13.0", "libs": {"ingredient-parser-nlp": "2.10.0", "recipe-scrapers": "15.14.0"}}
            def answer(result):
                sys.stdout.write(json.dumps({"v": 1, "ok": True, "engine": ENGINE, "result": result}))
            def main():
            {{{indented}}}
            main()
            """.Replace("\r", string.Empty, StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return new FakeWorker(script, work);
    }

    public static (PythonWorker Worker, PythonWorkerHealth Health) Worker(PythonOptions options, ILogger<PythonWorker>? logger = null)
    {
        var health = new PythonWorkerHealth(TimeProvider.System);
        return (new PythonWorker(options, health, logger ?? new ListLogger<PythonWorker>()), health);
    }

    public static PythonFeatures Features(PythonOptions options, PythonWorkerHealth health, SettingsStore settings, ListLogger<PythonFeatures>? logger = null) =>
        new(options, health, settings, logger ?? new ListLogger<PythonFeatures>());

    /// <summary>True while the process runs. A zombie (killed, not yet reaped by its new parent) has stopped running.</summary>
    public static bool ProcessExists(int pid)
    {
        var stat = "/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/stat";
        try
        {
            var text = File.ReadAllText(stat);
            var state = text[(text.LastIndexOf(')') + 2)..].Split(' ')[0];
            return state is not ("Z" or "X");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return false;
        }
    }
}

internal sealed record FakeWorker(string Script, string Work)
{
    public PythonOptions Options(int timeoutSeconds = 20) => new() { Executable = Script, TimeoutSeconds = timeoutSeconds, NltkData = Work };
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Text)> lines = [];

    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (lines)
            {
                return [.. lines.Where(l => l.Level >= LogLevel.Warning).Select(l => l.Text)];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (lines)
        {
            lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
