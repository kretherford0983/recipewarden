namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 9.5 / SEC-71: where the app keeps its data. Bound from the "Data" config section.</summary>
public sealed class DataOptions
{
    public const string SectionName = "Data";

    /// <summary>Root data directory, e.g. /var/lib/recipewarden.</summary>
    public string Directory { get; set; } = "/var/lib/recipewarden";

    public string DatabasePath => Path.Combine(Directory, "db", "recipewarden.db");

    public string KeysDirectory => Path.Combine(Directory, "keys");

    /// <summary>ADR 0022: re-encoded images, outside the web root.</summary>
    public string MediaDirectory => Path.Combine(Directory, "media");

    /// <summary>ADR 0024, FR-IMP-08: raw import payloads, kept 30 days.</summary>
    public string ImportsDirectory => Path.Combine(Directory, "imports");

    /// <summary>Section 9.5: the built-in local backup folder, always allowed (FR-BKP-08).</summary>
    public string BackupsDirectory => Path.Combine(Directory, "backups");

    /// <summary>Work space for backups and restores, on the data volume (the service has a private /tmp).</summary>
    public string WorkDirectory => Path.Combine(Directory, "tmp");

    /// <summary>SEC-15: the setup token hash and the "setup complete" marker.</summary>
    public string SetupDirectory => Path.Combine(Directory, "setup");

    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True;Pooling=True";
}
