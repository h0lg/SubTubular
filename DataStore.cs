using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SubTubular
{
    internal interface DataStore
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value);
    }

    internal sealed class JsonFileDataStore : DataStore
    {
        private readonly string directory;

        internal JsonFileDataStore(string directory)
        {
            this.directory = directory;

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private string GetPath(string key) => Path.Combine(directory, key + ".json");

        public async Task<T> GetAsync<T>(string key)
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return default;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task SetAsync<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            await File.WriteAllTextAsync(GetPath(key), json);
        }
    }
}