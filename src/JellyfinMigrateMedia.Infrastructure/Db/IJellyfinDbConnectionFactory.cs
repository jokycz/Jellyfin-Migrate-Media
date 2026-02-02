using System.Data.Common;

namespace JellyfinMigrateMedia.Infrastructure.Db;

public interface IJellyfinDbConnectionFactory
{
    ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

