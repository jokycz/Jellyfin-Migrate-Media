using System.Data.Common;
using JellyfinMigrateMedia.Infrastructure.Db;

namespace JellyfinMigrateMedia.Host.Cli;

/// <summary>
/// Table-by-table cloning helpers.
/// Currently: PEOPLE only (per user request).
/// </summary>
internal static class JellyfinItemRelationsCloner
{
    public static async Task CopyPeopleAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceItemId.Value == Guid.Empty) throw new ArgumentException("Invalid source item id.", nameof(sourceItemId));
        if (newItemId.Value == Guid.Empty) throw new ArgumentException("Invalid new item id.", nameof(newItemId));

        // Don't enumerate all tables; target the known people table.
        // In this project we use ItemPeople. If it doesn't exist, we skip.
        const string peopleTable = "People";
        if (!await TableExistsAsync(conn, tx, peopleTable, ct).ConfigureAwait(false))
            return;

        var cols = await GetTableInfoAsync(conn, tx, peopleTable, ct).ConfigureAwait(false);
        if (!cols.Any(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return;

        await CloneRowsByItemIdAsync(conn, tx, peopleTable, cols, sourceItemId, newItemId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Clone per-user playback state from UserDatas.
    /// In your DB, the item key is stored as GUID text with dashes.
    /// Supports both common layouts:
    /// - UserDatas has column Key (TEXT)
    /// - UserDatas has column ItemId (BLOB or TEXT)
    /// </summary>
    public static async Task CopyUserDatasAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceItemId.Value == Guid.Empty) throw new ArgumentException("Invalid source item id.", nameof(sourceItemId));
        if (newItemId.Value == Guid.Empty) throw new ArgumentException("Invalid new item id.", nameof(newItemId));

        const string tableName = "UserDatas";
        if (!await TableExistsAsync(conn, tx, tableName, ct).ConfigureAwait(false))
            return;

        var cols = await GetTableInfoAsync(conn, tx, tableName, ct).ConfigureAwait(false);
        if (!cols.Any(c => c.Name.Equals("Key", StringComparison.OrdinalIgnoreCase)) &&
            !cols.Any(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return;

        await CloneUserDatasRowsAsync(conn, tx, tableName, cols, sourceItemId, newItemId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Clone ItemValues rows (keyed by ItemId). Duplicate detection: whole-row compare (excluding integer PK).
    /// Returns counts for logging/verification.
    /// </summary>
    public static async Task<(int selected, int inserted, int skippedExisting)> CopyItemValuesAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceItemId.Value == Guid.Empty) throw new ArgumentException("Invalid source item id.", nameof(sourceItemId));
        if (newItemId.Value == Guid.Empty) throw new ArgumentException("Invalid new item id.", nameof(newItemId));

        const string tableName = "ItemValues";
        if (!await TableExistsAsync(conn, tx, tableName, ct).ConfigureAwait(false))
            return (0, 0, 0);

        var cols = await GetTableInfoAsync(conn, tx, tableName, ct).ConfigureAwait(false);
        if (!cols.Any(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        return await CloneItemValuesRowsAsync(conn, tx, tableName, cols, sourceItemId, newItemId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Clone Chapters2 rows (keyed by ItemId). Duplicate detection: whole-row compare (excluding integer PK).
    /// Returns counts for logging/verification.
    /// </summary>
    public static async Task<(int selected, int inserted, int skippedExisting)> CopyChapters2Async(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceItemId.Value == Guid.Empty) throw new ArgumentException("Invalid source item id.", nameof(sourceItemId));
        if (newItemId.Value == Guid.Empty) throw new ArgumentException("Invalid new item id.", nameof(newItemId));

        const string tableName = "Chapters2";
        if (!await TableExistsAsync(conn, tx, tableName, ct).ConfigureAwait(false))
            return (0, 0, 0);

        var cols = await GetTableInfoAsync(conn, tx, tableName, ct).ConfigureAwait(false);
        if (!cols.Any(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        return await CloneChapters2RowsAsync(conn, tx, tableName, cols, sourceItemId, newItemId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Clone MediaAttachments rows (keyed by ItemId).
    /// NOTE: no filesystem operations are performed (attachments are part of the media container).
    /// Duplicate detection: whole-row compare (excluding integer PK).
    /// Returns counts for logging/verification.
    /// </summary>
    public static async Task<(int selected, int inserted, int skippedExisting)> CopyMediaAttachmentsAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceItemId.Value == Guid.Empty) throw new ArgumentException("Invalid source item id.", nameof(sourceItemId));
        if (newItemId.Value == Guid.Empty) throw new ArgumentException("Invalid new item id.", nameof(newItemId));

        const string tableName = "MediaAttachments";
        if (!await TableExistsAsync(conn, tx, tableName, ct).ConfigureAwait(false))
            return (0, 0, 0);

        var cols = await GetTableInfoAsync(conn, tx, tableName, ct).ConfigureAwait(false);
        if (!cols.Any(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        return await CloneMediaAttachmentsRowsAsync(conn, tx, tableName, cols, sourceItemId, newItemId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Clone MediaStreams rows (keyed by ItemId). Duplicate detection: whole-row compare (excluding integer PK).
    /// Returns counts for logging/verification.
    /// </summary>
    public static async Task<(int selected, int inserted, int skippedExisting)> CopyMediaStreamsAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceItemId.Value == Guid.Empty) throw new ArgumentException("Invalid source item id.", nameof(sourceItemId));
        if (newItemId.Value == Guid.Empty) throw new ArgumentException("Invalid new item id.", nameof(newItemId));

        const string tableName = "MediaStreams";
        if (!await TableExistsAsync(conn, tx, tableName, ct).ConfigureAwait(false))
            return (0, 0, 0);

        var cols = await GetTableInfoAsync(conn, tx, tableName, ct).ConfigureAwait(false);
        if (!cols.Any(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        return await CloneMediaStreamsRowsAsync(conn, tx, tableName, cols, sourceItemId, newItemId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures AncestorIds rows exist for a single item by walking TypedBaseItems.ParentId and inserting (ItemId, AncestorId)
    /// for each ancestor above the item. Duplicate detection: ItemId+AncestorId only.
    /// </summary>
    public static async Task<(int chainLength, int relationsInserted, int relationsSkippedExisting)> EnsureAncestorIdsForItemAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid itemId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (itemId.Value == Guid.Empty) throw new ArgumentException("Invalid item id.", nameof(itemId));

        const string tableName = "AncestorIds";
        if (!await TableExistsAsync(conn, tx, tableName, ct).ConfigureAwait(false))
            return (0, 0, 0);

        var aCols = await GetTableInfoAsync(conn, tx, tableName, ct).ConfigureAwait(false);
        var itemIdCol = aCols.FirstOrDefault(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase));
        var ancestorIdCol = aCols.FirstOrDefault(c => c.Name.Equals("AncestorId", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(itemIdCol.Name) || string.IsNullOrWhiteSpace(ancestorIdCol.Name))
            return (0, 0, 0);

        // Build chain: leaf, parent, ..., root
        var chain = new List<JellyfinGuid>(capacity: 8) { itemId };
        var seen = new HashSet<Guid> { itemId.Value };

        var current = itemId;
        for (var depth = 0; depth < 128; depth++)
        {
            ct.ThrowIfCancellationRequested();

            var parent = await TryGetParentIdAsync(conn, tx, current, ct).ConfigureAwait(false);
            if (parent.Value == Guid.Empty)
                break;
            if (!seen.Add(parent.Value))
                break; // cycle safety

            chain.Add(parent);
            current = parent;
        }

        var relationsInserted = 0;
        var relationsSkippedExisting = 0;

        // For this item, ancestors are the nodes above it (excluding itself).
        for (var j = 1; j < chain.Count; j++)
        {
            ct.ThrowIfCancellationRequested();

            var anc = chain[j];

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [itemIdCol.Name] = CoerceGuidForColumn(itemIdCol, itemId),
                [ancestorIdCol.Name] = CoerceGuidForColumn(ancestorIdCol, anc),
            };

            if (await RowExistsByColumnsAsync(conn, tx, tableName, [itemIdCol.Name, ancestorIdCol.Name], row, ct).ConfigureAwait(false))
            {
                relationsSkippedExisting++;
                continue;
            }

            await InsertRowAsync(conn, tx, tableName, [itemIdCol.Name, ancestorIdCol.Name], row, ct).ConfigureAwait(false);
            relationsInserted++;
        }

        return (chain.Count, relationsInserted, relationsSkippedExisting);
    }

    private static async Task CloneRowsByItemIdAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<ColumnInfo> cols,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        // If table has an INTEGER single-column PK (typical AUTOINCREMENT id), omit it so SQLite can generate a new value.
        var pkCols = cols.Where(c => c.Pk > 0).OrderBy(c => c.Pk).ToList();
        var insertCols = cols.Select(c => c.Name).ToList();

        if (pkCols.Count == 1)
        {
            var pk = pkCols[0];
            var pkIsIntegerish = (pk.Type ?? "").Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                                 pk.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

            if (pkIsIntegerish && !pk.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase))
                insertCols.RemoveAll(c => c.Equals(pk.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return;

        // Robust idempotency:
        // - Read source rows
        // - Rewrite ItemId to target
        // - Before INSERT, check if an identical row already exists (by comparing all insertCols)
        var selectColsSql = string.Join(", ", insertCols.Select(EscapeIdent));

        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;
        cmdSel.CommandText = $"SELECT {selectColsSql} FROM {EscapeIdent(tableName)} WHERE ItemId = $srcItemId;";
        AddParam(cmdSel, "$srcItemId", sourceItemId.Bytes);

        await using var reader = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < insertCols.Count; i++)
            {
                var name = insertCols[i];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            // Rewrite ItemId to new item
            row["ItemId"] = newItemId.Bytes;

            if (await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false))
                continue;

            await InsertRowAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
        }
    }

    private static async Task CloneUserDatasRowsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<ColumnInfo> cols,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        // Omit integer PK if present (AUTOINCREMENT)
        var pkCols = cols.Where(c => c.Pk > 0).OrderBy(c => c.Pk).ToList();
        var insertCols = cols.Select(c => c.Name).ToList();

        if (pkCols.Count == 1)
        {
            var pk = pkCols[0];
            var pkIsIntegerish = (pk.Type ?? "").Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                                 pk.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

            if (pkIsIntegerish &&
                !pk.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase) &&
                !pk.Name.Equals("Key", StringComparison.OrdinalIgnoreCase))
            {
                insertCols.RemoveAll(c => c.Equals(pk.Name, StringComparison.OrdinalIgnoreCase));
            }
        }

        var hasKey = insertCols.Any(c => c.Equals("Key", StringComparison.OrdinalIgnoreCase));
        var hasItemId = insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase));
        if (!hasKey && !hasItemId)
            return;

        var selectColsSql = string.Join(", ", insertCols.Select(EscapeIdent));

        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;

        if (hasKey)
        {
            // Key is TEXT (dashed guid) in your DB.
            cmdSel.CommandText = $"SELECT {selectColsSql} FROM {EscapeIdent(tableName)} WHERE lower({EscapeIdent("Key")}) = lower($srcKey);";
            AddParam(cmdSel, "$srcKey", sourceItemId.Dashed);
        }
        else
        {
            // ItemId stored directly (commonly BLOB).
            cmdSel.CommandText = $"SELECT {selectColsSql} FROM {EscapeIdent(tableName)} WHERE ItemId = $srcItemId;";
            AddParam(cmdSel, "$srcItemId", sourceItemId.Bytes);
        }

        await using var reader = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < insertCols.Count; i++)
            {
                var name = insertCols[i];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            if (hasKey)
                row["Key"] = newItemId.Dashed;
            if (hasItemId)
                row["ItemId"] = CoerceItemIdValue(cols, newItemId);

            if (await UserDatasRowExistsAsync(conn, tx, tableName, insertCols, row, hasKey, ct).ConfigureAwait(false))
                continue;

            await InsertRowAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
        }
    }

    private static async Task<(int selected, int inserted, int skippedExisting)> CloneItemValuesRowsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<ColumnInfo> cols,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        var selected = 0;
        var inserted = 0;
        var skippedExisting = 0;

        // Omit integer PK if present (AUTOINCREMENT)
        var pkCols = cols.Where(c => c.Pk > 0).OrderBy(c => c.Pk).ToList();
        var insertCols = cols.Select(c => c.Name).ToList();

        if (pkCols.Count == 1)
        {
            var pk = pkCols[0];
            var pkIsIntegerish = (pk.Type ?? "").Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                                 pk.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

            if (pkIsIntegerish && !pk.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase))
                insertCols.RemoveAll(c => c.Equals(pk.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        var selectColsSql = string.Join(", ", insertCols.Select(EscapeIdent));

        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;
        // ItemId can be stored as BLOB or as TEXT (often dashed GUID) depending on Jellyfin version/schema.
        // Make the source-row lookup tolerant to both representations.
        cmdSel.CommandText = $@"
SELECT {selectColsSql}
FROM {EscapeIdent(tableName)}
WHERE ItemId = $srcBlob
   OR lower(CAST(ItemId AS TEXT)) = lower($srcDashed)
   OR lower(CAST(ItemId AS TEXT)) = lower($srcNoDashes)
   OR lower(hex(ItemId)) = lower($srcNoDashes);";
        AddParam(cmdSel, "$srcBlob", sourceItemId.Bytes);
        AddParam(cmdSel, "$srcDashed", sourceItemId.Dashed);
        AddParam(cmdSel, "$srcNoDashes", sourceItemId.NoDashes);

        await using var reader = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            selected++;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < insertCols.Count; i++)
            {
                var name = insertCols[i];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            // Rewrite ItemId to new item; preserve original storage shape (TEXT vs BLOB).
            row["ItemId"] = RewriteItemIdValue(row.TryGetValue("ItemId", out var old) ? old : null, newItemId);

            // Duplicate check: whole-row compare (as requested).
            if (await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false))
            {
                skippedExisting++;
                continue;
            }

            await InsertRowAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
            inserted++;
        }

        return (selected, inserted, skippedExisting);
    }

    private static async Task<(int selected, int inserted, int skippedExisting)> CloneChapters2RowsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<ColumnInfo> cols,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        var selected = 0;
        var inserted = 0;
        var skippedExisting = 0;

        // Omit integer PK if present (AUTOINCREMENT)
        var pkCols = cols.Where(c => c.Pk > 0).OrderBy(c => c.Pk).ToList();
        var insertCols = cols.Select(c => c.Name).ToList();

        if (pkCols.Count == 1)
        {
            var pk = pkCols[0];
            var pkIsIntegerish = (pk.Type ?? "").Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                                 pk.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

            if (pkIsIntegerish && !pk.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase))
                insertCols.RemoveAll(c => c.Equals(pk.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        var selectColsSql = string.Join(", ", insertCols.Select(EscapeIdent));

        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;

        // ItemId can be stored as BLOB or as TEXT depending on Jellyfin version/schema.
        cmdSel.CommandText = $@"
SELECT {selectColsSql}
FROM {EscapeIdent(tableName)}
WHERE ItemId = $srcBlob
   OR lower(CAST(ItemId AS TEXT)) = lower($srcDashed)
   OR lower(CAST(ItemId AS TEXT)) = lower($srcNoDashes)
   OR lower(hex(ItemId)) = lower($srcNoDashes);";
        AddParam(cmdSel, "$srcBlob", sourceItemId.Bytes);
        AddParam(cmdSel, "$srcDashed", sourceItemId.Dashed);
        AddParam(cmdSel, "$srcNoDashes", sourceItemId.NoDashes);

        await using var reader = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            selected++;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < insertCols.Count; i++)
            {
                var name = insertCols[i];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            row["ItemId"] = RewriteItemIdValue(row.TryGetValue("ItemId", out var old) ? old : null, newItemId);

            if (await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false))
            {
                skippedExisting++;
                continue;
            }

            await InsertRowAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
            inserted++;
        }

        return (selected, inserted, skippedExisting);
    }

    private static async Task<(int selected, int inserted, int skippedExisting)> CloneMediaStreamsRowsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<ColumnInfo> cols,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        var selected = 0;
        var inserted = 0;
        var skippedExisting = 0;

        // Omit integer PK if present (AUTOINCREMENT)
        var pkCols = cols.Where(c => c.Pk > 0).OrderBy(c => c.Pk).ToList();
        var insertCols = cols.Select(c => c.Name).ToList();

        if (pkCols.Count == 1)
        {
            var pk = pkCols[0];
            var pkIsIntegerish = (pk.Type ?? "").Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                                 pk.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

            if (pkIsIntegerish && !pk.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase))
                insertCols.RemoveAll(c => c.Equals(pk.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        var selectColsSql = string.Join(", ", insertCols.Select(EscapeIdent));

        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;

        // ItemId can be stored as BLOB or as TEXT depending on Jellyfin version/schema.
        cmdSel.CommandText = $@"
SELECT {selectColsSql}
FROM {EscapeIdent(tableName)}
WHERE ItemId = $srcBlob
   OR lower(CAST(ItemId AS TEXT)) = lower($srcDashed)
   OR lower(CAST(ItemId AS TEXT)) = lower($srcNoDashes)
   OR lower(hex(ItemId)) = lower($srcNoDashes);";
        AddParam(cmdSel, "$srcBlob", sourceItemId.Bytes);
        AddParam(cmdSel, "$srcDashed", sourceItemId.Dashed);
        AddParam(cmdSel, "$srcNoDashes", sourceItemId.NoDashes);

        await using var reader = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            selected++;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < insertCols.Count; i++)
            {
                var name = insertCols[i];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            row["ItemId"] = RewriteItemIdValue(row.TryGetValue("ItemId", out var old) ? old : null, newItemId);

            if (await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false))
            {
                skippedExisting++;
                continue;
            }

            await InsertRowAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
            inserted++;
        }

        return (selected, inserted, skippedExisting);
    }

    private static async Task<(int selected, int inserted, int skippedExisting)> CloneMediaAttachmentsRowsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<ColumnInfo> cols,
        JellyfinGuid sourceItemId,
        JellyfinGuid newItemId,
        CancellationToken ct)
    {
        var selected = 0;
        var inserted = 0;
        var skippedExisting = 0;

        // Omit integer PK if present (AUTOINCREMENT)
        var pkCols = cols.Where(c => c.Pk > 0).OrderBy(c => c.Pk).ToList();
        var insertCols = cols.Select(c => c.Name).ToList();

        if (pkCols.Count == 1)
        {
            var pk = pkCols[0];
            var pkIsIntegerish = (pk.Type ?? "").Contains("INT", StringComparison.OrdinalIgnoreCase) ||
                                 pk.Name.Equals("Id", StringComparison.OrdinalIgnoreCase);

            if (pkIsIntegerish && !pk.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase))
                insertCols.RemoveAll(c => c.Equals(pk.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (!insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
            return (0, 0, 0);

        var selectColsSql = string.Join(", ", insertCols.Select(EscapeIdent));

        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;

        // ItemId can be stored as BLOB or as TEXT depending on Jellyfin version/schema.
        cmdSel.CommandText = $@"
SELECT {selectColsSql}
FROM {EscapeIdent(tableName)}
WHERE ItemId = $srcBlob
   OR lower(CAST(ItemId AS TEXT)) = lower($srcDashed)
   OR lower(CAST(ItemId AS TEXT)) = lower($srcNoDashes)
   OR lower(hex(ItemId)) = lower($srcNoDashes);";
        AddParam(cmdSel, "$srcBlob", sourceItemId.Bytes);
        AddParam(cmdSel, "$srcDashed", sourceItemId.Dashed);
        AddParam(cmdSel, "$srcNoDashes", sourceItemId.NoDashes);

        await using var reader = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            selected++;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < insertCols.Count; i++)
            {
                var name = insertCols[i];
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            row["ItemId"] = RewriteItemIdValue(row.TryGetValue("ItemId", out var oldItemId) ? oldItemId : null, newItemId);

            if (await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false))
            {
                skippedExisting++;
                continue;
            }

            await InsertRowAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
            inserted++;
        }

        return (selected, inserted, skippedExisting);
    }

    private static async Task<bool> UserDatasRowExistsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<string> insertCols,
        Dictionary<string, object?> row,
        bool hasKey,
        CancellationToken ct)
    {
        // User asked: duplicate check should be only by Key + UserId.
        // If Key doesn't exist, fall back to ItemId + UserId.

        var userIdCol =
            insertCols.FirstOrDefault(c => c.Equals("UserId", StringComparison.OrdinalIgnoreCase)) ??
            insertCols.FirstOrDefault(c => c.Equals("UserGuid", StringComparison.OrdinalIgnoreCase)) ??
            insertCols.FirstOrDefault(c => c.Equals("UserID", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(userIdCol))
        {
            // Can't do the requested check; fall back to full-row compare for safety.
            return await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
        }

        if (hasKey)
        {
            return await RowExistsByColumnsAsync(conn, tx, tableName, ["Key", userIdCol], row, ct).ConfigureAwait(false);
        }

        // No Key; try ItemId + UserId.
        if (insertCols.Any(c => c.Equals("ItemId", StringComparison.OrdinalIgnoreCase)))
        {
            return await RowExistsByColumnsAsync(conn, tx, tableName, ["ItemId", userIdCol], row, ct).ConfigureAwait(false);
        }

        return await RowExistsAsync(conn, tx, tableName, insertCols, row, ct).ConfigureAwait(false);
    }

    private static async Task<bool> RowExistsByColumnsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<string> keyCols,
        Dictionary<string, object?> values,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        var whereParts = new List<string>(keyCols.Count);
        var pn = 0;

        foreach (var c in keyCols)
        {
            // keyCols might contain "Key"/"ItemId" in canonical form; values dict is case-insensitive.
            if (!values.TryGetValue(c, out var v) || v is null)
            {
                whereParts.Add($"{EscapeIdent(c)} IS NULL");
                continue;
            }

            var pName = $"$p{pn++}";
            whereParts.Add($"{EscapeIdent(c)} = {pName}");
            var p = cmd.CreateParameter();
            p.ParameterName = pName;
            p.Value = v;
            cmd.Parameters.Add(p);
        }

        cmd.CommandText = $"SELECT 1 FROM {EscapeIdent(tableName)} WHERE {string.Join(" AND ", whereParts)} LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    private static object CoerceItemIdValue(List<ColumnInfo> cols, JellyfinGuid itemId)
    {
        var itemIdCol = cols.FirstOrDefault(c => c.Name.Equals("ItemId", StringComparison.OrdinalIgnoreCase));
        var t = (itemIdCol.Type ?? "").ToLowerInvariant();
        if (t.Contains("text") || t.Contains("char") || t.Contains("clob"))
            return itemId.Dashed;
        return itemId.Bytes;
    }

    private static object RewriteItemIdValue(object? oldItemIdValue, JellyfinGuid newItemId)
    {
        // Preserve original storage "shape":
        // - if source row had TEXT -> store dashed GUID
        // - if source row had BLOB -> store Jellyfin BLOB bytes
        return (oldItemIdValue is string) ? newItemId.Dashed :
             newItemId.Bytes;
    }

    private static object CoerceGuidForColumn(ColumnInfo col, JellyfinGuid id)
    {
        var t = (col.Type ?? "").ToLowerInvariant();
        if (t.Contains("text") || t.Contains("char") || t.Contains("clob"))
            return id.Dashed;
        return id.Bytes;
    }

    private static async Task<JellyfinGuid> TryGetParentIdAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid itemGuid,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT ParentId
FROM TypedBaseItems
WHERE guid = $guid
LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$guid";
        p.Value = itemGuid.Bytes;
        cmd.Parameters.Add(p);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result == DBNull.Value)
            return default;

        // ParentId is usually BLOB guid.
        if (result is byte[] { Length: 16 } bytes)
            return JellyfinGuid.FromBytes(bytes);

        // Some DBs might store ParentId as TEXT.
        var s = Convert.ToString(result);
        if (JellyfinGuid.TryParse(s, out var parsed))
            return parsed;

        return default;
    }


    private static async Task<bool> RowExistsAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<string> cols,
        Dictionary<string, object?> values,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        var whereParts = new List<string>(cols.Count);
        var pn = 0;

        foreach (var c in cols)
        {
            if (!values.TryGetValue(c, out var v) || v is null)
            {
                whereParts.Add($"{EscapeIdent(c)} IS NULL");
                continue;
            }

            var pName = $"$p{pn++}";
            whereParts.Add($"{EscapeIdent(c)} = {pName}");
            var p = cmd.CreateParameter();
            p.ParameterName = pName;
            p.Value = v;
            cmd.Parameters.Add(p);
        }

        cmd.CommandText = $"SELECT 1 FROM {EscapeIdent(tableName)} WHERE {string.Join(" AND ", whereParts)} LIMIT 1;";
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    private static async Task InsertRowAsync(
        DbConnection conn,
        DbTransaction tx,
        string tableName,
        List<string> cols,
        Dictionary<string, object?> values,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        var colsSql = string.Join(", ", cols.Select(EscapeIdent));
        var pSql = string.Join(", ", cols.Select((_, i) => $"$p{i}"));

        cmd.CommandText = $"INSERT INTO {EscapeIdent(tableName)} ({colsSql}) VALUES ({pSql});";

        for (var i = 0; i < cols.Count; i++)
        {
            var col = cols[i];
            var p = cmd.CreateParameter();
            p.ParameterName = $"$p{i}";
            p.Value = values.TryGetValue(col, out var v) ? (v ?? DBNull.Value) : DBNull.Value;
            cmd.Parameters.Add(p);
        }

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(DbConnection conn, DbTransaction tx, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT 1
FROM sqlite_master
WHERE type = 'table'
  AND lower(name) = lower($name)
LIMIT 1;";
        AddParam(cmd, "$name", tableName);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result != DBNull.Value;
    }

    private static async Task<List<ColumnInfo>> GetTableInfoAsync(DbConnection conn, DbTransaction tx, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({EscapeIdent(tableName)});";

        var cols = new List<ColumnInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            // PRAGMA table_info => cid, name, type, notnull, dflt_value, pk
            var name = r.IsDBNull(1) ? "" : r.GetString(1);
            var type = r.IsDBNull(2) ? "" : r.GetString(2);
            var pk = r.IsDBNull(5) ? 0 : r.GetInt32(5);
            cols.Add(new ColumnInfo(name, type, pk));
        }

        return cols;
    }

    private static string EscapeIdent(string ident)
    {
        ident ??= "";
        ident = ident.Replace("\"", "\"\"");
        return $"\"{ident}\"";
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private readonly record struct ColumnInfo(string Name, string Type, int Pk);
}

