using System.IO;
using System.Text.Json;

namespace SubTubular
{
    internal interface DataStore
    {
        T Get<T>(string key);
        void Set<T>(string key, T value);
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

        public T Get<T>(string key)
        {
            var path = GetPath(key);
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json);
        }

        public void Set<T>(string key, T value)
        {
            var json = JsonSerializer.Serialize(value);
            File.WriteAllText(GetPath(key), json);
        }
    }
}