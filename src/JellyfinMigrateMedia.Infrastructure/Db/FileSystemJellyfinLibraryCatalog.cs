using System.Xml.Linq;

// ReSharper disable UnusedType.Global

namespace JellyfinMigrateMedia.Infrastructure.Db;

/// <summary>
/// Discovers Jellyfin libraries from the server root directory by reading root/default/*/options.xml
/// (with fallback to root/*/options.xml).
/// </summary>
public sealed class FileSystemJellyfinLibraryCatalog : IJellyfinLibraryCatalog
{
    private readonly string _serverRoot;

    public FileSystemJellyfinLibraryCatalog(string serverRoot)
    {
        _serverRoot = serverRoot ?? throw new ArgumentNullException(nameof(serverRoot));
    }

    public Task<IReadOnlyList<JellyfinLibraryInfo>> GetLibrariesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_serverRoot))
            return Task.FromResult<IReadOnlyList<JellyfinLibraryInfo>>([]);

        var list = new List<JellyfinLibraryInfo>();

        foreach (var optionsPath in JellyfinServerPathResolver.EnumerateLibraryOptionsXml(_serverRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dir = Path.GetDirectoryName(optionsPath) ?? _serverRoot;
            var id = Path.GetFileName(dir);
            if (!string.IsNullOrWhiteSpace(id))
                list.Add(new JellyfinLibraryInfo(id, null, []));
        }

        return Task.FromResult<IReadOnlyList<JellyfinLibraryInfo>>(
            [.. list.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)]);
    }

    public Task<JellyfinLibraryInfo?> GetLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(libraryId))
            return Task.FromResult<JellyfinLibraryInfo?>(null);

        if (!Directory.Exists(_serverRoot))
            return Task.FromResult<JellyfinLibraryInfo?>(null);

        var optionsPath = JellyfinServerPathResolver.TryGetLibraryOptionsXmlPath(_serverRoot, libraryId.Trim());
        if (string.IsNullOrWhiteSpace(optionsPath) || !File.Exists(optionsPath))
            return Task.FromResult<JellyfinLibraryInfo?>(null);

        try
        {
            var doc = XDocument.Load(optionsPath);
            var root = doc.Root;

            var id = libraryId.Trim();
            var name = GetDirectChildValue(root, "Name");
            if (string.IsNullOrWhiteSpace(name))
                name = id;

            // Prefer content type from TypeOptions/Type (e.g. Movie/Episode/Audio/Photo).
            // Fallback to CollectionType if present.
            var contentType =
                ExtractContentType(doc)
                ?? GetDirectChildValue(root, "CollectionType");

            var paths = ExtractPaths(doc)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => new ContentPath(p, ""))
                .ToArray();

            return Task.FromResult<JellyfinLibraryInfo?>(new JellyfinLibraryInfo(name, contentType, paths));
        }
        catch
        {
            // Return minimal info on parse issues.
            return Task.FromResult<JellyfinLibraryInfo?>(new JellyfinLibraryInfo(libraryId.Trim(), null, []));
        }
    }

    private static string? GetDirectChildValue(XElement? root, string localName)
    {
        if (root is null) return null;
        var el = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        return el?.Value;
    }

    private static IEnumerable<string> ExtractPaths(XDocument doc)
    {
        // Best-effort: Jellyfin has used variants like:
        // PathInfos/PathInfo/Path or Folders/Folder/Path.
        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName.Equals("Path", StringComparison.OrdinalIgnoreCase)))
        {
            var v = el.Value;
            if (!string.IsNullOrWhiteSpace(v))
                yield return v;
        }
    }

    private static string? ExtractContentType(XDocument doc)
    {
        // Example:
        // <TypeOptions>
        //   <TypeOptions>
        //     <Type>Movie</Type>
        //   </TypeOptions>
        // </TypeOptions>
        var types = doc.Descendants()
            .Where(e => e.Name.LocalName.Equals("Type", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return types.Length == 0 ? null :
            // If multiple are present, keep the first (we can refine later if needed).
            types[0];
    }
}

