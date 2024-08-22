using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubTubular;

public interface DataStore
{
    DateTime? GetLastModified(string key);
    ValueTask<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
    IEnumerable<string> GetKeysByPrefix(string keyPrefix, ushort? notAccessedForDays);
    IEnumerable<string> Delete(string? keyPrefix = null, string? key = null, ushort? notAccessedForDays = null, bool simulate = false);
}

public abstract class FileDataStore : DataStore
{
    private readonly string fileExtension;
    private readonly string directory;

    protected FileDataStore(string directory, string fileExtension)
    {
        this.fileExtension = fileExtension;
        this.directory = directory;
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
    }

    private string GetFileName(string key) => key + fileExtension;
    private string GetPath(string key) => Path.Combine(directory, GetFileName(key));

    protected abstract ValueTask<T?> DeserializeFrom<T>(string key, string path);
    protected abstract Task SerializeToPath<T>(T value, string path);

    public async ValueTask<T?> GetAsync<T>(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path)) return default;

        try { return await DeserializeFrom<T>(key, path); }
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

    public IEnumerable<string> Delete(string? keyPrefix = null, string? key = null, ushort? notAccessedForDays = null, bool simulate = false)
        => Delete(directory, fileExtension, keyPrefix, key, notAccessedForDays, simulate);

    internal static IEnumerable<string> Delete(string directory, string keySuffix, string? keyPrefix, string? key, ushort? notAccessedForDays, bool simulate)
        => FileHelper.DeleteFiles(directory, (key ?? keyPrefix + "*") + keySuffix, notAccessedForDays, simulate);
}
/*
public abstract class FileDataStore<T> : FileDataStore where T : class
{
    protected FileDataStore(string directory, string fileExtension) : base(directory, fileExtension)
    {
    }

    //private readonly FileDataStore fileDataStore;

    //public FileDataStore(FileDataStore fileDataStore)
    //{
    //    fileDataStore = new FileDataStore(directory, fileExtension);
    //}

    protected abstract Task<T> Deserialize(string key, string path);

    protected override async ValueTask<Ts> DeserializeFrom<Ts>(string key, string path)// where Ts : class
    {
        T t = await Deserialize(key, path);
        return (Ts)Convert.ChangeType(t, typeof(Ts));
    }

    //protected override Task<Ts?> DeserializeFrom<Ts>(string key, string path) where T : class
    //    => 

    //protected override async Task SerializeToPath<T>(T value, string path)
    //{
    //    var json = JsonSerializer.Serialize(value);
    //    await File.WriteAllTextAsync(path, json);
    //}
}
*/

public class JsonFileDataStore(string directory) : FileDataStore(directory, FileExtension)
{
    public const string FileExtension = ".json";
    private readonly JsonSerializerOptions options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    protected override async ValueTask<T?> DeserializeFrom<T>(string key, string path) where T : default
    {
        await using FileStream stream = new(path, FileMode.Open);
        return await JsonSerializer.DeserializeAsync<T?>(stream, options);
    }

    protected override async Task SerializeToPath<T>(T value, string path)
    {
        await using FileStream stream = new(path, FileMode.Create);
        await JsonSerializer.SerializeAsync(stream, value, options);
    }
}

/*
public class BinaryFileDataStore(string directory) : FileDataStore(directory, ".bin")
{
    protected override async ValueTask<T?> DeserializeFrom<T>(string key, string path) where T : default
    {
        BinaryFormatter formatter = new();
        await using FileStream stream = new(path, FileMode.Open);
        return (T?)formatter.Deserialize(stream);
    }

    protected override async Task SerializeToPath<T>(T value, string path)
    {
        BinaryFormatter formatter = new();
        await using FileStream stream = new(path, FileMode.Create);
        formatter.Serialize(stream, value!);
    }
}
*/