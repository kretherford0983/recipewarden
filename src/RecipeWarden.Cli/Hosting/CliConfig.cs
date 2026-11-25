using Microsoft.Extensions.Configuration;

namespace RecipeWarden.Cli.Hosting;

/// <summary>Loads the server config file the service uses (/etc/recipewarden/appsettings.Production.json).</summary>
internal static class CliConfig
{
    public const string ConfigPathVariable = "RECIPEWARDEN_CONFIG";
    public const string DefaultConfigPath = "/etc/recipewarden/appsettings.Production.json";

    public static string ResolvePath(string? explicitPath) =>
        explicitPath ?? Environment.GetEnvironmentVariable(ConfigPathVariable) ?? DefaultConfigPath;

    /// <summary>The main config plus the installer-owned network.json beside it (ADR 0007).</summary>
    public static IConfiguration Load(string path)
    {
        var full = Path.GetFullPath(path);
        return new ConfigurationBuilder()
            .AddJsonFile(full, optional: false, reloadOnChange: false)
            .AddJsonFile(Path.Combine(Path.GetDirectoryName(full) ?? "/", "network.json"), optional: true, reloadOnChange: false)
            .Build();
    }

    /// <summary>The local URL the service listens on, used to poll /health after a restart.</summary>
    public static Uri HealthUri(IConfiguration config)
    {
        var url = config["Kestrel:Endpoints:Http:Url"] ?? "http://127.0.0.1:5080";
        var builder = new UriBuilder(url.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)) { Path = "/health" };
        return builder.Uri;
    }
}
