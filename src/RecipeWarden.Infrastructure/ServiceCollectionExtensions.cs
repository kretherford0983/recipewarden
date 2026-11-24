using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeWarden.Infrastructure.Audit;
using RecipeWarden.Infrastructure.Backup;
using RecipeWarden.Infrastructure.Health;
using RecipeWarden.Infrastructure.Settings;
using RecipeWarden.Infrastructure.Setup;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;
using RecipeWarden.Infrastructure.Import;
using RecipeWarden.Infrastructure.Kitchen;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Infrastructure.Shopping;

namespace RecipeWarden.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the SQLite context and data services shared by Web and CLI.</summary>
    public static IServiceCollection AddRecipeWardenData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var data = new DataOptions();
        configuration.GetSection(DataOptions.SectionName).Bind(data);
        services.AddSingleton(data);
        services.AddSingleton(TimeProvider.System);

        Directory.CreateDirectory(Path.GetDirectoryName(data.DatabasePath)!);

        // FR-ADM-01, ADR 0041: every write is audited by the interceptor (the actor comes from the request in Web).
        services.TryAddSingleton<IAuditActor, SystemAuditActor>();
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseSqlite(data.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor(), sp.GetRequiredService<AuditInterceptor>())
            // Child rows of a soft-deleted recipe are only reached through the recipe, which the filter hides.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<UserPreferenceStore>();
        services.AddScoped<TagService>();
        services.AddScoped<RecipeService>();
        services.AddScoped<RecipeSearchService>();
        services.AddScoped<CollectionService>();
        var home = new HomeOptions();
        configuration.GetSection(HomeOptions.SectionName).Bind(home);
        services.AddSingleton(home);
        services.AddScoped<RecommendationService>();
        services.AddScoped<FoodCatalogService>();
        services.AddScoped<MeasurementSeeder>();
        services.AddScoped<RecipeScaler>();
        services.AddScoped<KitchenService>();
        services.AddScoped<KitchenSeeder>();
        services.AddScoped<SuggestionService>();

        // M4 import and images (ADR 0022 to 0025): one fetcher for the process; every fetch goes through it.
        var import = new ImportOptions();
        configuration.GetSection(ImportOptions.SectionName).Bind(import);
        services.AddSingleton(import);
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton<ISocketConnector, TcpSocketConnector>();
        services.AddSingleton<SafeFetcher>();
        services.AddScoped<ImageStore>();
        services.AddScoped<ImportService>();
        services.AddScoped<ShoppingService>();
        services.AddScoped<ShoppingSeeder>();
        services.AddScoped<RecipeExporter>();
        services.AddScoped<AiExchange.AiExchangeService>();
        services.AddScoped<Nutrition.NutritionService>();
        services.AddScoped<Nutrition.NutrientDatasetImporter>();

        // M7 Operations (ADR 0037 to 0041)
        services.AddScoped<AuditLog>();
        services.AddScoped<SettingsStore>();
        services.AddSingleton(BackupOptions.From(configuration));
        services.AddSingleton<BackupFolders>();
        services.AddSingleton<BackupGate>();
        services.AddSingleton<MaintenanceMode>();
        services.AddScoped<BackupService>();
        services.AddScoped<BackupKeyService>();
        services.AddScoped<BackupScheduleService>();
        services.AddScoped<RestoreService>();
        services.AddScoped<SetupService>();
        services.AddSingleton<IVolumeProbe, DriveInfoVolumeProbe>();
        var health = new HealthOptions();
        configuration.GetSection(HealthOptions.SectionName).Bind(health);
        services.AddSingleton(health);
        services.AddSingleton<HealthState>();
        services.AddScoped<HealthService>();

        // M8: the optional AI parser (ADR 0042). Its API key is protected with the app's Data Protection key ring
        // (SEC-45), kept in the data directory for Web and CLI alike (SEC-71, ADR 0038).
        services.AddDataProtection()
            .SetApplicationName("RecipeWarden")
            .PersistKeysToFileSystem(new DirectoryInfo(data.KeysDirectory));
        var ai = new Ai.AiOptions();
        configuration.GetSection(Ai.AiOptions.SectionName).Bind(ai);
        services.AddSingleton(ai);
        services.AddScoped<Ai.AiParserService>();

        // M9: the Python worker (ADR 0051). One runner and one health record per process; adapters behind Core interfaces.
        var python = new Python.PythonOptions();
        configuration.GetSection(Python.PythonOptions.SectionName).Bind(python);
        services.AddSingleton(python);
        services.AddSingleton<Python.PythonWorkerHealth>();
        services.AddSingleton<Python.PythonWorker>();
        services.AddScoped<Python.PythonFeatures>();
        services.AddScoped<Core.Python.IFoodSuggestionSource, Python.PythonFoodSuggestionSource>();
        services.AddScoped<Core.Python.IIngredientSecondOpinion, Python.PythonIngredientSecondOpinion>();
        services.AddScoped<Core.Python.ISiteRecipeScraper, Python.PythonSiteRecipeScraper>();
        return services;
    }

    /// <summary>Identity core with the rules from SEC-01, SEC-02 and SEC-05 (no sign-in or cookies; Web adds those).</summary>
    public static IdentityBuilder AddRecipeWardenIdentityCore(this IServiceCollection services) =>
        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddPasswordValidator<CommonPasswordValidator>(); // SEC-01

    internal static void ConfigureIdentity(IdentityOptions o)
    {
        // SEC-01: length is the rule (NIST 800-63B style); composition rules are off.
        o.Password.RequiredLength = AccountRules.MinimumPasswordLength;
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredUniqueChars = 1;

        // SEC-02
        o.Lockout.MaxFailedAccessAttempts = AccountRules.MaxFailedAccessAttempts;
        o.Lockout.DefaultLockoutTimeSpan = AccountRules.LockoutDuration;
        o.Lockout.AllowedForNewUsers = true;

        o.User.RequireUniqueEmail = true;
        o.User.AllowedUserNameCharacters = AccountRules.UserNameCharacters; // FR-INST-04: the email is the user name
        o.SignIn.RequireConfirmedAccount = false; // SEC-05: accounts are created by an Admin, not self sign-up
    }
}
