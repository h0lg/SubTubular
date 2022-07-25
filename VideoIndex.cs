using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lifti;
using Lifti.Serialization.Binary;

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
            // see https://mikegoatly.github.io/lifti/docs/serialization/
            using (var writer = new FileStream(GetPath(key), FileMode.OpenOrCreate, FileAccess.Write))
                await serializer.SerializeAsync(index.Index, writer, disposeStream: false);
        }
    }

    internal sealed class VideoIndex
    {
        internal FullTextIndex<string> Index { get; }

        internal VideoIndex(FullTextIndex<string> index) => Index = index;

        internal string[] GetIndexed(IEnumerable<string> videoIds)
            => videoIds.Where(id => Index.Items.Contains(id)).ToArray();

        internal async Task AddAsync(Video video, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            await Index.AddAsync(video);

            foreach (var track in video.CaptionTracks)
                await Index.AddAsync(video.Id + "#" + track.LanguageName, track.GetFullText());
        }

        internal void BeginBatchChange() => Index.BeginBatchChange();
        internal Task CommitBatchChangeAsync() => Index.CommitBatchChangeAsync();

        /// <summary>Searches the index according to the specified <paramref name="command"/>,
        /// recombining the matches with <see cref="Video"/>s loaded using <paramref name="getVideoAsync"/>
        /// and returns <see cref="VideoSearchResult"/>s until all are processed
        /// or the <paramref name="cancellation"/> is invoked.</summary>
        /// <param name="command">Determines the <see cref="SearchCommand.Query"/> for the search
        /// and <see cref="SearchCommand.Padding"/> of the results.</param>
        /// <param name="relevantVideoIds"><see cref="Video.Id"/>s the search is limited to.</param>
        internal async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command,
            Func<string, CancellationToken, ValueTask<Video>> getVideoAsync,
            [EnumeratorCancellation] CancellationToken cancellation = default, string[] relevantVideoIds = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var results = Index.Search(command.Query);

            var resultsByVideoId = results.Select(result =>
                {
                    var ids = result.Key.Split('#');
                    var videoId = ids[0];
                    var language = ids.Length > 1 ? ids[1] : null;
                    return new { videoId, language, result };
                })
            // make sure to only return results for the requested videos; index may contain more
            .Where(m => relevantVideoIds == null || relevantVideoIds.Contains(m.videoId))
            .GroupBy(m => m.videoId);

            foreach (var group in resultsByVideoId)
            {
                cancellation.ThrowIfCancellationRequested();
                var video = await getVideoAsync(group.Key, cancellation);

                var result = new VideoSearchResult { Video = video };
                var metaDataMatch = group.SingleOrDefault(m => m.language == null);

                if (metaDataMatch != null)
                {
                    var titleMatches = metaDataMatch.result.FieldMatches.Where(m => m.FoundIn == nameof(Video.Title));

                    if (titleMatches.Any()) result.TitleMatches = new PaddedMatch(video.Title,
                        titleMatches.SelectMany(m => m.Locations)
                            .Select(m => new PaddedMatch.IncludedMatch { Start = m.Start, Length = m.Length }).ToArray());

                    result.DescriptionMatches = metaDataMatch.result.FieldMatches
                        .Where(m => m.FoundIn == nameof(Video.Description))
                        .SelectMany(m => m.Locations)
                        .Select(l => new PaddedMatch(l.Start, l.Length, command.Padding, video.Description))
                        .MergeOverlapping(video.Description)
                        .ToArray();

                    var keywordMatches = metaDataMatch.result.FieldMatches
                        .Where(m => m.FoundIn == nameof(Video.Keywords))
                        .ToArray();

                    if (keywordMatches.Any())
                    {
                        var joinedKeywords = string.Empty;

                        // remembers the index in the list of keywords and start index in joinedKeywords for each keyword
                        var keywordInfos = video.Keywords.Select((keyword, index) =>
                        {
                            var info = new { index, Start = joinedKeywords.Length };
                            joinedKeywords += keyword;
                            return info;
                        }).ToArray();

                        result.KeywordMatches = keywordMatches.SelectMany(match => match.Locations)
                            .Select(location => new
                            {
                                location, // represents the match location in joinedKeywords
                                          // used to calculate the match index within a matched keyword
                                keywordInfo = keywordInfos.TakeWhile(info => info.Start <= location.Start).Last()
                            })
                            .GroupBy(x => x.keywordInfo.index) // group matches by keyword
                            .Select(g => new PaddedMatch(video.Keywords[g.Key],
                                g.Select(x => new PaddedMatch.IncludedMatch
                                {
                                    // recalculate match index relative to keyword start
                                    Start = x.location.Start - x.keywordInfo.Start,
                                    Length = x.location.Length
                                }).ToArray()))
                            .ToArray();
                    }
                }

                result.MatchingCaptionTracks = group.Where(m => m.language != null).Select(m =>
                {
                    var track = video.CaptionTracks.SingleOrDefault(t => t.LanguageName == m.language);
                    var fullText = track.GetFullText();
                    var captionAtFullTextIndex = track.GetCaptionAtFullTextIndex();

                    var matches = m.result.FieldMatches.First().Locations
                        // use a temporary/transitory PaddedMatch to ensure the minumum configured padding
                        .Select(l => new PaddedMatch(l.Start, l.Length, command.Padding, fullText))
                        .MergeOverlapping(fullText)
                        /*  map transitory padded match to captions containing it and a new padded match
                            with adjusted included matches containing the joined text of the matched caption */
                        .Select(match =>
                        {
                            // find first and last captions containing parts of the padded match
                            var first = captionAtFullTextIndex.Last(x => x.Key <= match.Start);
                            var last = captionAtFullTextIndex.Last(x => first.Key <= x.Key && x.Key <= match.End);

                            var captions = captionAtFullTextIndex // span of captions containing the padded match
                                .Where(x => first.Key <= x.Key && x.Key <= last.Key).ToArray();

                            // return a single caption for all captions containing the padded match
                            var joinedCaption = new Caption
                            {
                                At = first.Value.At,
                                Text = captions.Select(x => x.Value.Text)
                                    .Where(text => !string.IsNullOrWhiteSpace(text)) // skip included line breaks
                                    .Select(text => text.NormalizeWhiteSpace(CaptionTrack.FullTextSeperator)) // replace included line breaks
                                    .Join(CaptionTrack.FullTextSeperator)
                            };

                            return Tuple.Create(new PaddedMatch(match, joinedCaption, first.Key), joinedCaption);
                        })
                        .OrderBy(tuple => tuple.Item2.At).ToList(); // return captions in order

                    return new VideoSearchResult.CaptionTrackResult { Track = track, Matches = matches };
                }).ToArray();

                yield return result;
            }
        }
    }
}