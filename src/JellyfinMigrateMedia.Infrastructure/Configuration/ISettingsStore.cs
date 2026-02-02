namespace JellyfinMigrateMedia.Infrastructure.Configuration;

public interface ISettingsStore
{
    string SettingsPath { get; }

    ValueTask<JellyfinMigrateSettings> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(JellyfinMigrateSettings settings, CancellationToken cancellationToken = default);
}

