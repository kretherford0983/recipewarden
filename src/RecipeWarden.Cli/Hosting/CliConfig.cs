using Microsoft.Extensions.Configuration;

namespace RecipeWarden.Cli.Hosting;

/// <summary>Loads the server config file the service uses (/etc/recipewarden/appsettings.Production.json).</summary>
internal static class CliConfig
{
    public const string ConfigPathVariable = "RECIPEWARDEN_CONFIG";
    public const string DefaultConfigPath = "/etc/recipewarden/appsettings.Production.json";

    public static string ResolvePath(string? explicitPath) =>
        explicitPath ?? Environment.GetEnvironmentVariable(ConfigPathVariable) ?? DefaultConfigPath;

    public static IConfiguration Load(string path) =>
        new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(path), optional: false, reloadOnChange: false).Build();

    /// <summary>The local URL the service listens on, used to poll /health after a restart.</summary>
    public static Uri HealthUri(IConfiguration config)
    {
        var url = config["Kestrel:Endpoints:Http:Url"] ?? "http://127.0.0.1:5080";
        var builder = new UriBuilder(url.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)) { Path = "/health" };
        return builder.Uri;
    }
}
