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
        return services;
    }

    /// <summary>Identity core with the rules from SEC-01, SEC-02 and SEC-05 (no sign-in or cookies; Web adds those).</summary>
    public static IdentityBuilder AddRecipeWardenIdentityCore(this IServiceCollection services) =>
        services.AddIdentityCore<AppUser>(ConfigureIdentity)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

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
