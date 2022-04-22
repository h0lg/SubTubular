using System.IO;
using System.Threading.Tasks;
using Lifti;
using Lifti.Serialization.Binary;

namespace SubTubular
{
    internal sealed class VideoIndex
    {
        private readonly string directory;
        private readonly FullTextIndexBuilder<string> builder;
        private readonly IIndexSerializer<string> serializer;

        internal VideoIndex(string directory)
        {
            this.directory = directory;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            //see https://mikegoatly.github.io/lifti/docs/getting-started/indexing-objects/
            builder = new FullTextIndexBuilder<string>()
                .WithDefaultTokenization(o => o.AccentInsensitive().CaseInsensitive())
                // see https://mikegoatly.github.io/lifti/docs/index-construction/withobjecttokenization/
                .WithObjectTokenization<Video>(itemOptions => itemOptions
                    .WithKey(v => v.Id)
                    .WithField(nameof(Video.Title), v => v.Title)
                    .WithField(nameof(Video.Keywords), v => v.Keywords)
                    .WithField(nameof(Video.Description), v => v.Description));

            serializer = new BinarySerializer<string>();
        }

        internal FullTextIndex<string> Create() => builder.Build();

        private string GetPath(string key) => Path.Combine(directory, key + ".idx");

        internal async ValueTask<FullTextIndex<string>> GetAsync(string key)
        {
            var path = GetPath(key);
            var file = new FileInfo(path);
            if (!file.Exists) return null;

            try
            {
                var index = Create();

                // see https://mikegoatly.github.io/lifti/docs/serialization/
                using (var reader = file.OpenRead())
                    await serializer.DeserializeAsync(index, reader, disposeStream: false);

                return index;
            }
            catch
            {
                file.Delete(); // delete corrupted index
                return null;
            }
        }

        internal async Task SaveAsync(FullTextIndex<string> index, string key)
        {
            var path = GetPath(key);

            // see https://mikegoatly.github.io/lifti/docs/serialization/
            using (var writer = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
                await serializer.SerializeAsync(index, writer, disposeStream: false);
        }
    }
}