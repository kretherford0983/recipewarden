namespace RecipeWarden.Infrastructure.Python;

/// <summary>
/// "Python" section (ADR 0051). Defaults find the bundled runtime next to <c>web/</c> and <c>cli/</c> in a release
/// (<c>/opt/recipewarden/&lt;ver&gt;/python</c>), or a development environment (<c>python/.venv</c>) above the build
/// output. <see cref="Enabled"/> is the server owner's switch; the Admin switch is the <c>Python.Enabled</c> setting.
/// </summary>
public sealed class PythonOptions
{
    public const string SectionName = "Python";

    /// <summary>SEC-92 limits.</summary>
    public const int MaxRequestBytes = 6 * 1024 * 1024;
    public const int MaxResponseBytes = 2 * 1024 * 1024;
    public const int MaxStderrChars = 16 * 1024;
    public const int MaxConcurrent = 2;

    public bool Enabled { get; set; } = true;

    /// <summary>The runtime folder (bin/python3 inside). Empty: found automatically.</summary>
    public string? Directory { get; set; }

    /// <summary>The NLTK model folder. Empty: <c>nltk_data</c> in the runtime, or <c>python/.nltk_data</c> in development.</summary>
    public string? NltkData { get; set; }

    /// <summary>
    /// Tests only: a program to run instead of <c>python3 -I -B -m rw_worker</c>; it gets the command as its one argument.
    /// </summary>
    public string? Executable { get; set; }

    public int TimeoutSeconds { get; set; } = 20;

    public int SelftestTimeoutSeconds { get; set; } = 30;

    /// <summary>The program and the fixed arguments before the command (SEC-91), or null when no runtime is found.</summary>
    public (string Program, IReadOnlyList<string> Arguments, string NltkData)? Resolve()
    {
        if (!string.IsNullOrWhiteSpace(Executable))
        {
            return (Executable, [], NltkData ?? string.Empty);
        }

        foreach (var (root, nltk) in Candidates())
        {
            var python = Path.Combine(root, "bin", "python3");
            if (File.Exists(python))
            {
                return (python, ["-I", "-B", "-m", "rw_worker"], NltkData ?? nltk);
            }
        }

        return null;
    }

    private IEnumerable<(string Root, string Nltk)> Candidates()
    {
        if (!string.IsNullOrWhiteSpace(Directory))
        {
            yield return (Directory, Path.Combine(Directory, "nltk_data"));
            yield break;
        }

        var release = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "python"));
        yield return (release, Path.Combine(release, "nltk_data"));

        // Development: the repository's python/.venv, from bin/Debug/net10.0 of any project.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var venv = Path.Combine(dir.FullName, "python", ".venv");
            if (System.IO.Directory.Exists(venv))
            {
                yield return (venv, Path.Combine(dir.FullName, "python", ".nltk_data"));
                yield break;
            }
        }
    }
}
