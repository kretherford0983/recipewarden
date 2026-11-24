namespace RecipeWarden.Web.Tests;

/// <summary>ADR 0051: the repository's development worker, and fake workers, for web test hosts.</summary>
internal static class PythonWebTestKit
{
    public static string RepoRoot { get; } = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RecipeWarden.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    /// <summary>Points a host at python/.venv (built by build/python.sh dev; never skipped when missing).</summary>
    public static void UseRealWorker(RecipeWardenFactory host)
    {
        var venv = Path.Combine(RepoRoot, "python", ".venv");
        File.Exists(Path.Combine(venv, "bin", "python3")).ShouldBeTrue("no Python environment: run build/python.sh dev");
        host.Settings["Python:Directory"] = venv;
        host.Settings["Python:NltkData"] = Path.Combine(RepoRoot, "python", ".nltk_data");
    }

    /// <summary>A fake worker (system python3, isolated) whose <c>main()</c> body gets command, req and answer().</summary>
    public static void UseFakeWorker(RecipeWardenFactory host, string body)
    {
        var work = Directory.CreateTempSubdirectory("rw-web-fake-worker-").FullName;
        var script = Path.Combine(work, "worker.py");
        var indented = string.Join('\n', body.Split('\n').Select(l => "    " + l));
        File.WriteAllText(script, $$$"""
            #!/usr/bin/python3 -I
            import json, sys
            command = sys.argv[1]
            req = json.loads(sys.stdin.buffer.read() or b"{}")
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

        host.Settings["Python:Executable"] = script;
    }
}
