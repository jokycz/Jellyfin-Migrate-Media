namespace JellyfinMigrateMedia.Infrastructure.Db;

public static class JellyfinServerPathResolver
{
    public static string? NormalizeServerRoot(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        return expanded;
    }

    public static string GetLibraryDbPath(string serverRoot)
        => Path.Combine(serverRoot, "data", "library.db");

    /// <summary>
    /// Jellyfin stores per-library options in folders under the server root.
    /// User reports: root/default/*/options.xml
    /// </summary>
    public static IEnumerable<string> EnumerateLibraryOptionsXml(string serverRoot)
    {
        // Preferred: <root>\default\<libraryId>\options.xml
        var defaultRoot = Path.Combine(serverRoot, "root", "default");
        if (Directory.Exists(defaultRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(defaultRoot))
            {
                var candidate = Path.Combine(dir, "options.xml");
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }

        // Fallback (older layouts): <root>\<libraryId>\options.xml
        foreach (var dir in Directory.EnumerateDirectories(serverRoot))
        {
            var candidate = Path.Combine(dir, "options.xml");
            if (File.Exists(candidate))
                yield return candidate;
        }
    }

    public static string? TryGetLibraryOptionsXmlPath(string serverRoot, string libraryId)
    {
        if (string.IsNullOrWhiteSpace(serverRoot) || string.IsNullOrWhiteSpace(libraryId))
            return null;

        // Preferred: <root>\default\<libraryId>\options.xml
        var preferred = Path.Combine(serverRoot, "root", "default", libraryId, "options.xml");
        if (File.Exists(preferred))
            return preferred;

        // Fallback: <root>\<libraryId>\options.xml
        var fallback = Path.Combine(serverRoot, libraryId, "options.xml");
        if (File.Exists(fallback))
            return fallback;

        return null;
    }
}

