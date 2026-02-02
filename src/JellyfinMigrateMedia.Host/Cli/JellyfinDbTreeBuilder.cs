using System.Data.Common;
using JellyfinMigrateMedia.Infrastructure.Db;

namespace JellyfinMigrateMedia.Host.Cli;

internal sealed class JellyfinDbTreeBuilder
{
    private readonly DbConnection _conn;
    private readonly DbTransaction? _tx;
    private readonly string _basePath;

    public JellyfinGuid TopParentGuidBytes { get; }

    private JellyfinDbTreeBuilder(DbConnection conn, DbTransaction? tx, JellyfinGuid topParentGuidBytes, string basePath)
    {
        _conn = conn;
        _tx = tx;
        TopParentGuidBytes = topParentGuidBytes;
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    public static async Task<JellyfinDbTreeBuilder> CreateAsync(
        DbConnection conn,
        DbTransaction? tx,
        JellyfinGuid topParentId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        if (topParentId.Value == Guid.Empty)
            throw new ArgumentException("Missing topParentId.", nameof(topParentId));

        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT guid, Path
FROM TypedBaseItems
WHERE guid = $guid
LIMIT 1;";
        // guid column is BLOB in Jellyfin (Guid.ToByteArray()).
        AddParam(cmd, "$guid", topParentId.Bytes);

        JellyfinGuid guidBytes = default;
        string? path = null;
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                guidBytes = JellyfinGuid.FromBytes((byte[]) r.GetValue(0));
                path = r.IsDBNull(1) ? null : r.GetString(1);
            }
        }

        if (guidBytes.Value == Guid.Empty || string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Cannot locate base folder in DB for TopParentId={topParentId.NoDashes}.");

        return new(conn, tx, guidBytes, path);
    }

    /// <summary>
    /// Ensures DB folder hierarchy exists for the given absolute directory path (must be under base path).
    /// Returns the guid bytes of the deepest folder item.
    /// </summary>
    public async Task<JellyfinGuid> EnsureFolderTreeAsync(string targetDirPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(targetDirPath))
            return TopParentGuidBytes;

        var normBase = NormalizePathForCompare(_basePath);
        var normTarget = NormalizePathForCompare(targetDirPath);

        if (!normTarget.StartsWith(normBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Target path is not under base path. Base='{_basePath}', Target='{targetDirPath}'.");

        var relative = targetDirPath[_basePath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative))
            return TopParentGuidBytes;

        var parts = relative
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var parentGuid = TopParentGuidBytes;
        var currentPath = _basePath;

        foreach (var part in parts)
        {
            ct.ThrowIfCancellationRequested();

            currentPath = Path.Combine(currentPath, part);

            var existing = await FindFolderGuidByPathAsync(currentPath, parentGuid, ct).ConfigureAwait(false);
            if (existing.Value != Guid.Empty)
            {
                parentGuid = existing;
                continue;
            }

            // Create
            var newGuid = new JellyfinGuid(Guid.NewGuid());
                // Clone parent row to satisfy NOT NULL defaults, then override columns.
                await JellyfinTypedBaseItemsCloner.CloneAsync(
                        _conn,
                        _tx ?? throw new InvalidOperationException("DB transaction missing"),
                        parentGuid,
                        newGuid,
                        new Dictionary<string, object?>
                        {
                            ["Type"] = "MediaBrowser.Controller.Entities.Folder",
                            ["name"] = part,
                            ["Path"] = currentPath,
                            // ParentId is BLOB guid in DB.
                            ["ParentId"] = parentGuid.Bytes,
                            // TopParentId is inconsistent in Jellyfin; store as TEXT ("N") to match common usage.
                            ["TopParentId"] = TopParentGuidBytes.NoDashes,
                        },
                        ct)
                    .ConfigureAwait(false);

            // Ensure AncestorIds for the newly created folder (idempotent).
            // This is the "folder" side of the relationship; movies will do their own call after clone/reuse.
            _ = await JellyfinItemRelationsCloner.EnsureAncestorIdsForItemAsync(
                    _conn,
                    _tx ?? throw new InvalidOperationException("DB transaction missing"),
                    newGuid,
                    ct)
                .ConfigureAwait(false);

            parentGuid = newGuid;
        }

        return parentGuid;
    }

    private async Task<JellyfinGuid> FindFolderGuidByPathAsync(string fullPath, JellyfinGuid parentGuid, CancellationToken ct)
    {
        await using var cmd = _conn.CreateCommand();
        if (_tx is not null) cmd.Transaction = _tx;
        cmd.CommandText = @"
SELECT guid
FROM TypedBaseItems
WHERE Type = $type
  AND lower(Path) = lower($path)
  AND (
        TopParentId = $topBlob
     OR lower(CAST(TopParentId AS TEXT)) = lower($topText)
     OR lower(hex(TopParentId)) = lower($topText)
  )
  AND ParentId = $parent
LIMIT 1;";

        AddParam(cmd, "$type", "MediaBrowser.Controller.Entities.Folder");
        AddParam(cmd, "$path", fullPath);
        AddParam(cmd, "$topBlob", TopParentGuidBytes.Bytes);
        AddParam(cmd, "$topText", TopParentGuidBytes.NoDashes);
        AddParam(cmd, "$parent", parentGuid.Bytes);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result == DBNull.Value)
            return default;
        return JellyfinGuid.FromBytes((byte[]) result);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string NormalizePathForCompare(string p)
        => p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

