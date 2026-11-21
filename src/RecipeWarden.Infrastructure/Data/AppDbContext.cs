using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>The single EF Core context: Identity, user preferences and the recipe aggregate (8.2, ADR 0009).</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<IngredientGroup> IngredientGroups => Set<IngredientGroup>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> Steps => Set<RecipeStep>();
    public DbSet<RecipeSlugAlias> RecipeSlugAliases => Set<RecipeSlugAlias>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RecipeTag> RecipeTags => Set<RecipeTag>();
    public DbSet<RecipeCollection> Collections => Set<RecipeCollection>();
    public DbSet<CollectionRecipe> CollectionRecipes => Set<CollectionRecipe>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserPreference>(e =>
        {
            e.HasKey(p => p.UserId);
            e.Property(p => p.Theme).HasConversion<string>().HasMaxLength(16); // 8.1: enums stored as text
            e.HasOne<AppUser>().WithOne().HasForeignKey<UserPreference>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Recipe>(e =>
        {
            e.HasIndex(r => r.Slug).IsUnique();
            e.HasIndex(r => new { r.Status, r.DeletedAt });
            e.HasIndex(r => r.Title);
            e.HasIndex(r => r.TotalMinutes);
            e.HasIndex(r => r.CreatedAt);
            e.Property(r => r.Slug).HasMaxLength(SlugRules.MaxLength);
            e.Property(r => r.Title).HasMaxLength(PublishRules.MaxTitleLength);
            e.Property(r => r.Description).HasMaxLength(RecipeEdit.MaxDescriptionLength);
            e.Property(r => r.Notes).HasMaxLength(RecipeEdit.MaxNotesLength);
            e.Property(r => r.SourceUrl).HasMaxLength(SourceRules.MaxUrlLength);
            e.Property(r => r.SourceName).HasMaxLength(SourceRules.MaxNameLength);
            e.Property(r => r.SourceAuthor).HasMaxLength(SourceRules.MaxNameLength);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(r => r.SourceType).HasConversion<string>().HasMaxLength(16);
            e.Property(r => r.YieldSource).HasMaxLength(16);
            Real(e.Property(r => r.Servings));
            Real(e.Property(r => r.ServingsMax));
            Real(e.Property(r => r.YieldAmount));
            Real(e.Property(r => r.FinishedDensityGPerMl));
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasQueryFilter(r => r.DeletedAt == null); // BR-05: trash is hidden everywhere unless asked for
            e.HasOne<Unit>().WithMany().HasForeignKey(r => r.YieldUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Recipe>().WithMany().HasForeignKey(r => r.ParentRecipeId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(r => r.IngredientGroups).WithOne().HasForeignKey(g => g.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Ingredients).WithOne().HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Tags).WithOne().HasForeignKey(t => t.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<IngredientGroup>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(200);
            e.HasIndex(g => new { g.RecipeId, g.SortOrder });
        });

        builder.Entity<RecipeIngredient>(e =>
        {
            e.HasIndex(i => new { i.RecipeId, i.SortOrder });
            e.HasIndex(i => i.Name); // unmatched-ingredient report (FR-NUT-03) groups by name
            e.Property(i => i.RawText).HasMaxLength(RecipeEdit.MaxIngredientLineLength);
            e.Property(i => i.Name).HasMaxLength(RecipeEdit.MaxIngredientLineLength);
            e.Property(i => i.Preparation).HasMaxLength(RecipeEdit.MaxIngredientLineLength);
            e.Property(i => i.Note).HasMaxLength(RecipeEdit.MaxIngredientLineLength);
            e.Property(i => i.QuantityText).HasMaxLength(32);
            e.Property(i => i.State).HasMaxLength(16);
            Real(e.Property(i => i.Quantity));
            Real(e.Property(i => i.QuantityMax));
            Real(e.Property(i => i.ParseConfidence));
            e.HasOne(i => i.Group).WithMany().HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Unit).WithMany().HasForeignKey(i => i.UnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RecipeStep>(e =>
        {
            e.HasIndex(s => new { s.RecipeId, s.SortOrder });
            e.Property(s => s.Text).HasMaxLength(StepTextRules.MaxStepLength);
            e.Property(s => s.SectionName).HasMaxLength(200);
        });

        builder.Entity<RecipeSlugAlias>(e =>
        {
            e.HasKey(a => a.Slug);
            e.Property(a => a.Slug).HasMaxLength(SlugRules.MaxLength);
            e.HasIndex(a => a.RecipeId);
            e.HasOne<Recipe>().WithMany().HasForeignKey(a => a.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Unit>(e =>
        {
            e.HasIndex(u => u.Code).IsUnique();
            e.Property(u => u.Code).HasMaxLength(16);
            e.Property(u => u.Name).HasMaxLength(32);
            e.Property(u => u.PluralName).HasMaxLength(32);
            e.Property(u => u.Aliases).HasMaxLength(500);
            e.Property(u => u.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(u => u.System).HasConversion<string>().HasMaxLength(16);
            Real(e.Property(u => u.ToBase));
            e.HasData(UnitCatalog.All.Select((u, i) => new Unit
            {
                Id = i + 1,
                Code = u.Code,
                Name = u.Name,
                PluralName = u.PluralName,
                Aliases = string.Join('|', u.Aliases),
                Kind = u.Kind,
                System = u.System,
                ToBase = u.ToBase,
                IsVague = u.IsVague,
            }));
        });

        builder.Entity<Tag>(e =>
        {
            e.HasIndex(t => t.NormalizedName).IsUnique();
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.Name).HasMaxLength(NameRules.MaxTagLength);
            e.Property(t => t.NormalizedName).HasMaxLength(NameRules.MaxTagLength * 2);
            e.Property(t => t.Slug).HasMaxLength(SlugRules.MaxLength);
            e.Property(t => t.TagType).HasConversion<string>().HasMaxLength(16);
        });

        builder.Entity<RecipeTag>(e =>
        {
            e.HasKey(t => new { t.RecipeId, t.TagId });
            e.HasIndex(t => t.TagId);
            e.HasOne(t => t.Tag).WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecipeCollection>(e =>
        {
            e.HasIndex(c => c.NormalizedName).IsUnique();
            e.HasIndex(c => c.Slug).IsUnique();
            e.Property(c => c.Name).HasMaxLength(NameRules.MaxCollectionLength);
            e.Property(c => c.NormalizedName).HasMaxLength(NameRules.MaxCollectionLength * 2);
            e.Property(c => c.Slug).HasMaxLength(SlugRules.MaxLength);
            e.Property(c => c.Description).HasMaxLength(RecipeEdit.MaxDescriptionLength);
            e.Property(c => c.Version).IsConcurrencyToken();
            e.HasMany(c => c.Recipes).WithOne().HasForeignKey(r => r.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CollectionRecipe>(e =>
        {
            e.HasKey(c => new { c.CollectionId, c.RecipeId });
            e.HasIndex(c => c.RecipeId);
            e.HasOne<Recipe>().WithMany().HasForeignKey(c => c.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        // 8.1: timestamps as ISO-8601 text in UTC; SQLite has no native DateTimeOffset ordering.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToIsoStringConverter>();
    }

    // 8.1: quantities are REAL in the database (SQLite stores decimal as TEXT, which sorts and compares as text);
    // code works in decimal.
    private static void Real(PropertyBuilder<decimal> p) => p.HasConversion<double>();

    private static void Real(PropertyBuilder<decimal?> p) => p.HasConversion<double?>();
}
