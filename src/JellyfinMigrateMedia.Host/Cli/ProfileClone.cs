using JellyfinMigrateMedia.Infrastructure.Configuration;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class ProfileClone
{
    public static MigrationProfile Clone(MigrationProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Destination = new DestinationSettings
        {
            TargetPath = p.Destination.TargetPath,
            TargetTopId = p.Destination.TargetTopId
        },
        Naming = new NamingAndOrganizationSettings
        {
            MovieFolderTemplate = p.Naming.MovieFolderTemplate,
            MovieFileTemplate = p.Naming.MovieFileTemplate,
            SeriesFolderTemplate = p.Naming.SeriesFolderTemplate,
            EpisodeFileTemplate = p.Naming.EpisodeFileTemplate,
            SanitizeFileAndFolderNames = p.Naming.SanitizeFileAndFolderNames
        },
        Sources =
        [
            .. (p.Sources ?? [])
                .Select(s => new SourceMediaDefinition
                {
                    LibraryId = s.LibraryId,
                    LibraryName = s.LibraryName,
                    ContentType = s.ContentType,
                    SourcePath = s.SourcePath,
                    TopId = s.TopId,
                    DiskLabel = s.DiskLabel
                })
        ]
    };
}

