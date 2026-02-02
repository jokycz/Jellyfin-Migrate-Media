using JellyfinMigrateMedia.Infrastructure.Configuration;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class ProfilePrinter
{
    public static void PrintProfile(MigrationProfile p)
    {
        var src = p.Sources.FirstOrDefault();

        Console.WriteLine("1) Profil");
        Console.WriteLine($"   Name: {p.Name}");
        Console.WriteLine($"   Id:   {p.Id}");
        Console.WriteLine();

        Console.WriteLine("2) Knihovna / složky");
        Console.WriteLine($"   Library:     {src?.LibraryName ?? "(none)"}");
        Console.WriteLine($"   ContentType: {src?.ContentType ?? "(none)"}");
        Console.WriteLine($"   SOURCE:      {src?.SourcePath ?? "(none)"}  [{src?.TopId ?? ""}]");
        Console.WriteLine($"   TARGET:      {p.Destination.TargetPath ?? "(none)"}  [{p.Destination.TargetTopId ?? ""}]");
        Console.WriteLine();

        Console.WriteLine("3) Masky");
        if (string.Equals((src?.ContentType ?? "").Trim(), "Movie", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"   MovieFolderTemplate: {p.Naming.MovieFolderTemplate}");
            Console.WriteLine($"   MovieFileTemplate:   {p.Naming.MovieFileTemplate}");
        }
        else
        {
            Console.WriteLine($"   SeriesFolderTemplate: {p.Naming.SeriesFolderTemplate}");
            Console.WriteLine($"   EpisodeFileTemplate:  {p.Naming.EpisodeFileTemplate}");
        }
        Console.WriteLine($"   Sanitize: {p.Naming.SanitizeFileAndFolderNames}");
    }
}

