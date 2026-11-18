using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>The single EF Core context. M0 holds Identity and user preferences only.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserPreference>(e =>
        {
            e.HasKey(p => p.UserId);
            e.Property(p => p.Theme).HasConversion<string>().HasMaxLength(16); // 8.1: enums stored as text
            e.HasOne<AppUser>().WithOne().HasForeignKey<UserPreference>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        // 8.1: timestamps as ISO-8601 text in UTC; SQLite has no native DateTimeOffset ordering.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToIsoStringConverter>();
    }
}
