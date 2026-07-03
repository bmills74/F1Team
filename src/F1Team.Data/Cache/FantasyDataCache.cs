using System.Text.Json;
using F1Team.Core.Models;

namespace F1Team.Data.Cache;

public sealed class FantasyDataCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _cacheFilePath;

    public FantasyDataCache()
        : this(GetDefaultCacheFilePath())
    {
    }

    public FantasyDataCache(string cacheFilePath)
    {
        _cacheFilePath = cacheFilePath;
    }

    public async Task SaveAsync(FantasyDataSet dataSet, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_cacheFilePath);
        await JsonSerializer.SerializeAsync(stream, dataSet, JsonOptions, cancellationToken);
    }

    public async Task<FantasyDataSet?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cacheFilePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_cacheFilePath);
        return await JsonSerializer.DeserializeAsync<FantasyDataSet>(stream, JsonOptions, cancellationToken);
    }

    private static string GetDefaultCacheFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "F1Team", "latest.json");
    }
}
