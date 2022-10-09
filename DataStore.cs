using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SubTubular
{
    internal interface DataStore
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value);

        bool Delete(string key);
    }

    internal sealed class JsonFileDataStore : DataStore
    {
        private readonly string directory;

        internal JsonFileDataStore(string directory)
        {
            this.directory = directory;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        }

        private static string GetFileName(string name) => name + ".json";
        private string GetPath(string key) => Path.Combine(directory, GetFileName(key));

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

        public bool Delete(string key) => FileHelper.DeleteFile(GetPath(key));

        internal void Clear(ushort? notAccessedForDays = null)
        {
            if (notAccessedForDays.HasValue)
            {
                var oldest = DateTime.Today.AddDays(-notAccessedForDays.Value);
                var paths = Directory.EnumerateFiles(directory).Where(path => File.GetLastAccessTime(path) < oldest);
                foreach (var path in paths) File.Delete(path);
            }
            else Directory.Delete(directory, true);
        }

        internal void Delete(Func<string, bool> isPathDeletable, ushort? notAccessedForDays)
            => FileHelper.DeleteFiles(directory, GetFileName("*"), notAccessedForDays, isPathDeletable);

        internal IEnumerable<string> GetKeysByPrefix(string keyPrefix, ushort? notAccessedForDays)
            => FileHelper.GetFiles(directory, GetFileName(keyPrefix + "*"), notAccessedForDays)
                .Select(file => Path.GetFileNameWithoutExtension(file.Name));
    }
}