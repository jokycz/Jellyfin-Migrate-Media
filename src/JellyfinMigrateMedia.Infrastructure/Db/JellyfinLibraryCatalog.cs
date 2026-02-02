using System.Data.Common;
using System.Text;
using System.Text.Json;
// ReSharper disable UnusedMember.Local

namespace JellyfinMigrateMedia.Infrastructure.Db;

/// <summary>
/// Reads Jellyfin "libraries" (collection folders) from library.db.
/// Schema differs slightly across versions, so we use a few fallbacks.
/// </summary>
public sealed class JellyfinLibraryCatalog : IJellyfinLibraryCatalog
{
    private readonly IJellyfinDbConnectionFactory _connectionFactory;

    public JellyfinLibraryCatalog(IJellyfinDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<JellyfinLibraryInfo>> GetLibrariesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Preferred schema:
        // - CollectionFolders(Id, Name, CollectionType)
        // - MediaFolders(CollectionFolderId, Path)
        // Canonical Jellyfin model: TypedBaseItems contains CollectionFolder entities.
        if (await TableExistsAsync(conn, "TypedBaseItems", cancellationToken).ConfigureAwait(false))
        {
            var list = await ReadCollectionFoldersFromTypedBaseItemsAsync(conn, cancellationToken).ConfigureAwait(false);
            return
            [
                .. list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            ];
        }

        // If schema isn't recognized, return empty.
        return [];
    }

    public async Task<JellyfinLibraryInfo?> GetLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(libraryId))
            return null;

        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (await TableExistsAsync(conn, "TypedBaseItems", cancellationToken).ConfigureAwait(false))
        {
            // In this mode we treat libraryId as a display name key (best-effort).
            // If you need stable identity, extend JellyfinLibraryInfo with Guid later.
            var all = await ReadCollectionFoldersFromTypedBaseItemsAsync(conn, cancellationToken).ConfigureAwait(false);
            return all.FirstOrDefault(x => string.Equals(x.Name, libraryId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string? MapDbCollectionTypeToContentType(string? collectionType)
    {
        if (string.IsNullOrWhiteSpace(collectionType))
            return null;

        return collectionType.Trim().ToLowerInvariant() switch
        {
            "movies" => "Movie",
            "tvshows" => "Episode",
            "music" => "Audio",
            "photos" => "Photo",
            "homevideos" => "Video",
            _ => collectionType
        };
    }

    private static async Task<List<JellyfinLibraryInfo>> ReadCollectionFoldersFromTypedBaseItemsAsync(
        DbConnection conn,
        CancellationToken ct)
    {
        // Jellyfin stores items in TypedBaseItems. Collection folders have Type =
        // "MediaBrowser.Controller.Entities.CollectionFolder".
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """

                          SELECT guid, name, data
                          FROM TypedBaseItems
                          WHERE Type = $type;
                          """;
        var pType = cmd.CreateParameter();
        pType.ParameterName = "$type";
        pType.Value = "MediaBrowser.Controller.Entities.CollectionFolder";
        cmd.Parameters.Add(pType);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var list = new List<JellyfinLibraryInfo>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetString(1) : "Library";
            var (contentType, paths) = TryParseDataForCollectionFolder(reader, 2);
            list.Add(new JellyfinLibraryInfo(name, contentType, paths));
        }

        return list;
    }

    private static (string? ContentType, IReadOnlyList<ContentPath> Paths) TryParseDataForCollectionFolder(DbDataReader reader, int ordinal)
    {
        if (reader.FieldCount <= ordinal || reader.IsDBNull(ordinal))
            return (null, []);

        var val = reader.GetValue(ordinal);
        string? text = val as string;
        if (text is null && val is byte[] { Length: > 0 } bytes)
        {
            try { text = Encoding.UTF8.GetString(bytes); }
            catch { text = null; }
        }

        if (string.IsNullOrWhiteSpace(text))
            return (null, []);

        text = text.TrimStart();
        if (!text.StartsWith('{'))
            return (null, []);

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            string? contentType = null;
            if (root.TryGetProperty("CollectionType", out var ctEl) && ctEl.ValueKind == JsonValueKind.String)
                contentType = MapDbCollectionTypeToContentType(ctEl.GetString());

            var folderIds = new List<string>();
            var locations = new List<string>();

            if (root.TryGetProperty("PhysicalFolderIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idsEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var id = item.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                        folderIds.Add(id);
                }
            }

            if (root.TryGetProperty("PhysicalLocationsList", out var locEl) && locEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in locEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var p = item.GetString();
                    if (!string.IsNullOrWhiteSpace(p))
                        locations.Add(p);
                }
            }

            // Mapping rule from user:
            // - PhysicalLocationsList[0] has no id
            // - PhysicalLocationsList[1] -> PhysicalFolderIds[0]
            // - PhysicalLocationsList[2] -> PhysicalFolderIds[1] ...
            var contentPaths = new List<ContentPath>();
            for (var i = 0; i < locations.Count; i++)
            {
                var path = locations[i];
                var topId = i == 0 ? "" : (i - 1 < folderIds.Count ? folderIds[i - 1] : "");
                contentPaths.Add(new ContentPath(path, topId));
            }

            return (contentType, contentPaths);
        }
        catch
        {
            return (null, []);
        }
    }

    private static async Task<bool> TableExistsAsync(DbConnection conn, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = $name LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    private static async Task<string?> FindFirstColumnAsync(DbConnection conn, string tableName, IEnumerable<string> candidates, CancellationToken ct)
    {
        var cols = await GetTableColumnsAsync(conn, tableName, ct).ConfigureAwait(false);
        foreach (var c in candidates)
        {
            if (cols.Contains(c))
                return c;
        }
        return null;
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(DbConnection conn, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // PRAGMA table_info => columns: cid, name, type, notnull, dflt_value, pk
            var name = reader.GetString(1);
            set.Add(name);
        }
        return set;
    }

    private static async Task<List<(string Id, string Name, string? CollectionType)>> ReadCollectionFoldersAsync(DbConnection conn, CancellationToken ct)
    {
        var cols = await GetTableColumnsAsync(conn, "CollectionFolders", ct).ConfigureAwait(false);
        var idCol = cols.Contains("Id") ? "Id" : cols.FirstOrDefault(c => c.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) ?? "Id";
        const string nameCol = "Name";
        var typeCol = cols.Contains("CollectionType") ? "CollectionType" : (cols.Contains("Type") ? "Type" : null);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = typeCol is null
            ? $"SELECT {idCol}, {nameCol} FROM CollectionFolders;"
            : $"SELECT {idCol}, {nameCol}, {typeCol} FROM CollectionFolders;";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var list = new List<(string, string, string?)>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var name = reader.FieldCount > 1 && !reader.IsDBNull(1) ? reader.GetString(1) : id;
            string? t = null;
            if (typeCol is not null && reader.FieldCount > 2 && !reader.IsDBNull(2))
                t = reader.GetString(2);
            if (!string.IsNullOrWhiteSpace(id))
                list.Add((id, name, t));
        }

        return list;
    }

    private static async Task<Dictionary<string, List<string>>> ReadMediaFolderPathsAsync(DbConnection conn, CancellationToken ct)
    {
        var cols = await GetTableColumnsAsync(conn, "MediaFolders", ct).ConfigureAwait(false);
        var folderIdCol = cols.Contains("CollectionFolderId") ? "CollectionFolderId" : (cols.Contains("LibraryId") ? "LibraryId" : "CollectionFolderId");
        var pathCol = cols.Contains("Path") ? "Path" : (cols.Contains("PhysicalPath") ? "PhysicalPath" : "Path");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {folderIdCol}, {pathCol} FROM MediaFolders;";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;

            var id = reader.GetString(0);
            var path = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
                continue;

            if (!dict.TryGetValue(id, out var list))
                dict[id] = list = [];

            if (!list.Contains(path, StringComparer.OrdinalIgnoreCase))
                list.Add(path);
        }

        return dict;
    }

    private static async Task<Dictionary<string, List<string>>> ReadInlinePathsAsync(
        DbConnection conn,
        string tableName,
        string idColumn,
        string pathColumn,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {idColumn}, {pathColumn} FROM {tableName};";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;

            var id = reader.GetString(0);
            var path = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
                continue;

            if (!dict.TryGetValue(id, out var list))
                dict[id] = list = [];

            if (!list.Contains(path, StringComparer.OrdinalIgnoreCase))
                list.Add(path);
        }

        return dict;
    }
}

