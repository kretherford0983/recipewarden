using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using RecipeWarden.Core.Backup;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Backup;

/// <summary>Backup settings from the server owner's config file (FR-BKP-08, section 9.6.1).</summary>
public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    /// <summary>Folders the app may write backups to. Only the server owner edits this list (then runs apply-config).</summary>
    public List<string> AllowedFolders { get; } = [];

    /// <summary>Config files copied into archives for reference (never applied on restore, ADR 0040).</summary>
    public List<string> ConfigFiles { get; } = [];

    /// <summary>Largest archive accepted by the upload form (Cloudflare's free plan stops uploads at 100 MB).</summary>
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;

    public static BackupOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var o = new BackupOptions();
        foreach (var c in configuration.GetSection(SectionName + ":AllowedFolders").GetChildren())
        {
            if (c.Value is not null)
            {
                o.AllowedFolders.Add(c.Value);
            }
        }

        if (long.TryParse(configuration[SectionName + ":MaxUploadBytes"], out var max) && max > 0)
        {
            o.MaxUploadBytes = max;
        }

        var configPath = Environment.GetEnvironmentVariable("RECIPEWARDEN_CONFIG");
        if (!string.IsNullOrEmpty(configPath))
        {
            o.ConfigFiles.Add(configPath);
            o.ConfigFiles.Add(Path.Combine(Path.GetDirectoryName(configPath) ?? "/", "network.json"));
        }

        return o;
    }
}

/// <summary>The result of checking or testing a folder.</summary>
public sealed record FolderCheck(string Folder, FolderProblem Problem, string Message)
{
    public bool Ok => Problem == FolderProblem.None;
}

/// <summary>
/// FR-BKP-08, 10 to 12, SEC-74: the folders backups may go to (the built-in local folder plus Backup:AllowedFolders),
/// the mount marker, and the Test button's write-and-delete probe.
/// </summary>
public sealed class BackupFolders(BackupOptions options, DataOptions data)
{
    /// <summary>The built-in local folder first, then every valid configured folder, normalized and without duplicates.</summary>
    public IReadOnlyList<string> Allowed()
    {
        var local = BackupFolderPolicy.Normalize(data.BackupsDirectory);
        var configured = BackupFolderPolicy.Validate(options.AllowedFolders, Probe).Folders;
        return new[] { local }.Concat(configured.Where(f => f != local)).ToList();
    }

    public string Local => BackupFolderPolicy.Normalize(data.BackupsDirectory);

    /// <summary>SEC-74: the allowed entry matching the request, or null.</summary>
    public string? Resolve(string? requested) => BackupDestinationRules.Resolve(requested, Allowed());

    /// <summary>
    /// The built-in folder lives inside the data directory and cannot be "unmounted", so the app keeps its marker
    /// (ADR 0037). Every other folder's marker is created by the server owner.
    /// </summary>
    public void EnsureLocalMarker()
    {
        Directory.CreateDirectory(Local);
        var marker = Path.Combine(Local, BackupDestinationRules.MarkerFileName);
        if (!File.Exists(marker))
        {
            File.WriteAllText(marker, "RecipeWarden backup folder marker (FR-BKP-12). Do not delete.\n");
        }
    }

    /// <summary>FR-BKP-12: the marker must be there before every run and Test; otherwise nothing is written.</summary>
    public FolderCheck CheckMarker(string folder)
    {
        if (folder == Local)
        {
            try
            {
                EnsureLocalMarker();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(folder, ex);
            }
        }

        if (!Directory.Exists(folder))
        {
            return new FolderCheck(folder, FolderProblem.MarkerMissing, BackupDestinationRules.Describe(FolderProblem.MarkerMissing));
        }

        return File.Exists(Path.Combine(folder, BackupDestinationRules.MarkerFileName))
            ? new FolderCheck(folder, FolderProblem.None, BackupDestinationRules.Describe(FolderProblem.None))
            : new FolderCheck(folder, FolderProblem.MarkerMissing, BackupDestinationRules.Describe(FolderProblem.MarkerMissing));
    }

    /// <summary>FR-BKP-11: writes and deletes .recipewarden-write-test-&lt;random&gt;, reporting the OS error with a hint.</summary>
    public FolderCheck Test(string folder)
    {
        var marker = CheckMarker(folder);
        if (!marker.Ok)
        {
            return marker;
        }

        var probe = Path.Combine(folder, BackupDestinationRules.ProbePrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
        try
        {
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return new FolderCheck(folder, FolderProblem.None, BackupDestinationRules.Describe(FolderProblem.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(probe);
            return Fail(folder, ex);
        }
    }

    /// <summary>Maps an OS error to the problem and message shown to the Admin.</summary>
    public static FolderCheck Fail(string folder, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var problem = Classify(ex);
        return new FolderCheck(folder, problem, BackupDestinationRules.Describe(problem, ex.Message));
    }

    public static FolderProblem Classify(Exception ex) => ex switch
    {
        DirectoryNotFoundException => FolderProblem.NotFound,
        UnauthorizedAccessException when ex.Message.Contains("Read-only", StringComparison.OrdinalIgnoreCase) => FolderProblem.ReadOnly,
        UnauthorizedAccessException => FolderProblem.PermissionDenied,
        IOException when ex.Message.Contains("Read-only file system", StringComparison.OrdinalIgnoreCase) => FolderProblem.ReadOnly,
        IOException when ex.Message.Contains("No space left", StringComparison.OrdinalIgnoreCase) => FolderProblem.NoSpace,
        IOException when ex.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) => FolderProblem.PermissionDenied,
        _ => FolderProblem.Other,
    };

    private static PathKind Probe(string path) =>
        Directory.Exists(path) ? PathKind.Directory : File.Exists(path) ? PathKind.File : PathKind.Missing;

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nothing more to do; the error was already reported
        }
    }
}
