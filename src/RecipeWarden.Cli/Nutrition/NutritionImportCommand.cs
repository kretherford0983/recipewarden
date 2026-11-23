using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RecipeWarden.Infrastructure;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Nutrition;

namespace RecipeWarden.Cli.Nutrition;

/// <summary>
/// FR-NUT-05, AC-58, ADR 0034: "recipewarden nutrition-import &lt;zip&gt;" applies a newer USDA FoodData Central CSV
/// download (SR Legacy or Foundation Foods) from a file on the server. Nothing is downloaded; mappings are kept.
/// The upload page comes with the admin area (M7).
/// </summary>
internal static class NutritionImportCommand
{
    public const long MaxFileBytes = 1024L * 1024 * 1024;

    public static async Task<int> RunAsync(IConfiguration config, string path, string? version, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            await error.WriteLineAsync($"error: {path} does not exist.");
            return 1;
        }

        if (file.Length > MaxFileBytes || !string.Equals(file.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            await error.WriteLineAsync("error: give a FoodData Central CSV .zip of at most 1 GB.");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<RecipeWarden.Infrastructure.Audit.IAuditActor>(new Operations.CliActor()); // FR-ADM-01: audited as cli:<os user>
        services.AddRecipeWardenData(config);
        services.AddRecipeWardenIdentityCore();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);

        await using var stream = file.OpenRead();
        var who = "cli:" + (Environment.GetEnvironmentVariable("SUDO_USER") ?? Environment.UserName);
        var result = await scope.ServiceProvider.GetRequiredService<NutrientDatasetImporter>()
            .ImportAsync(stream, string.IsNullOrWhiteSpace(version) ? Path.GetFileNameWithoutExtension(file.Name) : version, who, cancellationToken);
        if (!result.Ok)
        {
            await error.WriteLineAsync($"error: {result.Error}");
            return 1;
        }

        await output.WriteLineAsync($"Nutrient dataset \"{result.Version}\": {result.Foods:N0} foods ({result.Added:N0} new, {result.Updated:N0} updated, "
                                    + $"{result.Removed:N0} no longer in the data and flagged for re-mapping).");
        return 0;
    }
}
