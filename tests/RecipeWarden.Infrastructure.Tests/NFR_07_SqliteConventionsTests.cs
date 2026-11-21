using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>Section 8.1 conventions: WAL, foreign keys, busy timeout.</summary>
[Trait("Req", "NFR-07")]
public sealed class NFR_07_SqliteConventionsTests
{
    private static async Task<string> PragmaAsync(AppDbContext db, int which)
    {
        var conn = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = which switch
        {
            0 => "PRAGMA journal_mode;",
            1 => "PRAGMA foreign_keys;",
            _ => "PRAGMA busy_timeout;",
        };
        return (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!.ToString()!;
    }

    [Fact]
    public async Task Database_uses_wal_foreign_keys_and_busy_timeout()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await PragmaAsync(db, 0)).ShouldBe("wal");
        (await PragmaAsync(db, 1)).ShouldBe("1");
        (await PragmaAsync(db, 2)).ShouldBe("5000");
    }

    [Fact]
    public async Task Fresh_empty_database_is_migrated_and_seeded()
    {
        await using var tdb = await TestDatabase.CreateAsync(fromTemplate: false);
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        (await PragmaAsync(db, 0)).ShouldBe("wal");
        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await db.Roles.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Initialization_is_idempotent()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Roles.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task No_pending_model_changes_without_a_migration()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-SRCH-01")]
    public async Task Migration_creates_the_fts5_search_table()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sql = await db.Database
            .SqlQuery<string>($"SELECT sql AS Value FROM sqlite_master WHERE name = 'RecipeSearch'")
            .SingleAsync(TestContext.Current.CancellationToken);
        sql.ShouldContain("fts5");
        sql.ShouldContain("remove_diacritics 2");
    }

    [Fact]
    [Trait("Req", "FR-REC-03")]
    public async Task Units_are_seeded_from_the_catalogue()
    {
        await using var tdb = await TestDatabase.CreateAsync();
        await using var scope = tdb.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var units = await db.Units.ToListAsync(TestContext.Current.CancellationToken);
        units.Select(u => u.Code).Order().ShouldBe(RecipeWarden.Core.Measurement.UnitCatalog.All.Select(u => u.Code).Order());
        units.Single(u => u.Code == "cup").Aliases.Split('|').ShouldContain("cups");
    }
}
