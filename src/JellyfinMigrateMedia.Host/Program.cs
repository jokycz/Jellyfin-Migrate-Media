using Autofac;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;
using JellyfinMigrateMedia.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Formatting.Compact;

namespace JellyfinMigrateMedia.Host;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        ConfigureSerilog();
        try
        {
            // Shared user settings (also usable by UI).
            var settingsStore = new JsonSettingsStore();
            var settings = await settingsStore.LoadAsync();

            // Try load appsettings.json from common run locations (bin output / project folder / repo root).
            var appSettingsPaths = GetCandidateAppSettingsPaths().ToArray();
            var configBuilder = new ConfigurationBuilder()
                .AddEnvironmentVariables(prefix: "JELLYFINMIGRATE_");
            foreach (var p in appSettingsPaths)
                configBuilder.AddJsonFile(p, optional: true, reloadOnChange: false);
            var config = configBuilder.Build();

            // Build library catalog (optional). We infer DB path from Jellyfin Server root:
            // <root>\data\library.db
            var serverRootCandidate =
                config["JellyfinMigrate:JellyfinServerRootPath"]
                ?? settings.JellyfinServerRootPath
                ?? @"%ProgramData%\Jellyfin\Server";

            var serverRoot = JellyfinServerPathResolver.NormalizeServerRoot(serverRootCandidate);
            IJellyfinLibraryCatalog? libraryCatalog = null;
            IJellyfinDbConnectionFactory? dbConnectionFactory = null;
            if (!string.IsNullOrWhiteSpace(serverRoot) && Directory.Exists(serverRoot))
            {
                var dbPath = JellyfinServerPathResolver.GetLibraryDbPath(serverRoot);
                if (File.Exists(dbPath))
                {
                    var sqliteOptions = JellyfinMigrateOptionsRegistrationExtensions.BuildJellyfinSqliteOptions(config, settings);
                    sqliteOptions.DatabasePath = dbPath;
                    // Migration workflow will eventually update DB, so keep ReadWrite.
                    sqliteOptions.ReadOnly = false;
                    dbConnectionFactory = new JellyfinSqliteConnectionFactory(sqliteOptions);
                    libraryCatalog = new JellyfinLibraryCatalog(dbConnectionFactory);
                }
            }

            // Default behavior:
            // - no args => interactive profiles menu
            // - "profiles" => profiles menu (supports list/delete shortcuts)
            // - "dbtest" => quick DB connection test (legacy)
            // - otherwise => treat args[0] as "profile selector" and run that migration (placeholder for now)
            if (args.Length == 0)
                return await MigrationProfilesCli.RunAsync([], settingsStore, libraryCatalog, dbConnectionFactory);

            if (string.Equals(args[0], "profiles", StringComparison.OrdinalIgnoreCase))
                return await MigrationProfilesCli.RunAsync(args[1..], settingsStore, libraryCatalog, dbConnectionFactory);

            if (string.Equals(args[0], "dbtest", StringComparison.OrdinalIgnoreCase))
            {
                var dbPathArg = args.Length > 1 ? args[1] : null;
                var dbPathFromConfig = config["JellyfinMigrate:JellyfinSqliteDbPath"];
                var dbPathFromUserSettings = settings.JellyfinSqliteDbPath;

                var dbPathRaw =
                    !string.IsNullOrWhiteSpace(dbPathArg)
                        ? dbPathArg
                        : (!string.IsNullOrWhiteSpace(dbPathFromConfig)
                            ? dbPathFromConfig
                            : (!string.IsNullOrWhiteSpace(dbPathFromUserSettings)
                                ? dbPathFromUserSettings
                                : (!string.IsNullOrWhiteSpace(serverRoot) ? JellyfinServerPathResolver.GetLibraryDbPath(serverRoot) : null)));

                var dbPath = NormalizeDbPath(dbPathRaw);

                if (string.IsNullOrWhiteSpace(dbPath))
                {
                    Log.Error("Usage: JellyfinMigrate.Host dbtest <path-to-jellyfin-sqlite-db>");
                    Log.Error("Or set JellyfinMigrate:JellyfinSqliteDbPath (optional) in appsettings.");
                    Log.Error("Or set JellyfinMigrate:JellyfinServerRootPath and DB will be inferred as <root>\\data\\library.db.");
                    Log.Error("Or save user settings to %APPDATA%\\JellyfinMigrate\\settings.json");
                    return 2;
                }

                Log.Information("Using DB path: {DbPath}", dbPath);
                var loadedFrom = appSettingsPaths.FirstOrDefault(File.Exists);
                if (!string.IsNullOrWhiteSpace(loadedFrom))
                    Log.Information("Loaded appsettings from: {AppSettingsPath}", loadedFrom);

                var builder = new ContainerBuilder();

                // Make app configuration available via DI to any service that needs it.
                builder.RegisterInstance(config)
                    .As<IConfiguration>()
                    .SingleInstance();

                // Shared user settings storage (also usable by UI).
                builder.RegisterInstance(settingsStore)
                    .As<ISettingsStore>()
                    .SingleInstance();

                // Register loaded settings as data (Infrastructure can use it for options).
                builder.RegisterInstance(settings)
                    .AsSelf()
                    .SingleInstance();

                // App-wide options registration (used across apps/services).
                builder.RegisterJellyfinMigrateOptions(config, settings, o =>
                {
                    // CLI/config/user settings resolved DB path is the final authority.
                    o.DatabasePath = dbPath;
                    // Host defaults to read-only for safety.
                    o.ReadOnly = true;
                });

                // Ruční registrace modulů pro tento projekt.
                // (Scanning používáš až u větších/rozšiřitelných řešení.)
                builder.RegisterModule(new InfrastructureModule());

                var container = builder.Build();

                await using var scope = container.BeginLifetimeScope();
                var factory = scope.Resolve<IJellyfinDbConnectionFactory>();

                await using var conn = await factory.OpenConnectionAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1;";
                var result = await cmd.ExecuteScalarAsync();

                Log.Information("DB connection OK. SELECT 1 => {Result}", result);
                return 0;
            }

            // Run selected migration profile (non-interactive).
            return await MigrationProfilesCli.RunSelectedAsync(args[0], settingsStore, libraryCatalog, dbConnectionFactory);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

private static IEnumerable<string> GetCandidateAppSettingsPaths()
    {
        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();

        // 1) Next to executable (when copied to output)
        yield return Path.Combine(baseDir, "appsettings.json");

        // 2) Current working directory (e.g., running from project folder)
        yield return Path.Combine(cwd, "appsettings.json");

        // 3) Running from repo root (common in VS): src/JellyfinMigrate.Host/appsettings.json
        yield return Path.Combine(cwd, "src", "JellyfinMigrate.Host", "appsettings.json");

        // 4) Typical bin/Debug/netX.0 -> project root: ../../../appsettings.json
        yield return Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "appsettings.json"));
    }

    private static string? NormalizeDbPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());

        // If user points to Jellyfin data directory, auto-resolve to library.db
        if (Directory.Exists(expanded))
        {
            var candidate = Path.Combine(expanded, "library.db");
            return File.Exists(candidate) ? candidate : expanded;
        }

        // If path ends with separator, treat as directory
        if (expanded.EndsWith(Path.DirectorySeparatorChar) || expanded.EndsWith(Path.AltDirectorySeparatorChar))
        {
            var dir = expanded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.Combine(dir, "library.db");
            return File.Exists(candidate) ? candidate : dir;
        }

        return expanded;
    }

    private static void ConfigureSerilog()
    {
        var logsDir = Path.Combine(Environment.CurrentDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        var hostLogPath = Path.Combine(logsDir, "host.jsonl");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(new RenderedCompactJsonFormatter(), hostLogPath)
            .CreateLogger();
    }
}
