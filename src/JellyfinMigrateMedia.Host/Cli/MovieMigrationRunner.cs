using System.Data.Common;
using System.Text;
using System.Text.Json;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;
using Serilog;
using Serilog.Formatting.Compact;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class MovieMigrationRunner
{
    public static async Task RunAsync(
        MigrationProfile profile,
        IJellyfinDbConnectionFactory dbConnectionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(dbConnectionFactory);

        var src = profile.Sources.FirstOrDefault()
                  ?? throw new InvalidOperationException("Profile has no source.");

        if (!string.Equals((src.ContentType ?? "").Trim(), "Movie", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pipeline currently supports only Movie profiles.");

        if (string.IsNullOrWhiteSpace(src.TopId))
            throw new InvalidOperationException("Missing SOURCE TopId.");

        if (string.IsNullOrWhiteSpace(profile.Destination.TargetPath))
            throw new InvalidOperationException("Missing TARGET path.");

        if (string.IsNullOrWhiteSpace(profile.Destination.TargetTopId))
            throw new InvalidOperationException("Missing TARGET TopId.");

        // Non-interactive: always apply safe operations and DB cloning.

        var outDir = PlanFiles.PlansDir;
        Directory.CreateDirectory(outDir);
        // JSON Lines (one JSON object per line) - easy to parse/stream.
        var outLog = Path.Combine(outDir, $"pipeline-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");

        var runId = Guid.NewGuid().ToString("N");

        using var log = new LoggerConfiguration()
            .MinimumLevel.Information()
            // Compact JSON suitable for parsing; one event per line.
            .WriteTo.File(new RenderedCompactJsonFormatter(), outLog)
            .CreateLogger();

        log.Information(
            "pipeline_started",
            new
            {
                runId,
                profileId = profile.Id,
                profileName = profile.Name,
                sourceTopId = src.TopId,
                targetTopId = profile.Destination.TargetTopId,
                targetPath = profile.Destination.TargetPath,
                startedAt = DateTimeOffset.Now
            });

        await using var conn = await dbConnectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var treeBuilder = await JellyfinDbTreeBuilder.CreateAsync(conn, tx, JellyfinGuid.Parse(profile.Destination.TargetTopId!), cancellationToken)
            .ConfigureAwait(false);

        var movies = await ReadMoviesAsync(conn, tx, JellyfinGuid.Parse(src.TopId!), cancellationToken).ConfigureAwait(false);

        var processed = 0;
        var mkdirOk = 0;
        var dbTreeOk = 0;
        var dbMovieOk = 0;
        var failed = 0;

        foreach (var m in movies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processed++;

            try
            {
                // Step 1) Compute new name/path
                var ext = Path.GetExtension(m.OldPath ?? "");
                var extNoDot = string.IsNullOrWhiteSpace(ext) ? "" : ext.TrimStart('.');

                var folder = MigrationTemplateRenderer.Render(
                    profile.Naming.MovieFolderTemplate,
                    m.MovieName,
                    m.OriginalName ?? "",
                    m.Year,
                    extNoDot);

                var fileName = MigrationTemplateRenderer.Render(
                    profile.Naming.MovieFileTemplate,
                    m.MovieName,
                    m.OriginalName ?? "",
                    m.Year,
                    extNoDot);

                var newPath = Path.Combine(profile.Destination.TargetPath!, folder, fileName);
                var newDir = Path.GetDirectoryName(newPath) ?? profile.Destination.TargetPath!;

                // Jellyfin DB typically enforces a UNIQUE constraint on TypedBaseItems.Path.
                // We must NOT auto-suffix filenames. If Path already exists, reuse the existing item GUID.
                var existingGuidByPath = await TryGetTypedBaseItemGuidByPathAsync(conn, tx, newPath, cancellationToken).ConfigureAwait(false);

                log.Information("{Event} {@Data}", "movie_planned", new
                {
                    runId,
                    sourceGuidHex = m.GuidBytes.Hex,
                    movieName = m.MovieName,
                    oldPath = m.OldPath,
                    newPath,
                    newDir,
                    existingGuid = existingGuidByPath.Value == Guid.Empty ? null : existingGuidByPath.NoDashes
                });

                // Step 2) FS mkdir (idempotent)
                Directory.CreateDirectory(newDir);
                mkdirOk++;
                log.Information("{Event} {@Data}", "fs_mkdir", new { runId, newDir });

                // Step 3) DB tree (folders)
                var parentFolderGuid = await treeBuilder.EnsureFolderTreeAsync(newDir, cancellationToken).ConfigureAwait(false);
                dbTreeOk++;
                log.Information("{Event} {@Data}", "db_tree_ok", new { runId, parentFolder = parentFolderGuid.NoDashes });

                // Step 4) Create new Movie in DB (clone)
                JellyfinGuid newGuid;
                var reusedExisting = existingGuidByPath.Value != Guid.Empty;
                if (reusedExisting)
                {
                    // Resume: a previous run likely created the TypedBaseItems row but didn't finish.
                    // Verify it's a Movie and make sure it's linked under the correct parent folder.
                    newGuid = existingGuidByPath;
                    var parentUpdated = await EnsureExistingMovieParentAsync(conn, tx, newGuid, parentFolderGuid, cancellationToken)
                        .ConfigureAwait(false);
                    log.Information("{Event} {@Data}", "db_movie_reused_by_path", new { runId, newGuid = newGuid.NoDashes, path = newPath, parentUpdated });
                }
                else
                {
                    newGuid = new JellyfinGuid(Guid.NewGuid());
                    await JellyfinTypedBaseItemsCloner.CloneAsync(
                            conn,
                            tx,
                            m.GuidBytes,
                            newGuid,
                            new Dictionary<string, object?>
                            {
                                ["Path"] = newPath,
                                ["ParentId"] = parentFolderGuid.Bytes,
                                // Store as TEXT ("N") because TopParentId can be stored inconsistently.
                                ["TopParentId"] = treeBuilder.TopParentGuidBytes.NoDashes,
                                // Keep "name" as-is; user-friendly title can remain the same.
                                // NOTE: "data" JSON likely contains path/media sources; we'll adjust later when we wire copy operations.
                            },
                            cancellationToken)
                        .ConfigureAwait(false);

                    dbMovieOk++;
                    log.Information("{Event} {@Data}", "db_movie_cloned", new { runId, sourceGuidHex = m.GuidBytes.Hex, newGuid = newGuid.NoDashes, path = newPath });
                }

                // Step 5) AncestorIds (Movie)
                var (ancChainLength, ancInserted, ancSkippedExisting) =
                    await JellyfinItemRelationsCloner.EnsureAncestorIdsForItemAsync(conn, tx, newGuid, cancellationToken)
                        .ConfigureAwait(false);
                log.Information(
                    "{Event} {@Data}",
                    "db_ancestorids_ok",
                    new { runId, kind = "movie", itemId = newGuid.NoDashes, chainLength = ancChainLength, inserted = ancInserted, skippedExisting = ancSkippedExisting });

                // Step 6) PEOPLE
                await JellyfinItemRelationsCloner.CopyPeopleAsync(conn, tx, m.GuidBytes, newGuid, cancellationToken)
                    .ConfigureAwait(false);
                log.Information("{Event} {@Data}", "db_people_ok", new { runId, newGuid = newGuid.NoDashes });

                // Step 7) UserDatas (watched/progress)
                await JellyfinItemRelationsCloner.CopyUserDatasAsync(conn, tx, m.GuidBytes, newGuid, cancellationToken)
                    .ConfigureAwait(false);
                log.Information("{Event} {@Data}", "db_userdatas_ok", new { runId, newGuid = newGuid.NoDashes });

                // Step 8) ItemValues (per-item values; duplicate check = whole row)
                var (ivSelected, ivInserted, ivSkippedExisting) =
                    await JellyfinItemRelationsCloner.CopyItemValuesAsync(conn, tx, m.GuidBytes, newGuid, cancellationToken)
                        .ConfigureAwait(false);
                log.Information("{Event} {@Data}", "db_itemvalues_ok", new { runId, newGuid = newGuid.NoDashes, selected = ivSelected, inserted = ivInserted, skippedExisting = ivSkippedExisting });

                // Step 9) Chapters2 (per-item chapters; duplicate check = whole row)
                var (chSelected, chInserted, chSkippedExisting) =
                    await JellyfinItemRelationsCloner.CopyChapters2Async(conn, tx, m.GuidBytes, newGuid, cancellationToken)
                        .ConfigureAwait(false);
                log.Information("{Event} {@Data}", "db_chapters2_ok", new { runId, newGuid = newGuid.NoDashes, selected = chSelected, inserted = chInserted, skippedExisting = chSkippedExisting });

                // Step 10) MediaAttachments (DB-only; no filesystem operations)
                var (maSelected, maInserted, maSkippedExisting) =
                    await JellyfinItemRelationsCloner.CopyMediaAttachmentsAsync(conn, tx, m.GuidBytes, newGuid, cancellationToken)
                        .ConfigureAwait(false);
                log.Information("{Event} {@Data}", "db_mediaattachments_ok", new { runId, newGuid = newGuid.NoDashes, selected = maSelected, inserted = maInserted, skippedExisting = maSkippedExisting });

                // Step 11) MediaStreams (audio/video/subtitle stream metadata)
                var (msSelected, msInserted, msSkippedExisting) =
                    await JellyfinItemRelationsCloner.CopyMediaStreamsAsync(conn, tx, m.GuidBytes, newGuid, cancellationToken)
                        .ConfigureAwait(false);
                log.Information("{Event} {@Data}", "db_mediastreams_ok", new { runId, newGuid = newGuid.NoDashes, selected = msSelected, inserted = msInserted, skippedExisting = msSkippedExisting });

                // Step 5+
                // TODO (attributes, watched state, delete old)
                log.Information("{Event} {@Data}", "todo_delete_old", new { runId, newGuid = newGuid.NoDashes });
            }
            catch (Exception ex)
            {
                failed++;
                log.Error(ex, "{Event} {@Data}", "movie_failed", new { runId, sourceGuidHex = m.GuidBytes.Hex });
            }
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        log.Information(
            "{Event} {@Data}",
            "pipeline_done",
            new
            {
                runId,
                doneAt = DateTimeOffset.Now,
                processed,
                fsMkdirOk = mkdirOk,
                dbTreeOk,
                dbMovieCloned = dbMovieOk,
                failed
            });

        Console.WriteLine($"Pipeline log: {outLog}");
    }

    private static async Task<JellyfinGuid> TryGetTypedBaseItemGuidByPathAsync(
        DbConnection conn,
        DbTransaction tx,
        string path,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
SELECT guid
FROM TypedBaseItems
WHERE lower(Path) = lower($path)
LIMIT 1;";

        var p = cmd.CreateParameter();
        p.ParameterName = "$path";
        p.Value = path;
        cmd.Parameters.Add(p);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result == DBNull.Value)
            return default;
        return JellyfinGuid.FromBytes((byte[])result);
    }

    private static async Task<bool> EnsureExistingMovieParentAsync(
        DbConnection conn,
        DbTransaction tx,
        JellyfinGuid existingGuid,
        JellyfinGuid desiredParentFolderGuid,
        CancellationToken ct)
    {
        // Verify item type and update ParentId if needed (resume/incomplete prior run).
        await using var cmdSel = conn.CreateCommand();
        cmdSel.Transaction = tx;
        cmdSel.CommandText = @"
SELECT Type, ParentId
FROM TypedBaseItems
WHERE guid = $guid
LIMIT 1;";
        var pGuid = cmdSel.CreateParameter();
        pGuid.ParameterName = "$guid";
        pGuid.Value = existingGuid.Bytes;
        cmdSel.Parameters.Add(pGuid);

        string? type = null;
        byte[]? parentIdBytes = null;

        await using (var r = await cmdSel.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                type = r.IsDBNull(0) ? null : r.GetString(0);
                parentIdBytes = r.IsDBNull(1) ? null : (byte[])r.GetValue(1);
            }
        }

        if (!string.Equals(type, "MediaBrowser.Controller.Entities.Movies.Movie", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path already exists but is not a Movie item (Type='{type ?? "NULL"}').");

        // If ParentId differs, update it.
        if (parentIdBytes is not null && desiredParentFolderGuid.Bytes.SequenceEqual(parentIdBytes))
            return false;

        await using var cmdUpd = conn.CreateCommand();
        cmdUpd.Transaction = tx;
        cmdUpd.CommandText = @"
UPDATE TypedBaseItems
SET ParentId = $parent
WHERE guid = $guid;";
        var pParent = cmdUpd.CreateParameter();
        pParent.ParameterName = "$parent";
        pParent.Value = desiredParentFolderGuid.Bytes;
        cmdUpd.Parameters.Add(pParent);
        var pGuid2 = cmdUpd.CreateParameter();
        pGuid2.ParameterName = "$guid";
        pGuid2.Value = existingGuid.Bytes;
        cmdUpd.Parameters.Add(pGuid2);

        await cmdUpd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<List<MovieRow>> ReadMoviesAsync(
        DbConnection conn,
        DbTransaction? tx, 
        JellyfinGuid sourceTopId,
        CancellationToken ct)
    {

        var cols = await GetTableColumnsAsync(conn, tx, "TypedBaseItems", ct).ConfigureAwait(false);
        var hasOriginalTitle = cols.Contains("OriginalTitle");
        var hasProductionYear = cols.Contains("ProductionYear");
        var hasPath = cols.Contains("Path");
        var hasData = cols.Contains("data") || cols.Contains("Data");

        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = $@"
SELECT
  guid,
  name{(hasOriginalTitle ? ", OriginalTitle" : "")}{(hasProductionYear ? ", ProductionYear" : "")}{(hasPath ? ", Path" : "")}{(hasData ? ", data" : "")}
FROM TypedBaseItems
WHERE lower(TopParentId) = lower($idText)
  AND Type = $type;";

        AddParam(cmd, "$idText", sourceTopId.NoDashes);
        AddParam(cmd, "$type", "MediaBrowser.Controller.Entities.Movies.Movie");

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var results = new List<MovieRow>();

        var guidOrd = SafeOrdinal(reader, "guid") ?? 0;
        var nameOrd = SafeOrdinal(reader, "name") ?? 1;
        var originalOrd = hasOriginalTitle ? SafeOrdinal(reader, "OriginalTitle") : null;
        var yearOrd = hasProductionYear ? SafeOrdinal(reader, "ProductionYear") : null;
        var pathOrd = hasPath ? SafeOrdinal(reader, "Path") : null;
        var dataOrd = hasData ? SafeOrdinal(reader, "data") ?? SafeOrdinal(reader, "Data") : null;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var guidBytes =  reader.IsDBNull(guidOrd) ? new JellyfinGuid() : JellyfinGuid.FromBytes((byte[]) reader.GetValue(guidOrd)) ;
            if (guidBytes.Value == Guid.Empty)
                continue;

            var movieName = ReadString(reader, nameOrd) ?? "";
            var originalName = originalOrd is not null ? ReadString(reader, originalOrd.Value) : null;
            var year = yearOrd is not null ? ReadInt(reader, yearOrd.Value) : null;
            var oldPath = pathOrd is not null ? ReadString(reader, pathOrd.Value) : null;

            if (dataOrd is not null && !reader.IsDBNull(dataOrd.Value))
            {
                var dataText = ReadJsonText(reader, dataOrd.Value);
                if (!string.IsNullOrWhiteSpace(dataText))
                    TryHydrateFromJson(dataText, ref originalName, ref year, ref oldPath);
            }

            results.Add(new MovieRow(
                GuidBytes: guidBytes,
                MovieName: movieName,
                OriginalName: originalName,
                Year: year,
                OldPath: oldPath ?? ""));
        }

        return results;
    }

    private static void TryHydrateFromJson(string json, ref string? originalName, ref int? year, ref string? oldPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (string.IsNullOrWhiteSpace(originalName) && root.TryGetProperty("OriginalTitle", out var ot) && ot.ValueKind == JsonValueKind.String)
                originalName = ot.GetString();

            if (year is null && root.TryGetProperty("ProductionYear", out var py))
            {
                if (py.ValueKind == JsonValueKind.Number && py.TryGetInt32(out var y))
                    year = y;
                else if (py.ValueKind == JsonValueKind.String && int.TryParse(py.GetString(), out var ys))
                    year = ys;
            }

            if (string.IsNullOrWhiteSpace(oldPath))
            {
                if (root.TryGetProperty("Path", out var p) && p.ValueKind == JsonValueKind.String)
                    oldPath = p.GetString();
                else if (root.TryGetProperty("MediaSources", out var ms) && ms.ValueKind == JsonValueKind.Array)
                {
                    var first = ms.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("Path", out var mp) && mp.ValueKind == JsonValueKind.String)
                        oldPath = mp.GetString();
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static int? SafeOrdinal(DbDataReader reader, string name)
    {
        try { return reader.GetOrdinal(name); }
        catch { return null; }
    }

    private static string? ReadString(DbDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static int? ReadInt(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var v = reader.GetValue(ordinal);
        try { return Convert.ToInt32(v); }
        catch { return null; }
    }

    private static string? ReadJsonText(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var val = reader.GetValue(ordinal);
        if (val is string s) return s;
        if (val is byte[] { Length: > 0 } bytes)
        {
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return null; }
        }
        return null;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(DbConnection conn, DbTransaction? tx, string tableName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(1);
            set.Add(name);
        }
        return set;
    }

    private readonly record struct MovieRow(
        JellyfinGuid GuidBytes,
        string MovieName,
        string? OriginalName,
        int? Year,
        string OldPath);
}

