
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace JellyfinMigrateMedia.Infrastructure.Configuration;

public sealed class JellyfinMigrateSettings
{
    /// <summary>
    /// Jellyfin Server root directory, e.g. "C:\ProgramData\Jellyfin\Server".
    /// Used to discover libraries on disk (root\*\options.xml) and DB path (root\data\library.db).
    /// </summary>
    public string? JellyfinServerRootPath { get; set; }

    public string? JellyfinSqliteDbPath { get; set; }

    public List<MigrationProfile> MigrationProfiles { get; set; } = [];

    /// <summary>
    /// Remembers the last selected profile for convenience.
    /// </summary>
    public string? LastProfileId { get; set; }
}

