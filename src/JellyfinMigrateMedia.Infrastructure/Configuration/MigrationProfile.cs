// ReSharper disable PropertyCanBeMadeInitOnly.Global
namespace JellyfinMigrateMedia.Infrastructure.Configuration;

public sealed class MigrationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New profile";

    /// <summary>
    /// Target settings (selected folder within the same Jellyfin library).
    /// </summary>
    public DestinationSettings Destination { get; set; } = new();

    /// <summary>
    /// 2) Rules for renaming + folder organization.
    /// </summary>
    public NamingAndOrganizationSettings Naming { get; set; } = new();

    /// <summary>
    /// 3) Source folders from Jellyfin libraries.
    /// </summary>
    public List<SourceMediaDefinition> Sources { get; set; } = [];
}

public sealed class DestinationSettings
{
    /// <summary>
    /// Target path within the selected Jellyfin library.
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// Jellyfin physical folder id for the target path (from TypedBaseItems JSON PhysicalFolderIds mapping).
    /// </summary>
    public string? TargetTopId { get; set; }
}

public sealed class NamingAndOrganizationSettings
{
    /// <summary>
    /// Folder template for movies (relative inside selected target root), e.g. "{MovieName[0]}".
    /// </summary>
    public string MovieFolderTemplate { get; set; } = "{MovieName[0]}";

    /// <summary>
    /// Movie file template (filename only, without directory), e.g. "{MovieName} ({OriginalName}) {Year}.{Extension}".
    /// </summary>
    public string MovieFileTemplate { get; set; } = "{MovieName} ({OriginalName}) {Year}.{Extension}";

    /// <summary>
    /// Folder template for series, e.g. "Series/{Title}/Season {SeasonNumber:00}".
    /// </summary>
    public string SeriesFolderTemplate { get; set; } = "Series/{Title}/Season {SeasonNumber:00}";

    /// <summary>
    /// File template for episodes, e.g. "{Title} - S{SeasonNumber:00}E{EpisodeNumber:00} - {EpisodeTitle}".
    /// </summary>
    public string EpisodeFileTemplate { get; set; } = "{Title} - S{SeasonNumber:00}E{EpisodeNumber:00} - {EpisodeTitle}";

    /// <summary>
    /// If true, invalid path characters will be replaced (implementation later).
    /// </summary>
    public bool SanitizeFileAndFolderNames { get; set; } = true;
}

public sealed class SourceMediaDefinition
{
    /// <summary>
    /// Jellyfin library (CollectionFolders.Id).
    /// </summary>
    public string? LibraryId { get; set; }

    /// <summary>
    /// Jellyfin library name (CollectionFolders.Name).
    /// </summary>
    public string? LibraryName { get; set; }

    /// <summary>
    /// Jellyfin content type (e.g. Movie/Episode/Audio/Photo), parsed from options.xml TypeOptions/Type.
    /// Used to infer behavior, not to be manually configured by the user.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Source path where files live (folder).
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Jellyfin physical folder id for the selected path (from TypedBaseItems JSON PhysicalFolderIds mapping).
    /// </summary>
    public string? TopId { get; set; }

    /// <summary>
    /// Legacy: Optional label to identify the disk, e.g. "Disk 1" or volume label.
    /// </summary>
    public string? DiskLabel { get; set; }
}

// NOTE: MediaType removed from configuration. If needed later, infer from ContentType.

