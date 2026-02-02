using JellyfinMigrateMedia.Host.Cli;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;
using Serilog;

namespace JellyfinMigrateMedia.Host;

/// <summary>
/// CLI router for profiles. Heavy lifting is split into operation-specific classes under Host/Cli.
/// </summary>
internal static class MigrationProfilesCli
{
    public static async Task<int> RunSelectedAsync(
        string selector,
        ISettingsStore settingsStore,
        IJellyfinLibraryCatalog? libraryCatalog = null,
        IJellyfinDbConnectionFactory? dbConnectionFactory = null)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            Log.Error("Missing profile selector (index or id).");
            return 2;
        }

        var settings = await settingsStore.LoadAsync();
        if (!ProfileIndex.TryResolveProfile(settings, selector.Trim(), out var p))
        {
            Log.Error("Profile not found: {Selector}", selector);
            ProfileIndex.PrintProfiles(settings);
            return 2;
        }

        settings.LastProfileId = p.Id;
        await settingsStore.SaveAsync(settings);

        Console.WriteLine($"Selected: {p.Name} ({p.Id})");
        Console.WriteLine();
        Console.WriteLine("--- Status (scan sources) ---");
        Console.WriteLine(ProfileScan.ScanProfile(p));
        Console.WriteLine();
        Console.WriteLine("Run migration: not wired yet (placeholder).");
        if (libraryCatalog is not null || dbConnectionFactory is not null)
            Console.WriteLine("Tip: Jellyfin libraries are available (use profiles menu to pick them during edit/new).");

        return 0;
    }

    public static async Task<int> RunAsync(
        string[] args,
        ISettingsStore settingsStore,
        IJellyfinLibraryCatalog? libraryCatalog = null,
        IJellyfinDbConnectionFactory? dbConnectionFactory = null)
    {
        var settings = await settingsStore.LoadAsync();

        // Non-interactive helpers
        if (args.Length > 0)
        {
            var cmd = args[0].ToLowerInvariant();
            if (cmd is "list" or "ls")
            {
                ProfileIndex.PrintProfiles(settings);
                return 0;
            }

            if (cmd is "delete" or "del" && args.Length > 1)
            {
                var target = args[1];
                if (!ProfileIndex.TryResolveProfile(settings, target, out var p))
                {
                    Log.Error("Profile not found: {Target}", target);
                    return 2;
                }

                settings.MigrationProfiles.RemoveAll(x => x.Id == p.Id);
                if (settings.LastProfileId == p.Id) settings.LastProfileId = null;
                await settingsStore.SaveAsync(settings);
                Console.WriteLine($"Deleted: {p.Name} ({p.Id})");
                return 0;
            }
        }

        // Interactive launcher
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== JellyfinMigrate profiles ===");
            Console.WriteLine($"Settings: {settingsStore.SettingsPath}");
            Console.WriteLine();

            ProfileIndex.PrintProfiles(settings);

            Console.WriteLine();
            Console.WriteLine("[N] New profile");
            Console.WriteLine("[E] Edit profile");
            Console.WriteLine("[S] Show status (scan sources)");
            Console.WriteLine("[R] Run migration (step 1: DB list -> new names/paths -> txt log)");
            Console.WriteLine("[D] Delete profile");
            Console.WriteLine("[Q] Quit");
            Console.Write("Select: ");

            var key = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            switch (key.ToLowerInvariant())
            {
                case "q":
                    await settingsStore.SaveAsync(settings);
                    return 0;

                case "n":
                    {
                        var created = await ProfileCreateWizard.RunAsync(existing: null, libraryCatalog, dbConnectionFactory);
                        if (created is null) break;

                        settings.MigrationProfiles.Add(created);
                        settings.LastProfileId = created.Id;
                        await settingsStore.SaveAsync(settings);
                        Console.WriteLine($"Saved: {created.Name} ({created.Id})");
                        break;
                    }

                case "e":
                    {
                        var p = ProfileIndex.PromptPick(settings);
                        if (p is null) break;

                        var edited = await ProfileEditWizard.RunAsync(p, libraryCatalog, dbConnectionFactory);
                        if (edited is null) break;

                        var idx = settings.MigrationProfiles.FindIndex(x => x.Id == p.Id);
                        if (idx >= 0) settings.MigrationProfiles[idx] = edited;
                        settings.LastProfileId = edited.Id;
                        await settingsStore.SaveAsync(settings);
                        Console.WriteLine($"Saved: {edited.Name} ({edited.Id})");
                        break;
                    }

                case "s":
                    {
                        var p = ProfileIndex.PromptPick(settings);
                        if (p is null) break;

                        settings.LastProfileId = p.Id;
                        await settingsStore.SaveAsync(settings);

                        Console.WriteLine();
                        Console.WriteLine($"--- Status: {p.Name} ---");
                        Console.WriteLine(ProfileScan.ScanProfile(p));
                        break;
                    }

                case "r":
                    {
                        var p = ProfileIndex.PromptPick(settings);
                        if (p is null) break;

                        settings.LastProfileId = p.Id;
                        await settingsStore.SaveAsync(settings);

                        if (dbConnectionFactory is null)
                        {
                            Console.WriteLine("Chybí přístup k Jellyfin DB (nastav JellyfinMigrate:JellyfinServerRootPath a aby existovalo data\\library.db).");
                            break;
                        }

                        Console.WriteLine();
                        Console.WriteLine($"Running per-movie pipeline: {p.Name} ({p.Id})");
                        await MovieMigrationRunner.RunAsync(p, dbConnectionFactory);
                        break;
                    }

                case "d":
                    {
                        var p = ProfileIndex.PromptPick(settings);
                        if (p is null) break;

                        Console.Write($"Delete '{p.Name}'? (y/N): ");
                        var confirm = Console.ReadLine()?.Trim();
                        if (!string.Equals(confirm, "y", StringComparison.OrdinalIgnoreCase))
                            break;

                        settings.MigrationProfiles.RemoveAll(x => x.Id == p.Id);
                        if (settings.LastProfileId == p.Id) settings.LastProfileId = null;
                        await settingsStore.SaveAsync(settings);
                        Console.WriteLine("Deleted.");
                        break;
                    }

                default:
                    Console.WriteLine("Unknown option.");
                    break;
            }
        }
    }
}

