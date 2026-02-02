using JellyfinMigrateMedia.Infrastructure.Configuration;

namespace JellyfinMigrateMedia.Host.Cli;

internal static class ProfileIndex
{
    public static MigrationProfile? PromptPick(JellyfinMigrateSettings settings)
    {
        if (settings.MigrationProfiles.Count == 0)
        {
            Console.WriteLine("No profiles yet.");
            return null;
        }

        Console.Write("Pick profile (index or id): ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return null;

        if (TryResolveProfile(settings, input, out var p))
            return p;

        Console.WriteLine("Not found.");
        return null;
    }

    public static bool TryResolveProfile(JellyfinMigrateSettings settings, string input, out MigrationProfile profile)
    {
        profile = null!;

        if (int.TryParse(input, out var idx))
        {
            idx -= 1; // displayed 1-based
            if (idx >= 0 && idx < settings.MigrationProfiles.Count)
            {
                profile = settings.MigrationProfiles[idx];
                return true;
            }
        }

        var byId = settings.MigrationProfiles.FirstOrDefault(p => string.Equals(p.Id, input, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            profile = byId;
            return true;
        }

        return false;
    }

    public static void PrintProfiles(JellyfinMigrateSettings settings)
    {
        if (settings.MigrationProfiles.Count == 0)
        {
            Console.WriteLine("(no profiles)");
            return;
        }

        for (var i = 0; i < settings.MigrationProfiles.Count; i++)
        {
            var p = settings.MigrationProfiles[i];
            var marker = settings.LastProfileId == p.Id ? "*" : " ";
            Console.WriteLine($"{marker} {i + 1}. {p.Name}  [{p.Id}]");
        }
    }
}

