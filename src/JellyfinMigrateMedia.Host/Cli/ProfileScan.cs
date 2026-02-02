using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class ProfileScan
{
    public static string ScanProfile(MigrationProfile profile)
    {
        var lines = new List<string>();
        var total = 0;

        foreach (var src in profile.Sources ?? [])
        {
            var path = src.SourcePath;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var lib = string.IsNullOrWhiteSpace(src.LibraryName) ? "Library" : src.LibraryName;
            var type = string.IsNullOrWhiteSpace(src.ContentType) ? "unknown" : src.ContentType;

            if (!Directory.Exists(path))
            {
                lines.Add($"{lib} ({type}): {path} (nenalezeno)");
                continue;
            }

            var exts = GetExtensionsForContentType(src.ContentType);
            var count = 0;

            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            foreach (var file in Directory.EnumerateFiles(path, "*.*", opts))
            {
                if (exts is not null)
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrWhiteSpace(ext) || !exts.Contains(ext))
                        continue;
                }

                count++;
            }

            total += count;
            lines.Add($"{lib} ({type}): {count} položek");
        }

        return lines.Count == 0 ? "Profil nemá žádné zdroje." : $"Nalezeno celkem: {total} položek{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    public static async Task<int> CountMoviesFromDbAsync(IJellyfinDbConnectionFactory? dbConnectionFactory, string? topParentIdHex)
    {
        try
        {
            if (dbConnectionFactory is null)
                return 0;

            if (string.IsNullOrWhiteSpace(topParentIdHex))
                return 0;

            await using var conn = await dbConnectionFactory.OpenConnectionAsync().ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();

            // Support both TEXT TopParentId (already hex) and BLOB TopParentId by comparing hex(TopParentId).
            cmd.CommandText = @"
SELECT COUNT(1)
FROM TypedBaseItems
WHERE (TopParentId = $id OR lower(hex(TopParentId)) = lower($id))
  AND Type = $type;";

            var pId = cmd.CreateParameter();
            pId.ParameterName = "$id";
            pId.Value = topParentIdHex.Trim();
            cmd.Parameters.Add(pId);

            var pType = cmd.CreateParameter();
            pType.ParameterName = "$type";
            pType.Value = "MediaBrowser.Controller.Entities.Movies.Movie";
            cmd.Parameters.Add(pType);

            var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (result is null || result == DBNull.Value)
                return 0;

            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    private static HashSet<string>? GetExtensionsForContentType(string? contentType)
    {
        var ct = (contentType ?? "").Trim().ToLowerInvariant();
        return ct switch
        {
            "movie" or "episode" or "video" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mkv",".mp4",".avi",".mov",".m4v",".ts",".webm"
            },
            "audio" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3",".flac",".aac",".m4a",".ogg",".wav"
            },
            "photo" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",".jpeg",".png",".gif",".webp",".tiff",".bmp",".heic"
            },
            _ => null
        };
    }
}

