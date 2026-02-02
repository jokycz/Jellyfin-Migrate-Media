using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace JellyfinMigrateMedia.Infrastructure.Db;

public sealed class JellyfinSqliteConnectionFactory : IJellyfinDbConnectionFactory
{
    private readonly JellyfinSqliteOptions _options;

    public JellyfinSqliteConnectionFactory(JellyfinSqliteOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = _options.BuildConnectionString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Safer defaults; also makes FK constraints visible during reads.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}

