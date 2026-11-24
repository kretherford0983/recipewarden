using Microsoft.Data.Sqlite;
using RecipeWarden.Infrastructure.Backup;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-BKP-01: a restore must not fail with "database is locked" because another connection holds a lock on the live
/// database for a moment (a request writing, a checkpoint). The copy waits for the lock (SQLite busy_timeout) instead
/// of giving up at once. This was the intermittent CI failure in FR_BKP_01_BackupPageTests and BackupServiceTests.
/// </summary>
[Trait("Req", "FR-BKP-01")]
public sealed class FR_BKP_01_RestoreLockTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-restore-lock-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Db(string name, int rows)
    {
        var path = Path.Combine(dir, name);
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE t(x INTEGER);" + string.Concat(Enumerable.Repeat("INSERT INTO t VALUES (1);", rows));
        cmd.ExecuteNonQuery();
        return path;
    }

    private static long Rows(string path)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM t";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task The_copy_waits_for_a_brief_write_lock_on_the_live_database_instead_of_failing()
    {
        var archive = Db("archive.db", 5);
        var live = Db("live.db", 1);
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = live, Pooling = false }.ToString());
        writer.Open();
        using var tx = writer.BeginTransaction(deferred: false); // BEGIN IMMEDIATE: holds the write lock
        using (var insert = writer.CreateCommand())
        {
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO t VALUES (2)";
            insert.ExecuteNonQuery();
        }

        var copy = Task.Run(() => RestoreService.CopyDatabase(archive, live), Ct);
        // Hold the lock for a moment while the copy is waiting for it, then let go.
        await Task.WhenAny(copy, Task.Delay(300, Ct));
        copy.IsCompleted.ShouldBeFalse(copy.Exception?.GetBaseException().Message ?? "the copy should be waiting for the lock");
        tx.Commit();

        await copy; // no "database is locked"
        Rows(live).ShouldBe(5);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
