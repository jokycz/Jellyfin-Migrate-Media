using System.Data.Common;
using JellyfinMigrateMedia.Infrastructure.Db;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class JellyfinTypedBaseItemsCloner
{
    /// <summary>
    /// Clones a TypedBaseItems row from sourceGuid to newGuid, overriding specific columns.
    /// This is intentionally generic so it can satisfy NOT NULL constraints by copying an existing row.
    /// </summary>
    public static async Task CloneAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid sourceGuidBytes,
        JellyfinGuid newGuidBytes,
        IReadOnlyDictionary<string, object?> overrides,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        if (sourceGuidBytes.Value == Guid.Empty) throw new ArgumentException("Invalid source guid.", nameof(sourceGuidBytes));
        if (newGuidBytes.Value == Guid.Empty) throw new ArgumentException("Invalid new guid.", nameof(newGuidBytes));

        var cols = await GetTableColumnsAsync(conn, tx, "TypedBaseItems", ct).ConfigureAwait(false);
        if (cols.Count == 0)
            throw new InvalidOperationException("Cannot read TypedBaseItems schema.");

        // Build: INSERT INTO TypedBaseItems (c1,c2,...) SELECT expr1,expr2,... FROM TypedBaseItems WHERE guid=$src LIMIT 1
        var columnList = string.Join(", ", cols.Select(EscapeIdent));

        var selectExprs = new List<string>(cols.Count);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        var pn = 0;

        foreach (var c in cols)
        {
            if (string.Equals(c, "guid", StringComparison.OrdinalIgnoreCase))
            {
                var p = $"$p{pn++}";
                AddParam(cmd, p, newGuidBytes.Bytes);
                selectExprs.Add(p);
                continue;
            }

            if (TryGetOverride(overrides, c, out var ov))
            {
                var p = $"$p{pn++}";
                AddParam(cmd, p, ov ?? DBNull.Value);
                selectExprs.Add(p);
                continue;
            }

            // Default: copy from source row
            selectExprs.Add(EscapeIdent(c));
        }

        AddParam(cmd, "$src", sourceGuidBytes.Bytes);

        cmd.CommandText = $@"
INSERT INTO TypedBaseItems ({columnList})
SELECT {string.Join(", ", selectExprs)}
FROM TypedBaseItems
WHERE guid = $src
LIMIT 1;";

        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (rows != 1)
            throw new InvalidOperationException($"Clone failed; inserted rows: {rows}.");
    }

    private static bool TryGetOverride(IReadOnlyDictionary<string, object?> overrides, string columnName, out object? value)
    {
        // Column names in Jellyfin DB are not always consistently cased.
        foreach (var kv in overrides)
        {
            if (string.Equals(kv.Key, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static async Task<List<string>> GetTableColumnsAsync(DbConnection conn, DbTransaction tx, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var cols = new List<string>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(1);
            cols.Add(name);
        }

        return cols;
    }

    private static string EscapeIdent(string ident)
    {
        // SQLite identifier quoting
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
}

