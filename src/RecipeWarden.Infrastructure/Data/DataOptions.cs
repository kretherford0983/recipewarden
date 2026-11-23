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

    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True;Pooling=True";
}
