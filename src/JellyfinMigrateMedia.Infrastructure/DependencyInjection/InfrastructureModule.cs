using Autofac;
using JellyfinMigrateMedia.Infrastructure.Configuration;
using JellyfinMigrateMedia.Infrastructure.Db;
using JellyfinMigrateMedia.Infrastructure.Services;

namespace JellyfinMigrateMedia.Infrastructure.DependencyInjection;

public sealed class InfrastructureModule : Module, IAutofacModule
{
    protected override void Load(ContainerBuilder builder)
    {
        // Register all non-abstract classes from this project (assembly) for convenient DI usage.
        // - AsSelf(): allows resolving concrete types (handy for app services)
        // - AsImplementedInterfaces(): allows resolving by interface where applicable
        builder.RegisterAssemblyTypes(ThisAssembly)
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false }
                && t != typeof(InfrastructureModule)
                // Options/settings must be explicitly configured by the composition root.
                // Auto-registering them leads to "empty" instances (e.g., DatabasePath = null).
                && t != typeof(JellyfinSqliteOptions)
                && t != typeof(JellyfinMigrateSettings))
            .AsSelf()
            .AsImplementedInterfaces()
            .InstancePerLifetimeScope();

        // NOTE: JellyfinSqliteOptions MUST be registered by the composition root (Host/UI).
        // This module intentionally does not bind configuration.

        builder.RegisterType<JellyfinSqliteConnectionFactory>()
            .As<IJellyfinDbConnectionFactory>()
            .SingleInstance();

        builder.RegisterType<MigrationService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MediaLibraryService>()
            .AsSelf()
            .SingleInstance();
    }
}

