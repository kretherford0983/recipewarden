using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Identity;

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

        services.AddDbContext<AppDbContext>(o => o
            .UseSqlite(data.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor()));

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<UserPreferenceStore>();
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
        o.SignIn.RequireConfirmedAccount = false; // SEC-05: accounts are created by an Admin, not self sign-up
    }
}
