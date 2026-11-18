using System.Reflection;

namespace RecipeWarden.Web.Hosting;

/// <summary>The running build's version, e.g. "0.1.0" for a release or "0.1.0-dev.12" for a develop build.</summary>
public static class AppInfo
{
    public static string Version { get; } = FromAssembly(typeof(AppInfo).Assembly);

    /// <summary>Informational version without the "+commit" build metadata.</summary>
    internal static string FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? informational[..plus] : informational;
    }
}
