using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Shopping;

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
    public DbSet<Food> Foods => Set<Food>();
    public DbSet<FoodAlias> FoodAliases => Set<FoodAlias>();
    public DbSet<YieldConversion> YieldConversions => Set<YieldConversion>();
    public DbSet<SeedRun> SeedRuns => Set<SeedRun>();
    public DbSet<KitchenTool> KitchenTools => Set<KitchenTool>();
    public DbSet<InventoryVersion> InventoryVersions => Set<InventoryVersion>();
    public DbSet<InventorySnapshot> InventorySnapshots => Set<InventorySnapshot>();
    public DbSet<RecipeSuggestionState> RecipeSuggestionStates => Set<RecipeSuggestionState>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<RecipeCardImage> RecipeCardImages => Set<RecipeCardImage>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListRecipe> ShoppingListRecipes => Set<ShoppingListRecipe>();
    public DbSet<ShoppingLine> ShoppingLines => Set<ShoppingLine>();
    public DbSet<ShoppingCheck> ShoppingChecks => Set<ShoppingCheck>();
    public DbSet<ShoppingListName> ShoppingListNames => Set<ShoppingListName>();
    public DbSet<ShoppingStapleCheck> ShoppingStapleChecks => Set<ShoppingStapleCheck>();
    public DbSet<Staple> Staples => Set<Staple>();
    public DbSet<NutrientRef> NutrientRefs => Set<NutrientRef>();
    public DbSet<NutrientDataset> NutrientDatasets => Set<NutrientDataset>();
    public DbSet<NutritionFacts> NutritionFacts => Set<NutritionFacts>();
    public DbSet<RecipeAllergen> RecipeAllergens => Set<RecipeAllergen>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<BackupSchedule> BackupSchedules => Set<BackupSchedule>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<StorageSnapshot> StorageSnapshots => Set<StorageSnapshot>();
    public DbSet<HealthCheckResult> HealthCheckResults => Set<HealthCheckResult>();
    public DbSet<AiCallLog> AiCallLogs => Set<AiCallLog>();
    public DbSet<FoodSuggestion> FoodSuggestions => Set<FoodSuggestion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserPreference>(e =>
        {
            e.HasKey(p => p.UserId);
            e.Property(p => p.Theme).HasConversion<string>().HasMaxLength(16); // 8.1: enums stored as text
            e.Property(p => p.Units).HasConversion<string>().HasMaxLength(16);
            Real(e.Property(p => p.DefaultScale));
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
            e.Property(r => r.PanShape).HasConversion<string>().HasMaxLength(16);
            Real(e.Property(r => r.PanWidth));
            Real(e.Property(r => r.PanLength));
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasQueryFilter(r => r.DeletedAt == null); // BR-05: trash is hidden everywhere unless asked for
            e.HasOne<Unit>().WithMany().HasForeignKey(r => r.YieldUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Recipe>().WithMany().HasForeignKey(r => r.ParentRecipeId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(r => r.IngredientGroups).WithOne().HasForeignKey(g => g.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Ingredients).WithOne().HasForeignKey(i => i.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Steps).WithOne().HasForeignKey(s => s.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(r => r.Tags).WithOne().HasForeignKey(t => t.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.FinishImageId).HasMaxLength(32);
            e.HasIndex(r => r.FinishImageId);
            e.HasOne<Image>().WithMany().HasForeignKey(r => r.FinishImageId).OnDelete(DeleteBehavior.SetNull);
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
            e.Property(i => i.Measure).HasConversion<string>().HasMaxLength(16).HasDefaultValue(LineMeasure.Auto).HasSentinel(LineMeasure.Auto);
            Real(e.Property(i => i.Quantity));
            Real(e.Property(i => i.QuantityMax));
            Real(e.Property(i => i.ParseConfidence));
            e.HasOne(i => i.Group).WithMany().HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Unit).WithMany().HasForeignKey(i => i.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.FoodId);
            e.HasOne<Food>().WithMany().HasForeignKey(i => i.FoodId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<RecipeStep>(e =>
        {
            e.HasIndex(s => new { s.RecipeId, s.SortOrder });
            e.Property(s => s.Text).HasMaxLength(StepTextRules.MaxStepLength);
            e.Property(s => s.SectionName).HasMaxLength(200);
            e.Property(s => s.Title).HasMaxLength(StepTextRules.MaxTitleLength);
            e.Property(s => s.ImageId).HasMaxLength(32);
            e.HasIndex(s => s.ImageId);
            e.HasOne<Image>().WithMany().HasForeignKey(s => s.ImageId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<RecipeCardImage>(e =>
        {
            e.HasIndex(c => new { c.RecipeId, c.Position });
            e.Property(c => c.ImageId).HasMaxLength(32);
            e.HasIndex(c => c.ImageId);
            e.HasOne<Recipe>().WithMany().HasForeignKey(c => c.RecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Image>().WithMany().HasForeignKey(c => c.ImageId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Image>(e =>
        {
            e.Property(i => i.Id).HasMaxLength(32).ValueGeneratedNever();
            e.Property(i => i.StoragePath).HasMaxLength(64);
            e.Property(i => i.ContentType).HasMaxLength(32);
            e.Property(i => i.Sha256).HasMaxLength(64);
            e.Property(i => i.AltText).HasMaxLength(300);
            e.Property(i => i.SourceUrl).HasMaxLength(SourceRules.MaxUrlLength);
            e.HasIndex(i => i.CreatedAt);
        });

        builder.Entity<ImportJob>(e =>
        {
            e.Property(j => j.BatchId).HasMaxLength(32);
            e.Property(j => j.SourceKind).HasConversion<string>().HasMaxLength(16);
            e.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(j => j.SourceRef).HasMaxLength(SourceRules.MaxUrlLength);
            e.Property(j => j.RawPath).HasMaxLength(128);
            e.Property(j => j.ImageUrl).HasMaxLength(SourceRules.MaxUrlLength);
            e.HasIndex(j => j.BatchId);
            e.HasIndex(j => new { j.Status, j.CreatedAt });
            e.HasOne<Recipe>().WithMany().HasForeignKey(j => j.DuplicateOfRecipeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Recipe>().WithMany().HasForeignKey(j => j.CreatedRecipeId).OnDelete(DeleteBehavior.SetNull);
        });

        // ADR 0034 to 0036: nutrition and allergens.
        builder.Entity<NutrientRef>(e =>
        {
            e.HasIndex(n => n.FdcId).IsUnique();
            e.HasIndex(n => n.NormalizedDescription);
            e.Property(n => n.Source).HasMaxLength(16);
            e.Property(n => n.DataType).HasMaxLength(32);
            e.Property(n => n.Description).HasMaxLength(300);
            e.Property(n => n.NormalizedDescription).HasMaxLength(600);
            e.Property(n => n.Category).HasMaxLength(100);
            e.Property(n => n.DatasetVersion).HasMaxLength(100);
            e.Property(n => n.RemovedInVersion).HasMaxLength(100);
            e.Property(n => n.PortionsJson).HasMaxLength(4000);
            Real(e.Property(n => n.DensityGPerMl));
            NutrientColumns(e);
        });

        builder.Entity<NutrientDataset>(e =>
        {
            e.Property(d => d.Version).HasMaxLength(100);
            e.Property(d => d.FileSha256).HasMaxLength(64);
            e.Property(d => d.AppliedBy).HasMaxLength(100);
        });

        builder.Entity<NutritionFacts>(e =>
        {
            e.HasKey(n => new { n.RecipeId, n.Source });
            e.Property(n => n.Source).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Recipe>().WithMany().HasForeignKey(n => n.RecipeId).OnDelete(DeleteBehavior.Cascade);
            NutrientColumns(e);
        });

        builder.Entity<RecipeAllergen>(e =>
        {
            e.HasKey(a => new { a.RecipeId, a.Allergen, a.Origin });
            e.Property(a => a.Allergen).HasConversion<string>().HasMaxLength(16);
            e.Property(a => a.Origin).HasConversion<string>().HasMaxLength(8);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(a => new { a.Allergen, a.Status });
            e.HasOne<Recipe>().WithMany().HasForeignKey(a => a.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        // ADR 0028/0029: shopping lists.
        builder.Entity<ShoppingList>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(ShoppingRules.MaxListNameLength);
            e.HasIndex(l => l.FinishedAt);
            e.HasMany(l => l.Recipes).WithOne().HasForeignKey(r => r.ShoppingListId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(l => l.Lines).WithOne().HasForeignKey(r => r.ShoppingListId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(l => l.Checks).WithOne().HasForeignKey(r => r.ShoppingListId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShoppingListRecipe>(e =>
        {
            e.Property(r => r.Title).HasMaxLength(PublishRules.MaxTitleLength);
            Real(e.Property(r => r.Scale));
            e.HasOne<Recipe>().WithMany().HasForeignKey(r => r.RecipeId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ShoppingLine>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(ShoppingRules.MaxItemLength);
            e.Property(l => l.RawText).HasMaxLength(ShoppingRules.MaxItemLength * 2);
            e.Property(l => l.CountUnit).HasMaxLength(16);
            e.Property(l => l.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(l => l.Section).HasMaxLength(ShoppingRules.MaxSectionLength);
            Real(e.Property(l => l.Amount));
            Real(e.Property(l => l.AmountMax));
            e.HasIndex(l => new { l.ShoppingListId, l.Position });
            e.HasOne<ShoppingListRecipe>().WithMany().HasForeignKey(l => l.ListRecipeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Food>().WithMany().HasForeignKey(l => l.FoodId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ShoppingCheck>(e =>
        {
            e.HasKey(c => new { c.ShoppingListId, c.ItemKey });
            e.Property(c => c.ItemKey).HasMaxLength(ShoppingRules.MaxItemLength + 2);
        });

        builder.Entity<ShoppingStapleCheck>(e =>
        {
            e.HasKey(c => new { c.ShoppingListId, c.ItemKey });
            e.Property(c => c.ItemKey).HasMaxLength(ShoppingRules.MaxItemLength + 2);
            e.Property(c => c.Answer).HasConversion<string>().HasMaxLength(8);
            e.HasOne<ShoppingList>().WithMany().HasForeignKey(c => c.ShoppingListId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Staple>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(StapleRules.MaxNameLength);
            e.Property(s => s.NormalizedName).HasMaxLength(StapleRules.MaxNameLength * 2);
            e.Property(s => s.Location).HasMaxLength(24);
            e.Property(s => s.Restock).HasMaxLength(StapleRules.MaxRestockLength);
            e.Property(s => s.Section).HasMaxLength(ShoppingRules.MaxSectionLength);
            e.HasIndex(s => s.NormalizedName).IsUnique();
            e.HasIndex(s => s.FoodId).IsUnique();
            e.HasOne<Food>().WithMany().HasForeignKey(s => s.FoodId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShoppingListName>(e =>
        {
            e.Property(n => n.Name).HasMaxLength(ShoppingRules.MaxListNameLength);
            e.Property(n => n.NormalizedName).HasMaxLength(ShoppingRules.MaxListNameLength * 2);
            e.HasIndex(n => n.NormalizedName).IsUnique();
            e.HasIndex(n => n.LastUsedAt);
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

        builder.Entity<Food>(e =>
        {
            e.HasIndex(f => f.NormalizedName).IsUnique();
            e.Property(f => f.Name).HasMaxLength(FoodRules.MaxNameLength);
            e.Property(f => f.NormalizedName).HasMaxLength(FoodRules.MaxNameLength * 2);
            e.Property(f => f.PluralName).HasMaxLength(FoodRules.MaxNameLength);
            e.Property(f => f.Category).HasMaxLength(40);
            e.Property(f => f.Source).HasMaxLength(16);
            e.Property(f => f.SourceNote).HasMaxLength(500);
            e.Property(f => f.StoreSection).HasMaxLength(ShoppingRules.MaxSectionLength);
            e.Property(f => f.PackageName).HasMaxLength(ShoppingRules.MaxPackageNameLength);
            Real(e.Property(f => f.DensityGPerMl));
            e.Property(f => f.Form).HasConversion<string>().HasMaxLength(16);
            Real(e.Property(f => f.PackageSize));
            e.HasOne<NutrientRef>().WithMany().HasForeignKey(f => f.NutrientRefId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(f => f.Aliases).WithOne().HasForeignKey(a => a.FoodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Unit>().WithMany().HasForeignKey(f => f.DefaultPurchaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Unit>().WithMany().HasForeignKey(f => f.PackageUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FoodAlias>(e =>
        {
            e.HasIndex(a => a.NormalizedAlias).IsUnique();
            e.HasIndex(a => a.FoodId);
            e.Property(a => a.Alias).HasMaxLength(FoodRules.MaxNameLength);
            e.Property(a => a.NormalizedAlias).HasMaxLength(FoodRules.MaxNameLength * 2);
        });

        builder.Entity<YieldConversion>(e =>
        {
            e.HasIndex(y => new { y.FoodId, y.FromState }).IsUnique();
            e.Property(y => y.FromState).HasMaxLength(16);
            e.Property(y => y.ToState).HasMaxLength(16);
            e.Property(y => y.FromUnitKind).HasConversion<string>().HasMaxLength(16);
            e.Property(y => y.Basis).HasMaxLength(FoodRules.MaxBasisLength);
            e.Property(y => y.Source).HasMaxLength(16);
            e.Property(y => y.SourceNote).HasMaxLength(500);
            Real(e.Property(y => y.Ratio));
            Real(e.Property(y => y.RatioMax));
            Real(e.Property(y => y.PreparedDensityGPerMl));
            e.HasOne(y => y.Food).WithMany().HasForeignKey(y => y.FoodId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<KitchenTool>(e =>
        {
            e.HasIndex(t => new { t.IsActive, t.ToolType });
            e.Property(t => t.Name).HasMaxLength(KitchenRules.MaxNameLength);
            e.Property(t => t.ToolType).HasConversion<string>().HasMaxLength(16);
            e.Property(t => t.PanShape).HasConversion<string>().HasMaxLength(16);
            e.Property(t => t.SupportedUnits).HasMaxLength(32);
            e.Property(t => t.Markings).HasMaxLength(512);
            Real(e.Property(t => t.DefaultFillPct));
            Real(e.Property(t => t.CapacityAmount));
            Real(e.Property(t => t.ResolutionAmount));
            Real(e.Property(t => t.PanWidth));
            Real(e.Property(t => t.PanLength));
            e.HasOne(t => t.CapacityUnit).WithMany().HasForeignKey(t => t.CapacityUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ResolutionUnit).WithMany().HasForeignKey(t => t.ResolutionUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryVersion>(e => e.Property(v => v.Id).ValueGeneratedNever());

        builder.Entity<InventorySnapshot>(e =>
        {
            e.HasKey(s => s.Version);
            e.Property(s => s.Version).ValueGeneratedNever();
        });

        builder.Entity<RecipeSuggestionState>(e =>
        {
            e.HasKey(s => new { s.RecipeId, s.SuggestionKey });
            e.Property(s => s.SuggestionKey).HasMaxLength(100);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Recipe>().WithMany().HasForeignKey(s => s.RecipeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SeedRun>(e =>
        {
            e.HasKey(r => r.Name);
            e.Property(r => r.Name).HasMaxLength(64);
        });

        // M7 Operations (8.5, ADR 0037 to 0041)
        builder.Entity<AuditEntry>(e =>
        {
            e.HasIndex(a => a.At);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
            e.Property(a => a.UserId).HasMaxLength(64);
            e.Property(a => a.UserName).HasMaxLength(256);
            e.Property(a => a.Action).HasMaxLength(64);
            e.Property(a => a.EntityType).HasMaxLength(64);
            e.Property(a => a.EntityId).HasMaxLength(128);
            e.Property(a => a.Ip).HasMaxLength(64);
            e.Property(a => a.DetailsJson).HasMaxLength(4000);
        });

        builder.Entity<BackupRecord>(e =>
        {
            e.HasIndex(b => b.StartedAt);
            e.HasIndex(b => new { b.ScheduleId, b.Status });
            e.Property(b => b.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(b => b.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(b => b.DestinationFolder).HasMaxLength(1024);
            e.Property(b => b.FileName).HasMaxLength(255);
            e.Property(b => b.Sha256).HasMaxLength(64);
            e.Property(b => b.KeyFingerprint).HasMaxLength(128);
            e.Property(b => b.Error).HasMaxLength(2000);
            e.Property(b => b.StartedBy).HasMaxLength(256);
            e.Ignore(b => b.FullPath);
        });

        builder.Entity<BackupSchedule>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(80);
            e.Property(s => s.Cron).HasMaxLength(120);
            e.Property(s => s.DestinationFolder).HasMaxLength(1024);
            e.Ignore(s => s.Retention);
            e.Ignore(s => s.Timing);
        });

        builder.Entity<AppSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(64);
            e.Property(s => s.Value).HasMaxLength(4000);
        });

        builder.Entity<StorageSnapshot>(e => e.HasIndex(s => s.TakenAt));

        builder.Entity<HealthCheckResult>(e =>
        {
            e.HasIndex(h => new { h.CheckName, h.RanAt });
            e.Property(h => h.CheckName).HasMaxLength(128);
            e.Property(h => h.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(h => h.Message).HasMaxLength(1000);
        });

        // M8: AI usage log (FR-IMP-10, ADR 0042)
        builder.Entity<FoodSuggestion>(e =>
        {
            // FR-NUT-07: one row per name, engine and dataset (BR-116).
            e.HasIndex(f => new { f.NormalizedName, f.EngineVersion, f.DatasetVersion }).IsUnique();
            e.Property(f => f.NormalizedName).HasMaxLength(200);
            e.Property(f => f.Description).HasMaxLength(300);
            e.Property(f => f.DataType).HasMaxLength(40);
            e.Property(f => f.EngineVersion).HasMaxLength(100);
            e.Property(f => f.DatasetVersion).HasMaxLength(400);
            e.Property(f => f.DismissedById).HasMaxLength(450);
            Real(e.Property(f => f.Confidence));
            e.HasOne<NutrientRef>().WithMany().HasForeignKey(f => f.NutrientRefId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiCallLog>(e =>
        {
            e.HasIndex(a => a.At);
            e.Property(a => a.Purpose).HasConversion<string>().HasMaxLength(24);
            e.Property(a => a.Provider).HasConversion<string>().HasMaxLength(24);
            e.Property(a => a.Result).HasConversion<string>().HasMaxLength(16);
            e.Property(a => a.Model).HasMaxLength(100);
            e.Property(a => a.Error).HasMaxLength(300);
            e.Property(a => a.UserId).HasMaxLength(64);
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
    private static void NutrientColumns<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : class
    {
        foreach (var name in new[] { "Calories", "FatG", "SatFatG", "CholesterolMg", "SodiumMg", "CarbsG", "FiberG", "SugarG", "ProteinG" })
        {
            e.Property<decimal?>(name).HasConversion<double?>();
        }
    }

    private static void Real(PropertyBuilder<decimal> p) => p.HasConversion<double>();

    private static void Real(PropertyBuilder<decimal?> p) => p.HasConversion<double?>();
}
