// ReSharper disable UnusedMember.Global
namespace JellyfinMigrateMedia.Host.Cli;

internal static class PlanFiles
{
    public static string PlansDir => Path.Combine(Environment.CurrentDirectory, "migration-plans");

    public static string? PickLatestPlanPath()
    {
        try
        {
            if (!Directory.Exists(PlansDir))
                return null;

            var files = Directory.EnumerateFiles(PlansDir, "*.txt", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            return files.FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }
}

