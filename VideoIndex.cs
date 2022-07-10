using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lifti;
using Lifti.Serialization.Binary;
using Nito.AsyncEx;

namespace SubTubular
{
    internal sealed class VideoIndexRepository
    {
        private readonly string directory;
        private readonly FullTextIndexBuilder<string> builder;
        private readonly IIndexSerializer<string> serializer;

        internal VideoIndexRepository(string directory)
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
                    .WithField(nameof(Video.Description), v => v.Description))
                .WithQueryParser(o => o.WithFuzzySearchDefaults(
                    maxEditDistance: termLength => (ushort)(termLength / 3),
                    // avoid returning zero here to allow for edits in the first place
                    maxSequentialEdits: termLength => (ushort)(termLength < 6 ? 1 : termLength / 6)));

            serializer = new BinarySerializer<string>();
        }

        internal VideoIndex Build() => new VideoIndex(builder.Build());

        private string GetPath(string key) => Path.Combine(directory, key + ".idx");

        internal async ValueTask<VideoIndex> GetAsync(string key)
        {
            var path = GetPath(key);
            var file = new FileInfo(path);
            if (!file.Exists) return null;

            try
            {
                var index = Build();

                // see https://mikegoatly.github.io/lifti/docs/serialization/
                using (var reader = file.OpenRead())
                    await serializer.DeserializeAsync(index.Index, reader, disposeStream: false);

                return index;
            }
            catch
            {
                file.Delete(); // delete corrupted index
                return null;
            }
        }

        internal async Task SaveAsync(VideoIndex index, string key)
        {
            var path = GetPath(key);

            // see https://mikegoatly.github.io/lifti/docs/serialization/
            using (var writer = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
                await serializer.SerializeAsync(index.Index, writer, disposeStream: false);
        }
    }

    internal sealed class VideoIndex
    {
        private int writeQueue = 0, inBatch = 0, unsaved = 0;
        private readonly AsyncReaderWriterLock readWriteLock = new AsyncReaderWriterLock();

        internal FullTextIndex<string> Index { get; }

        internal VideoIndex(FullTextIndex<string> index) => Index = index;

        internal async Task AddAsync(Video video, CancellationToken cancellation, Func<Task> save)
        {
            cancellation.ThrowIfCancellationRequested();

            Interlocked.Increment(ref writeQueue);
            var writeLock = await readWriteLock.WriterLockAsync();

            try
            {
                if (inBatch == 0)
                {
                    Index.BeginBatchChange(); // see https://mikegoatly.github.io/lifti/docs/indexing-mutations/batch-mutations/
                    Interlocked.Exchange(ref inBatch, 1);
                }

                await Index.AddAsync(video);

                foreach (var track in video.CaptionTracks)
                    await Index.AddAsync(video.Id + "#" + track.LanguageName, track.GetFullText());

                Interlocked.Increment(ref unsaved);

                if (writeQueue <= 1 || unsaved >= 10)
                {
                    await Index.CommitBatchChangeAsync();
                    Interlocked.Exchange(ref inBatch, 0);

                    await save();
                    Interlocked.Exchange(ref unsaved, 0);
                }
            }
            finally
            {
                Interlocked.Decrement(ref writeQueue);
                if (writeLock != null) writeLock.Dispose();
                else System.Diagnostics.Debugger.Break();
            }
        }

        internal async Task<IEnumerable<SearchResult<string>>> SearchAsync(string query, CancellationToken cancellation)
        {
            using (var readLock = await readWriteLock.ReaderLockAsync())
                return Index.Search(query);
        }

        internal async Task<string[]> GetUnIndexedVideoIdsAsync(string[] videoIds)
        {
            using (var readLock = await readWriteLock.ReaderLockAsync())
                return videoIds.Where(id => !Index.Items.Contains(id)).ToArray();
        }
    }
}