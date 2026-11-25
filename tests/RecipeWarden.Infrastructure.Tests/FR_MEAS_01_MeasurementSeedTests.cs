using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Measurement;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0013, FR-MEAS-01, FR-MEAS-04: the USDA seed is applied once and never overwrites the household's changes.</summary>
[Trait("Req", "FR-MEAS-01")]
public sealed class FR_MEAS_01_MeasurementSeedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_database_has_the_seeded_foods_aliases_and_yields()
    {
        await using var tdb = await TestDatabase.CreateAsync(fromTemplate: false);
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doc = MeasurementSeeder.Load();

        (await db.Foods.CountAsync(Ct)).ShouldBe(doc.Foods.Count);
        (await db.YieldConversions.CountAsync(Ct)).ShouldBe(doc.Yields.Count);
        (await db.SeedRuns.Select(r => r.Name).ToListAsync(Ct)).ShouldContain(MeasurementSeeder.SeedName); // M3 adds kitchen-v1 and food-form-v1

        var flour = await db.Foods.Include(f => f.Aliases).SingleAsync(f => f.Name == "all-purpose flour", Ct);
        flour.DensityGPerMl.ShouldBe(0.5283m);
        flour.FdcId.ShouldBe(168894);
        flour.Source.ShouldBe(FoodSources.Usda);
        flour.SourceNote!.ShouldContain("1 cup = 125 g");
        flour.Aliases.Select(a => a.NormalizedAlias).ShouldContain("ap flour"); // "a.p. flour" and "ap flour" share one key
    }

    [Fact]
    public void Every_seed_row_has_a_usda_source_and_plausible_values()
    {
        var doc = MeasurementSeeder.Load();
        doc.Foods.Count.ShouldBeGreaterThanOrEqualTo(150);
        doc.Foods.ShouldAllBe(f => f.FdcId > 0 && f.SourceNote!.StartsWith("USDA FoodData Central SR Legacy", StringComparison.Ordinal));
        doc.Foods.Where(f => f.DensityGPerMl != null).ShouldAllBe(f => FoodRules.ValidateDensity(f.DensityGPerMl) == null);
        doc.Foods.Select(f => FoodMatcher.Normalize(f.Name)).Distinct().Count().ShouldBe(doc.Foods.Count);
        doc.Yields.ShouldAllBe(y => FoodRules.ValidateYield(y.Ratio, y.RatioMax, y.PreparedDensityGPerMl, y.Basis) == null);
        doc.Yields.ShouldAllBe(y => y.SourceNote!.StartsWith("USDA Table of Cooking Yields", StringComparison.Ordinal));
        doc.Yields.Select(y => y.Food).ShouldAllBe(name => doc.Foods.Any(f => f.Name == name));
    }

    [Fact]
    public async Task The_seed_runs_once_so_deleted_foods_stay_deleted_and_edits_are_kept()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using (var scope = tdb.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Foods.Where(f => f.Name == "molasses").ExecuteDeleteAsync(Ct)).ShouldBe(1);
            await db.Foods.Where(f => f.Name == "honey").ExecuteUpdateAsync(u => u.SetProperty(f => f.DensityGPerMl, 1.4m), Ct);
        }

        await using (var scope = tdb.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(Ct);
            (await scope.ServiceProvider.GetRequiredService<MeasurementSeeder>().ApplyAsync(Ct)).ShouldBeFalse();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Foods.AnyAsync(f => f.Name == "molasses", Ct)).ShouldBeFalse();
            (await db.Foods.SingleAsync(f => f.Name == "honey", Ct)).DensityGPerMl.ShouldBe(1.4m);
        }
    }

    [Fact]
    public async Task Upgrading_from_m1_seeds_the_catalog_and_links_existing_ingredients()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rw-m2-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecipeWardenData(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir }).Build());
        services.AddRecipeWardenIdentityCore();
        await using var provider = services.BuildServiceProvider();
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.GetService<IMigrator>().MigrateAsync("20261121174600_M1_RangesAndDefaultTags", Ct);
                var now = DateTimeOffset.UtcNow;
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO Recipes (Slug, Title, Servings, ServingsEstimated, IsPrecision, Status, SourceType, InventoryVersionSeen, Version, CreatedAt, UpdatedAt) VALUES ('soup', 'Soup', 4, 0, 0, 'Published', 'Manual', 0, 1, {now.ToString("O")}, {now.ToString("O")})", Ct);
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO RecipeIngredients (RecipeId, SortOrder, RawText, Quantity, Name, IsOptional, ParseConfidence) VALUES (1, 0, '2 cups AP flour', 2, 'AP flour', 0, 1)", Ct);
                await db.Database.ExecuteSqlAsync(
                    $"INSERT INTO RecipeIngredients (RecipeId, SortOrder, RawText, Quantity, Name, IsOptional, ParseConfidence) VALUES (1, 1, '1 cup unobtainium', 1, 'unobtainium', 0, 1)", Ct);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(Ct);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var flourId = await db.Foods.Where(f => f.Name == "all-purpose flour").Select(f => f.Id).SingleAsync(Ct);
                var lines = await db.RecipeIngredients.IgnoreQueryFilters().OrderBy(i => i.SortOrder).ToListAsync(Ct);
                lines[0].FoodId.ShouldBe(flourId);
                lines[1].FoodId.ShouldBeNull();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task A_household_food_with_a_seed_name_is_kept_and_the_seed_row_skipped()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SeedRuns.RemoveRange(db.SeedRuns);
        db.YieldConversions.RemoveRange(db.YieldConversions);
        db.Foods.RemoveRange(db.Foods);
        await db.SaveChangesAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        db.Foods.Add(new Food { Name = "Chicken Breast", NormalizedName = "chicken breast", PluralName = "chicken breasts", DensityGPerMl = 0.5m, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(Ct);

        (await scope.ServiceProvider.GetRequiredService<MeasurementSeeder>().ApplyAsync(Ct)).ShouldBeTrue();

        db.ChangeTracker.Clear();
        var chicken = await db.Foods.SingleAsync(f => f.NormalizedName == "chicken breast", Ct);
        chicken.Source.ShouldBe(FoodSources.Custom);
        chicken.DensityGPerMl.ShouldBe(0.5m);
        (await db.YieldConversions.AnyAsync(y => y.FoodId == chicken.Id, Ct)).ShouldBeFalse();
        (await db.Foods.CountAsync(Ct)).ShouldBe(MeasurementSeeder.Load().Foods.Count);
    }
}
