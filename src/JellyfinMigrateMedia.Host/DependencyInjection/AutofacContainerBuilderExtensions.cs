using System.Reflection;
using Autofac;
using JellyfinMigrateMedia.Infrastructure.DependencyInjection;
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMethodReturnValue.Global
// ReSharper disable UnusedType.Global

// ReSharper disable UnusedMember.Global

namespace JellyfinMigrateMedia.Host.DependencyInjection;

public static class AutofacContainerBuilderExtensions
{
    public static ContainerBuilder RegisterModulesFromInterface(this ContainerBuilder builder, params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        var moduleTypes = assemblies
            .SelectMany(a => a.DefinedTypes)
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => typeof(IAutofacModule).IsAssignableFrom(t))
            .ToArray();

        foreach (var type in moduleTypes)
        {
            // Requires public parameterless ctor.
            var module = (Autofac.Core.IModule)Activator.CreateInstance(type.AsType())!;
            builder.RegisterModule(module);
        }

        return builder;
    }

    public static IContainer BuildContainerWithModules(this ContainerBuilder builder, params Assembly[] assemblies)
    {
        builder.RegisterModulesFromInterface(assemblies);
        return builder.Build();
    }
}

