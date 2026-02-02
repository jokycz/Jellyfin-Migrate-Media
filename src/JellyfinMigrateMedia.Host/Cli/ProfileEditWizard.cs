using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class ProfileEditWizard
{
    public static async Task<MigrationProfile?> RunAsync(
        MigrationProfile existing,
        IJellyfinLibraryCatalog? libraryCatalog,
        IJellyfinDbConnectionFactory? dbConnectionFactory)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var p = ProfileClone.Clone(existing);

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== Edit profile ===");
            ProfilePrinter.PrintProfile(p);
            Console.WriteLine();
            Console.WriteLine("[1] Upravit název profilu");
            Console.WriteLine("[2] Knihovna + SOURCE/TARGET složka");
            Console.WriteLine("[3] Masky (dle typu)");
            Console.WriteLine("[0] Uložit a zpět");
            Console.Write("Vyber část: ");

            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input))
                continue;

            switch (input)
            {
                case "0":
                    return p;
                    
                case "1":
                    p.Name = ConsolePrompts.Prompt("Název profilu", p.Name);
                    break;

                case "2":
                    {
                        if (libraryCatalog is null)
                        {
                            Console.WriteLine("Chybí přístup ke knihovnám (nastav JellyfinMigrate:JellyfinServerRootPath a aby existovalo data\\library.db).");
                            break;
                        }

                        var ok = await EditSection_LibraryAndFoldersAsync(p, libraryCatalog, dbConnectionFactory);
                        if (!ok)
                            Console.WriteLine("Část 2 nebyla změněna.");
                        break;
                    }

                case "3":
                    EditSection_Masks(p);
                    break;

                default:
                    Console.WriteLine("Neznámá volba.");
                    break;
            }
        }
    }

    private static async Task<bool> EditSection_LibraryAndFoldersAsync(
        MigrationProfile p,
        IJellyfinLibraryCatalog libraryCatalog,
        IJellyfinDbConnectionFactory? dbConnectionFactory)
    {
        Console.WriteLine();
        Console.WriteLine("=== Část 2: Knihovna + SOURCE/TARGET ===");

        var libs = await libraryCatalog.GetLibrariesAsync();
        if (libs.Count == 0)
        {
            Console.WriteLine("(Žádné knihovny nenalezeny.)");
            return false;
        }

        Console.WriteLine("0) Zrušit");
        for (var i = 0; i < libs.Count; i++)
            Console.WriteLine($"{i + 1}) {libs[i].Name} ({libs[i].ContentType ?? "unknown"})");

        Console.Write("Vyber knihovnu (číslo): ");
        var pickRaw = Console.ReadLine()?.Trim();
        if (!int.TryParse(pickRaw, out var idx) || idx <= 0 || idx > libs.Count)
            return false;

        var lib = libs[idx - 1];
        if (lib.Paths.Count == 0)
        {
            Console.WriteLine($"(Library '{lib.Name}' has no configured folders/paths)");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Folders for: {lib.Name} ({lib.ContentType ?? "unknown"})");
        for (var pi = 0; pi < lib.Paths.Count; pi++)
            Console.WriteLine($"  {pi + 1}) {lib.Paths[pi].Path}  [{lib.Paths[pi].TopId}]");

        Console.WriteLine();
        Console.Write("Vyber SOURCE složku (číslo): ");
        var srcRaw = Console.ReadLine()?.Trim();
        if (!int.TryParse(srcRaw, out var srcIdx) || srcIdx <= 0 || srcIdx > lib.Paths.Count)
            return false;

        Console.Write("Vyber TARGET složku (číslo): ");
        var dstRaw = Console.ReadLine()?.Trim();
        if (!int.TryParse(dstRaw, out var dstIdx) || dstIdx <= 0 || dstIdx > lib.Paths.Count)
            return false;

        var sourcePath = lib.Paths[srcIdx - 1];
        var targetPath = lib.Paths[dstIdx - 1];

        if (string.Equals(sourcePath.Path, targetPath.Path, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SOURCE a TARGET nesmí být stejná složka.");
            return false;
        }

        p.Sources =
        [
            new SourceMediaDefinition()
            {
                LibraryName = lib.Name,
                ContentType = lib.ContentType,
                SourcePath = sourcePath.Path,
                TopId = string.IsNullOrWhiteSpace(sourcePath.TopId) ? null : sourcePath.TopId,
                DiskLabel = null
            }
        ];

        p.Destination.TargetPath = targetPath.Path;
        p.Destination.TargetTopId = string.IsNullOrWhiteSpace(targetPath.TopId) ? null : targetPath.TopId;

        if (!string.IsNullOrWhiteSpace(sourcePath.Path)
            && string.Equals((lib.ContentType ?? "").Trim(), "Movie", StringComparison.OrdinalIgnoreCase))
        {
            var movieCount = await ProfileScan.CountMoviesFromDbAsync(dbConnectionFactory, p.Sources.FirstOrDefault()?.TopId)
                .ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"SOURCE obsahuje {movieCount} filmů v DB (Movie).");
        }

        return true;
    }

    private static void EditSection_Masks(MigrationProfile p)
    {
        Console.WriteLine();
        Console.WriteLine("=== Část 3: Masky ===");

        var contentType = p.Sources.FirstOrDefault()?.ContentType ?? "";
        if (string.Equals(contentType.Trim(), "Movie", StringComparison.OrdinalIgnoreCase))
        {
            p.Naming.MovieFolderTemplate = ConsolePrompts.Prompt("Movie folder template", p.Naming.MovieFolderTemplate);
            p.Naming.MovieFileTemplate = ConsolePrompts.Prompt("Movie file template", p.Naming.MovieFileTemplate);
            return;
        }

        p.Naming.SeriesFolderTemplate = ConsolePrompts.Prompt("Series folder template", p.Naming.SeriesFolderTemplate);
        p.Naming.EpisodeFileTemplate = ConsolePrompts.Prompt("Episode file template", p.Naming.EpisodeFileTemplate);
    }
}

