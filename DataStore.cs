using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SubTubular
{
    public interface DataStore
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value);
    }

    public class JsonFileDataStore : DataStore
    {
        private readonly string directory;

        public JsonFileDataStore(string directory)
        {
            this.directory = directory;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        protected virtual string GetPath(string key) => Path.Combine(directory, key + ".json");

        public virtual async Task<T> GetAsync<T>(string key)
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return default;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json);
        }

        public virtual async Task SetAsync<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            await File.WriteAllTextAsync(GetPath(key), json);
        }

        internal void Clear() => Directory.Delete(directory, true);
    }
}