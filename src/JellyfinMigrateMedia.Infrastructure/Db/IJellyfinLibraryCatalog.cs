namespace JellyfinMigrateMedia.Infrastructure.Db;

public interface IJellyfinLibraryCatalog
{
    Task<IReadOnlyList<JellyfinLibraryInfo>> GetLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads settings for a specific library (e.g. from its options.xml).
    /// </summary>
    Task<JellyfinLibraryInfo?> GetLibraryAsync(string libraryId, CancellationToken cancellationToken = default);
}

public sealed record JellyfinLibraryInfo(
    string Name,
    string? ContentType,
    IReadOnlyList<ContentPath> Paths);

public sealed record ContentPath(
    string Path,
    string TopId);

