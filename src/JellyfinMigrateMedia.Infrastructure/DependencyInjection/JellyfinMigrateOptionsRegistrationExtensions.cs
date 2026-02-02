using Autofac;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;
using Microsoft.Extensions.Configuration;
// ReSharper disable UnusedMethodReturnValue.Global

namespace JellyfinMigrateMedia.Infrastructure.DependencyInjection;

public static class JellyfinMigrateOptionsRegistrationExtensions
{
    /// <summary>
    /// Registers application-wide options used across apps (Host/UI/etc.).
    /// Composition root should call this before registering modules that depend on these options.
    /// </summary>
    public static ContainerBuilder RegisterJellyfinMigrateOptions(
        this ContainerBuilder builder,
        IConfiguration? configuration,
        JellyfinMigrateSettings? userSettings = null,
        Action<JellyfinSqliteOptions>? configureSqlite = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var sqlite = BuildJellyfinSqliteOptions(configuration, userSettings);
        configureSqlite?.Invoke(sqlite);

        builder.RegisterInstance(sqlite)
            .AsSelf()
            .SingleInstance();

        return builder;
    }

    public static JellyfinSqliteOptions BuildJellyfinSqliteOptions(
        IConfiguration? configuration,
        JellyfinMigrateSettings? userSettings = null)
    {
        var options = new JellyfinSqliteOptions
        {
            // Connection string (if provided, takes precedence over DatabasePath).
            ConnectionString = configuration?["JellyfinMigrate:JellyfinSqliteConnectionString"]
                               ?? configuration?["JellyfinMigrate:ConnectionString"]
                               ?? configuration?["JellyfinSqlite:ConnectionString"],
            // Database path (common key used by Host/appsettings.json).
            DatabasePath = configuration?["JellyfinMigrate:JellyfinSqliteDbPath"]
                           ?? configuration?["JellyfinSqlite:DatabasePath"]
                           ?? userSettings?.JellyfinSqliteDbPath
        };

        var readOnlyRaw =
            configuration?["JellyfinMigrate:ReadOnly"]
            ?? configuration?["JellyfinSqlite:ReadOnly"];

        if (bool.TryParse(readOnlyRaw, out var readOnly))
            options.ReadOnly = readOnly;

        return options;
    }
}

