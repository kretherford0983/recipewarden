using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Core.Organization;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>ADR 0011: built-in tags are seeded once; existing tags with the same name or slug are kept.</summary>
[Trait("Req", "FR-ORG-01")]
public sealed class ADR_0011_DefaultTagSeedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_database_has_every_default_tag_with_its_type()
    {
        await using var tdb = await TestDatabase.CreateAsync(fromTemplate: false);
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tags = await db.Tags.AsNoTracking().ToListAsync(Ct);
        tags.Select(t => (t.Name, t.TagType)).OrderBy(t => t.Name)
            .ShouldBe(DefaultTags.All.Select(t => (t.Name, (TagType?)t.Type)).OrderBy(t => t.Name));
        tags.Single(t => t.Name == "Homemade Ingredient").Slug.ShouldBe("homemade-ingredient");
        tags.Single(t => t.Name == "Side Dish").NormalizedName.ShouldBe("side dish");
    }

    [Fact]
    public async Task Upgrading_keeps_existing_tags_and_only_fills_in_missing_types()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rw-seed-" + Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRecipeWardenData(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir }).Build());
        await using var provider = services.BuildServiceProvider();
        try
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var migrator = db.GetService<IMigrator>();

            // The database as M1 left it, with tags the household already made.
            await migrator.MigrateAsync("20261121155738_M1_Recipes", Ct);
            var now = DateTimeOffset.UtcNow;
            db.Tags.AddRange(
                new Tag { Name = "dinner", NormalizedName = "dinner", Slug = "dinner", CreatedAt = now, UpdatedAt = now },
                new Tag { Name = "Soup", NormalizedName = "soup", Slug = "soup", TagType = TagType.Meal, CreatedAt = now, UpdatedAt = now },
                new Tag { Name = "Side-Dish", NormalizedName = "side-dish", Slug = "side-dish", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(Ct);

            await migrator.MigrateAsync(cancellationToken: Ct);
            db.ChangeTracker.Clear();

            var tags = await db.Tags.AsNoTracking().ToListAsync(Ct);
            var dinner = tags.Single(t => t.NormalizedName == "dinner");
            dinner.Name.ShouldBe("dinner");           // the household's spelling is kept
            dinner.TagType.ShouldBe(TagType.Meal);    // and the type is filled in
            tags.Single(t => t.NormalizedName == "soup").TagType.ShouldBe(TagType.Meal); // an existing type is not overwritten
            tags.Count(t => t.Slug == "side-dish").ShouldBe(1); // same slug, different spelling: kept, no clash
            // Three defaults matched existing tags and were skipped; the three household tags are kept.
            tags.Count.ShouldBe(DefaultTags.All.Count - 3 + 3);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task A_deleted_default_stays_deleted_after_restart()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using (var scope = tdb.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Tags.Where(t => t.Slug == "brunch").ExecuteDeleteAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(Ct);
        }

        await using var check = tdb.Provider.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().Tags.AnyAsync(t => t.Slug == "brunch", Ct)).ShouldBeFalse();
    }
}
