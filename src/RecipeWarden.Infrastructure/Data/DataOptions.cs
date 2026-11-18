namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 9.5 / SEC-71: where the app keeps its data. Bound from the "Data" config section.</summary>
public sealed class DataOptions
{
    public const string SectionName = "Data";

    /// <summary>Root data directory, e.g. /var/lib/recipewarden.</summary>
    public string Directory { get; set; } = "/var/lib/recipewarden";

    public string DatabasePath => Path.Combine(Directory, "db", "recipewarden.db");

    public string KeysDirectory => Path.Combine(Directory, "keys");

    public string ConnectionString => $"Data Source={DatabasePath};Foreign Keys=True;Pooling=True";
}
