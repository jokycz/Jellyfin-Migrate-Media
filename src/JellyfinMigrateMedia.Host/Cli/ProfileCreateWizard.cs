using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class ProfileCreateWizard
{
    public static async Task<MigrationProfile?> RunAsync(
        MigrationProfile? existing,
        IJellyfinLibraryCatalog? libraryCatalog,
        IJellyfinDbConnectionFactory? dbConnectionFactory)
    {
        var p = existing is null ? new() : ProfileClone.Clone(existing);

        Console.WriteLine();
        Console.WriteLine("=== Wizard: 1) Knihovna ===");
        p.Name = ConsolePrompts.Prompt("Název profilu", p.Name);
        Console.WriteLine("Vyberte knihovnu médií:");

        var sources = new List<SourceMediaDefinition>();
        if (existing?.Sources is { Count: > 0 })
            sources.AddRange(existing.Sources);

        var usedJellyfinSelection = false;
        if (libraryCatalog is not null)
        {
            try
            {
                var libs = await libraryCatalog.GetLibrariesAsync();
                if (libs.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("0) Zrušit (nic nevybírat)");
                    for (var i = 0; i < libs.Count; i++)
                        Console.WriteLine($"{i + 1}) {libs[i].Name} ({libs[i].ContentType ?? "unknown"})");

                    Console.WriteLine();
                    Console.Write("Vyber knihovnu (číslo), nebo 0: ");
                    var pickRaw = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrWhiteSpace(pickRaw) && pickRaw != "0")
                    {
                        if (!int.TryParse(pickRaw, out var idx) || idx <= 0 || idx > libs.Count)
                        {
                            Console.WriteLine("Neplatná volba.");
                            return null;
                        }

                        var lib = libs[idx - 1];

                        if (lib.Paths.Count == 0)
                        {
                            Console.WriteLine($"(Library '{lib.Name}' has no configured folders/paths)");
                            return null;
                        }

                        Console.WriteLine();
                        Console.WriteLine($"Folders for: {lib.Name} ({lib.ContentType ?? "unknown"})");
                        for (var pi = 0; pi < lib.Paths.Count; pi++)
                            Console.WriteLine($"  {pi + 1}) {lib.Paths[pi].Path}  [{lib.Paths[pi].TopId}]");

                        Console.WriteLine();
                        Console.Write("Vyber SOURCE složku (číslo): ");
                        var srcRaw = Console.ReadLine()?.Trim();
                        if (!int.TryParse(srcRaw, out var srcIdx) || srcIdx <= 0 || srcIdx > lib.Paths.Count)
                        {
                            Console.WriteLine("Neplatná volba SOURCE.");
                            return null;
                        }

                        Console.Write("Vyber TARGET složku (číslo): ");
                        var dstRaw = Console.ReadLine()?.Trim();
                        if (!int.TryParse(dstRaw, out var dstIdx) || dstIdx <= 0 || dstIdx > lib.Paths.Count)
                        {
                            Console.WriteLine("Neplatná volba TARGET.");
                            return null;
                        }

                        var sourcePath = lib.Paths[srcIdx - 1];
                        var targetPath = lib.Paths[dstIdx - 1];

                        if (string.Equals(sourcePath.Path, targetPath.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("SOURCE a TARGET nesmí být stejná složka.");
                            return null;
                        }

                        sources.Clear();
                        sources.Add(new()
                        {
                            LibraryName = lib.Name,
                            ContentType = lib.ContentType,
                            SourcePath = sourcePath.Path,
                            TopId = string.IsNullOrWhiteSpace(sourcePath.TopId) ? null : sourcePath.TopId,
                            DiskLabel = null
                        });

                        p.Destination.TargetPath = targetPath.Path;
                        p.Destination.TargetTopId = string.IsNullOrWhiteSpace(targetPath.TopId) ? null : targetPath.TopId;

                        if (!string.IsNullOrWhiteSpace(sourcePath.Path)
                            && string.Equals((lib.ContentType ?? "").Trim(), "Movie", StringComparison.OrdinalIgnoreCase))
                        {
                            var movieCount = await ProfileScan.CountMoviesFromDbAsync(dbConnectionFactory, sources.FirstOrDefault()?.TopId)
                                .ConfigureAwait(false);
                            Console.WriteLine();
                            Console.WriteLine($"SOURCE obsahuje {movieCount} filmů v DB (Movie).");
                        }

                        usedJellyfinSelection = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"(Cannot read Jellyfin libraries: {ex.Message})");
            }
        }

        if (!usedJellyfinSelection)
        {
            Console.WriteLine("Nejsou vybrané žádné Jellyfin knihovny.");
            Console.WriteLine("Nastav JellyfinMigrate:JellyfinServerRootPath (appsettings) nebo JellyfinServerRootPath (settings.json), a pak vyber knihovny.");
            return null;
        }

        p.Sources = sources;

        Console.WriteLine();
        Console.WriteLine("=== Wizard: 2) Maskování / struktura ===");

        var contentType = p.Sources.FirstOrDefault()?.ContentType ?? "";
        if (string.Equals(contentType.Trim(), "Movie", StringComparison.OrdinalIgnoreCase))
        {
            p.Naming.MovieFolderTemplate = ConsolePrompts.Prompt("Movie folder template", p.Naming.MovieFolderTemplate);
            p.Naming.MovieFileTemplate = ConsolePrompts.Prompt("Movie file template", p.Naming.MovieFileTemplate);
        }
        else
        {
            p.Naming.SeriesFolderTemplate = ConsolePrompts.Prompt("Series folder template", p.Naming.SeriesFolderTemplate);
            p.Naming.EpisodeFileTemplate = ConsolePrompts.Prompt("Episode file template", p.Naming.EpisodeFileTemplate);
        }

        p.Naming.SanitizeFileAndFolderNames = ConsolePrompts.PromptBool("Sanitize názvy", p.Naming.SanitizeFileAndFolderNames);

        if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Destination.TargetPath))
        {
            Console.WriteLine("Invalid profile: missing Name or Destination.TargetPath.");
            return null;
        }

        if (p.Sources.Count != 1 || p.Sources.All(s => string.IsNullOrWhiteSpace(s.SourcePath)))
        {
            Console.WriteLine("Invalid profile: missing source.");
            return null;
        }

        return p;
    }
}

