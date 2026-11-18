using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Cli.Health;

/// <summary>
/// Section 9.5: "recipewarden health" prints a summary for SSH use. M0 skeleton: data directory, database
/// quick_check and journal mode. Disk, backup and capacity checks arrive with FR-OPS-02 (M7).
/// </summary>
internal static class HealthCommand
{
    public static async Task<int> RunAsync(IConfiguration config, TextWriter output, CancellationToken cancellationToken)
    {
        var data = new DataOptions();
        config.GetSection(DataOptions.SectionName).Bind(data);
        var ok = true;

        await output.WriteLineAsync($"Data directory: {data.Directory} ({(Directory.Exists(data.Directory) ? "present" : "MISSING")})");
        ok &= Directory.Exists(data.Directory);

        if (!File.Exists(data.DatabasePath))
        {
            await output.WriteLineAsync($"Database: {data.DatabasePath} MISSING");
            await output.WriteLineAsync("Status: Critical");
            return 1;
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = data.DatabasePath, Mode = SqliteOpenMode.ReadOnly };
        var connection = new SqliteConnection(builder.ToString());
        await using (connection)
        {
            await connection.OpenAsync(cancellationToken);
            var quick = await ScalarAsync(connection, quickCheck: true, cancellationToken);
            var mode = await ScalarAsync(connection, quickCheck: false, cancellationToken);
            await output.WriteLineAsync($"Database: {data.DatabasePath} ({new FileInfo(data.DatabasePath).Length:N0} bytes)");
            await output.WriteLineAsync($"  quick_check: {quick}");
            await output.WriteLineAsync($"  journal_mode: {mode}");
            ok &= string.Equals(quick, "ok", StringComparison.Ordinal);
            ok &= string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase);
        }

        await output.WriteLineAsync($"Status: {(ok ? "OK" : "Critical")}");
        return ok ? 0 : 1;
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, bool quickCheck, CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        await using (cmd)
        {
            if (quickCheck)
            {
                cmd.CommandText = "PRAGMA quick_check;";
            }
            else
            {
                cmd.CommandText = "PRAGMA journal_mode;";
            }

            return (await cmd.ExecuteScalarAsync(cancellationToken))?.ToString();
        }
    }
}
