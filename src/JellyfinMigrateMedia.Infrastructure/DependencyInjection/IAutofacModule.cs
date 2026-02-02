using Autofac.Core;

namespace JellyfinMigrateMedia.Infrastructure.DependencyInjection;

/// <summary>
/// Marker interface for Autofac modules to enable assembly scanning.
/// </summary>
public interface IAutofacModule : IModule
{
}

