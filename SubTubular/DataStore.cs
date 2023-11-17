using System.Text.Json;

namespace SubTubular;

public interface DataStore
{
    DateTime? GetLastModified(string key);
    Task<T> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
}

public sealed class JsonFileDataStore : DataStore
{
    internal const string FileExtension = ".json";

    private readonly string directory;

    public JsonFileDataStore(string directory)
    {
        this.directory = directory;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
    }

    private static string GetFileName(string key) => key + FileExtension;
    private string GetPath(string key) => Path.Combine(directory, GetFileName(key));

    public DateTime? GetLastModified(string key)
    {
        var path = GetPath(key);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return default;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            File.Delete(path); // delete corrupted or incorrectly formatted cache
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        await File.WriteAllTextAsync(GetPath(key), json);
    }

    internal IEnumerable<string> GetKeysByPrefix(string keyPrefix, ushort? notAccessedForDays)
        => FileHelper.GetFiles(directory, GetFileName(keyPrefix + "*"), notAccessedForDays)
            .Select(file => Path.GetFileNameWithoutExtension(file.Name));
}