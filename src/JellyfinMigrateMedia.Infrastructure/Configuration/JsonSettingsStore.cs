using System.Text.Json;

namespace JellyfinMigrateMedia.Infrastructure.Configuration;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string SettingsPath { get; }

    public JsonSettingsStore(string? settingsPath = null)
    {
        SettingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "JellyfinMigrate",
                "settings.json")
            : settingsPath;
    }

    public async ValueTask<JellyfinMigrateSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
            return new JellyfinMigrateSettings();

        await using var stream = File.OpenRead(SettingsPath);
        var settings = await JsonSerializer.DeserializeAsync<JellyfinMigrateSettings>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return settings ?? new JellyfinMigrateSettings();
    }

    public async ValueTask SaveAsync(JellyfinMigrateSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}

