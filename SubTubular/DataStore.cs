using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;

namespace SubTubular;

public interface DataStore
{
    string FileExtension { get; }
    DateTime? GetLastModified(string key);
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
    IEnumerable<string> GetKeysByPrefix(string keyPrefix, ushort? notAccessedForDays);
    IEnumerable<string> DeleteFiles(string searchPattern = "*", ushort? notAccessedForDays = null, bool simulate = false);
}

public abstract class FileDataStore : DataStore
{
    private readonly string fileExtension;
    private readonly string directory;

    public string FileExtension => fileExtension;

    protected FileDataStore(string directory, string fileExtension)
    {
        this.fileExtension = fileExtension;
        this.directory = directory;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
    }

    private string GetFileName(string key) => key + fileExtension;
    private string GetPath(string key) => Path.Combine(directory, GetFileName(key));

    protected abstract Task<T?> DeserializeFrom<T>(string path);
    protected abstract Task SerializeToPath<T>(T value, string path);

    public async Task<T?> GetAsync<T>(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return default;

        try
        {
            return await DeserializeFrom<T>(path);
        }
        catch
        {
            File.Delete(path); // delete corrupted or incorrectly formatted cache
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value)
    {
        var path = GetPath(key);

        if (value == null) File.Delete(path);
        else await SerializeToPath(value, path);
    }

    public DateTime? GetLastModified(string key)
    {
        var path = GetPath(key);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    public IEnumerable<string> GetKeysByPrefix(string keyPrefix, ushort? notAccessedForDays)
        => FileHelper.GetFiles(directory, GetFileName(keyPrefix + "*"), notAccessedForDays)
            .Select(file => Path.GetFileNameWithoutExtension(file.Name));

    public IEnumerable<string> DeleteFiles(string searchPattern = "*",
        ushort? notAccessedForDays = null, bool simulate = false)
        => FileHelper.DeleteFiles(directory, searchPattern, notAccessedForDays, simulate);
}

public class JsonFileDataStore : FileDataStore
{
    public JsonFileDataStore(string directory) : base(directory, ".json") { }

    protected override async Task<T?> DeserializeFrom<T>(string path) where T : default
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T?>(json);
    }

    protected override async Task SerializeToPath<T>(T value, string path)
    {
        var json = JsonSerializer.Serialize(value);
        await File.WriteAllTextAsync(path, json);
    }
}

public class BinaryFileDataStore : FileDataStore
{
    public BinaryFileDataStore(string directory) : base(directory, ".bin") { }

    protected override Task<T?> DeserializeFrom<T>(string path) where T : default
    {
        BinaryFormatter formatter = new();
        using (FileStream stream = new(path, FileMode.Open))
        {
            return Task.FromResult((T?)formatter.Deserialize(stream));
        }
    }

    protected override Task SerializeToPath<T>(T value, string path)
    {
        BinaryFormatter formatter = new();
        using (FileStream stream = new(path, FileMode.Create))
        {
            formatter.Serialize(stream, value!);
        }

        return Task.CompletedTask;
    }
}