using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 8.1: per-connection pragmas (foreign keys and busy timeout).</summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = Pragmas;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
