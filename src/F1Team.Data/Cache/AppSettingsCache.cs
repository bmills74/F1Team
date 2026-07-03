using System.Text.Json;

namespace F1Team.Data.Cache;

public sealed class AppSettingsCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    public AppSettingsCache()
        : this(GetDefaultSettingsFilePath())
    {
    }

    public AppSettingsCache(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    public async Task SaveAsync(AppSettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_settingsFilePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    public async Task<AppSettingsSnapshot?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        return await JsonSerializer.DeserializeAsync<AppSettingsSnapshot>(stream, JsonOptions, cancellationToken);
    }

    private static string GetDefaultSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "F1Team", "settings.json");
    }
}
