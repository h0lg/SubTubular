using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
// dotnet add package Lifti.Core --source https://pkgs.dev.azure.com/goatly/LIFTI/_packaging/lifti-ci/nuget/v3/index.json --version 5.0.0-CI-20230620-190859
using Lifti;
using Lifti.Querying;
using Lifti.Serialization.Binary;

namespace SubTubular
{
    internal sealed class VideoIndexRepository
    {
        internal const string FileExtension = ".idx";

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
                    .WithField(nameof(Video.Description), v => v.Description)
                    .WithDynamicFields(nameof(Video.CaptionTracks), v => v.CaptionTracks,
                        ct => ct.LanguageName + VideoIndex.CaptionTrackFieldSuffix, ct => ct.GetFullText()))
                .WithQueryParser(o => o.WithFuzzySearchDefaults(
                    maxEditDistance: termLength => (ushort)(termLength / 3),
                    // avoid returning zero here to allow for edits in the first place
                    maxSequentialEdits: termLength => (ushort)(termLength < 6 ? 1 : termLength / 6)));

            serializer = new BinarySerializer<string>();
        }

        internal VideoIndex Build(string key)
        {
            VideoIndex videoIndex = null;

            // see https://mikegoatly.github.io/lifti/docs/index-construction/withindexmodificationaction/
            FullTextIndex<string> index = builder.WithIndexModificationAction(async idx => { await SaveAsync(videoIndex, key); }).Build();

            videoIndex = new VideoIndex(index);
            return videoIndex;
        }

        private string GetPath(string key) => Path.Combine(directory, key + FileExtension);

        internal async ValueTask<VideoIndex> GetAsync(string key)
        {
            var path = GetPath(key);
            var file = new FileInfo(path);
            if (!file.Exists) return null;

            try
            {
                var index = Build(key);

                // this is where I'd expect an exception when deserializing an index in the old format
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

        private async Task SaveAsync(VideoIndex index, string key)
        {
            // see https://mikegoatly.github.io/lifti/docs/serialization/
            using (var writer = new FileStream(GetPath(key), FileMode.OpenOrCreate, FileAccess.Write))
                await serializer.SerializeAsync(index.Index, writer, disposeStream: false);
        }
    }

    internal sealed class VideoIndex
    {
        internal const string CaptionTrackFieldSuffix = "_" + nameof(CaptionTrack.Captions);

        internal FullTextIndex<string> Index { get; }

        internal VideoIndex(FullTextIndex<string> index) => Index = index;

        internal string[] GetIndexed(IEnumerable<string> videoIds)
            => videoIds.Where(id => Index.Items.Contains(id)).ToArray();

        internal async Task AddAsync(Video video, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            await Index.AddAsync(video);
            video.UnIndexed = false; // to reset the flag
        }

        internal void BeginBatchChange() => Index.BeginBatchChange();
        internal Task CommitBatchChangeAsync() => Index.CommitBatchChangeAsync();

        /// <summary>Searches the index according to the specified <paramref name="command"/>,
        /// recombining the matches with <see cref="Video"/>s loaded using <paramref name="getVideoAsync"/>
        /// and returns <see cref="VideoSearchResult"/>s until all are processed
        /// or the <paramref name="cancellation"/> is invoked.</summary>
        /// <param name="command">Determines the <see cref="SearchCommand.Query"/> for the search
        /// and the <see cref="SearchCommand.OrderBy"/> and <see cref="SearchCommand.Padding"/> of the results.</param>
        /// <param name="relevantVideos"><see cref="Video.Id"/>s the search is limited to
        /// accompanied by their corresponding <see cref="Video.Uploaded"/> dates, if known.
        /// The latter are only used for <see cref="SearchCommand.OrderOptions.uploaded"/>
        /// and missing dates are determined by loading the videos using <paramref name="getVideoAsync"/>.</param>
        /// <param name="updatePlaylistVideosUploaded">A callback for updating the <see cref="Playlist.Videos"/>
        /// with the <see cref="Video.Uploaded"/> dates after loading them for <see cref="SearchCommand.OrderOptions.uploaded"/>.</param>
        internal async IAsyncEnumerable<VideoSearchResult> SearchAsync(SearchCommand command,
            Func<string, CancellationToken, Task<Video>> getVideoAsync,
            [EnumeratorCancellation] CancellationToken cancellation = default,
            IDictionary<string, DateTime?> relevantVideos = default,
            Func<IEnumerable<Video>, Task> updatePlaylistVideosUploaded = default)
        {
            cancellation.ThrowIfCancellationRequested();
            IEnumerable<SearchResult<string>> results;

            try { results = Index.Search(command.Query); }
            catch (QueryParserException ex) { throw new InputException("Error parsing query from --for | -f parameter: " + ex.Message, ex); }
            catch (Exception ex)
            {
                // this is where the exception occurs
                Debugger.Break();
                throw;
            }

            var matches = results
                // make sure to only return results for the requested videos if specified; index may contain more
                .Where(m => relevantVideos == default || relevantVideos.Keys.Contains(m.Key))
                .ToList();

            var previouslyLoadedVideos = new Video[0];
            var unIndexedVideos = new List<Video>();

            if (command is SearchPlaylistCommand searchPlaylist) // order playlist matches
            {
                var orderByUploaded = searchPlaylist.OrderBy.Contains(SearchPlaylistCommand.OrderOptions.uploaded);

                if (orderByUploaded)
                {
                    if (relevantVideos == default) relevantVideos = new Dictionary<string, DateTime?>();

                    var withoutUploadDate = matches.Where(m => !relevantVideos.ContainsKey(m.Key)
                        || relevantVideos[m.Key] == null).ToArray();

                    if (withoutUploadDate.Any()) // get upload dates for videos that we don't know it of
                    {
                        var getVideos = withoutUploadDate.Select(m => getVideoAsync(m.Key, cancellation)).ToArray();
                        await Task.WhenAll(getVideos).WithAggregateException();
                        previouslyLoadedVideos = getVideos.Select(t => t.Result).ToArray();
                        unIndexedVideos.AddRange(previouslyLoadedVideos.Where(v => v.UnIndexed));

                        foreach (var match in withoutUploadDate)
                            relevantVideos[match.Key] = previouslyLoadedVideos.Single(v => v.Id == match.Key).Uploaded;

                        if (updatePlaylistVideosUploaded != default)
                            await updatePlaylistVideosUploaded(previouslyLoadedVideos);
                    }
                }

                if (searchPlaylist.OrderBy.ContainsAny(SearchPlaylistCommand.Orders))
                {
                    var orderded = searchPlaylist.OrderBy.Contains(SearchPlaylistCommand.OrderOptions.asc)
                        ? matches.OrderBy(m => orderByUploaded ? relevantVideos[m.Key] : m.Score as object)
                        : matches.OrderByDescending(m => orderByUploaded ? relevantVideos[m.Key] : m.Score as object);

                    matches = orderded.ToList();
                }
            }

            foreach (var match in matches)
            {
                cancellation.ThrowIfCancellationRequested();

                // consider results for uncached videos stale
                if (unIndexedVideos.Any(video => video.Id == match.Key)) continue;

                var video = previouslyLoadedVideos.SingleOrDefault(v => v.Id == match.Key);

                if (video == null)
                {
                    video = await getVideoAsync(match.Key, cancellation);

                    if (video.UnIndexed)
                    {
                        unIndexedVideos.Add(video);
                        continue; // consider results for uncached videos stale
                    }
                }

                var result = new VideoSearchResult { Video = video };

                var titleMatches = match.FieldMatches.Where(m => m.FoundIn == nameof(Video.Title));

                if (titleMatches.Any()) result.TitleMatches = new PaddedMatch(video.Title,
                    titleMatches.SelectMany(m => m.Locations)
                        .Select(m => new PaddedMatch.IncludedMatch { Start = m.Start, Length = m.Length }).ToArray());

                result.DescriptionMatches = match.FieldMatches
                    .Where(m => m.FoundIn == nameof(Video.Description))
                    .SelectMany(m => m.Locations)
                    .Select(l => new PaddedMatch(l.Start, l.Length, command.Padding, video.Description))
                    .MergeOverlapping(video.Description)
                    .ToArray();

                var keywordMatches = match.FieldMatches
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

                result.MatchingCaptionTracks = match.FieldMatches.Where(m => m.FoundIn.EndsWith(CaptionTrackFieldSuffix)).Select(m =>
                {
                    var language = m.FoundIn.Remove(m.FoundIn.LastIndexOf(CaptionTrackFieldSuffix));
                    var track = video.CaptionTracks.SingleOrDefault(t => t.LanguageName == language);
                    var fullText = track.GetFullText();
                    var captionAtFullTextIndex = track.GetCaptionAtFullTextIndex();

                    var matches = m.Locations
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

                            return (new PaddedMatch(match, joinedCaption, first.Key), joinedCaption);
                        })
                        .OrderBy(tuple => tuple.Item2.At).ToList(); // return captions in order

                    return new VideoSearchResult.CaptionTrackResult { Track = track, Matches = matches };
                }).ToArray();

                yield return result;
            }

            if (unIndexedVideos.Count > 0)
            {
                // consider results for uncached videos stale and re-index them
                await UpdateAsync(unIndexedVideos, cancellation);

                // re-trigger search for re-indexed videos only
                Func<string, CancellationToken, Task<Video>> getReIndexedVideoAsync = async (id, cancellation)
                    => unIndexedVideos.SingleOrDefault(v => v.Id == id) ?? await getVideoAsync(id, cancellation);

                await foreach (var result in SearchAsync(command, getReIndexedVideoAsync, cancellation,
                    unIndexedVideos.ToDictionary(v => v.Id, v => v.Uploaded as DateTime?), updatePlaylistVideosUploaded))
                    yield return result;
            }
        }

        private async Task UpdateAsync(IEnumerable<Video> videos, CancellationToken cancellation)
        {
            var indexedKeys = Index.Items.GetIndexedItems().Select(i => i.Item).ToArray();
            BeginBatchChange();

            foreach (var video in videos)
            {
                await Task.WhenAll(indexedKeys.Where(key => key == video.Id)
                    .Select(key => Index.RemoveAsync(key))).WithAggregateException();

                await AddAsync(video, cancellation);
            }

            await CommitBatchChangeAsync();
        }
    }
}