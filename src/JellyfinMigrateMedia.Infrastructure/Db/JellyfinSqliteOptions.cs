// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace JellyfinMigrateMedia.Infrastructure.Db;

public sealed class JellyfinSqliteOptions
{
    /// <summary>
    /// If set, used as-is.
    /// Example: "Data Source=C:\path\library.db;Mode=ReadOnly"
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Alternative to ConnectionString. Will be converted to "Data Source=...".
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Defaults to true to avoid accidental writes during migration.
    /// </summary>
    public bool ReadOnly { get; set; } = true;

    public string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString!;

        var dbPath = DatabasePath;
        if (!string.IsNullOrWhiteSpace(dbPath))
        {
            // Common when reading from JSON config: "%ProgramData%\..." or quoted paths.
            dbPath = Environment.ExpandEnvironmentVariables(dbPath.Trim().Trim('"'));
        }

        if (string.IsNullOrWhiteSpace(dbPath))
            throw new InvalidOperationException(
                "Missing Jellyfin SQLite configuration. Set ConnectionString or DatabasePath. " +
                "If using configuration, set JellyfinMigrate:JellyfinSqliteDbPath (or JellyfinSqlite:DatabasePath).");

        // Microsoft.Data.Sqlite supports Mode=ReadOnly/ReadWrite/Create.
        var mode = ReadOnly ? "ReadOnly" : "ReadWrite";
        return $"Data Source={dbPath};Mode={mode}";
    }
}

