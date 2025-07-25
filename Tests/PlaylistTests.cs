﻿using SubTubular.Extensions;

namespace Tests;

[TestClass]
public class PlaylistTests
{
    [TestMethod]
    public async Task RefreshOrdersVideosAndUpdatesShardNumbersCorrectly()
    {
        const ushort shardSize = 10;
        Playlist.ShardSize = shardSize;
        var playlist = new Playlist { Title = "test", ThumbnailUrl = "" };
        List<int> videoIds = [];

        // loads initial two shards 0, 1
        await AddVideoShards(at: 0, start: 3, count: 2);
        // prepends two shards -2, -1 (like after Uploads playlist was added to remotely)
        await AddVideoShards(at: 0, start: 1, count: 2);
        // appends shard 2 (simulating skipping or taking more videos)
        await AddVideoShards(at: 4, start: 5, count: 1);
        // prepends shard -3 (like after Uploads playlist was added to remotely)
        await AddVideoShards(at: 0, start: 0, count: 1);

        AssertCollectionsEqual([.. GenerateShards(0, 6, shardSize)], videoIds);

        await using (playlist.CreateChangeToken(() => Task.CompletedTask))
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Ids().ToList();
            AssertCollectionsEqual(videoIds.ConvertAll(i => i.ToString()), actualVideoIds);

            var actualShardNumbers = videos.ConvertAll(v => v.ShardNumber!.Value);
            var expectedShardNumbers = new short[] { -3, -2, -1, 0, 1, 2 }.SelectMany(n => Enumerable.Repeat(n, shardSize)).ToList();
            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        async Task AddVideoShards(int at, int start, int count)
        {
            videoIds.InsertRange(at * shardSize, GenerateShards(start, count, shardSize));

            await using (playlist.CreateChangeToken(() => Task.CompletedTask))
            {
                foreach (var id in videoIds) playlist.TryAddVideoId(id.ToString(), (uint)videoIds.IndexOf(id));
                playlist.UpdateShardNumbers();
            }
        }
    }

    [TestMethod]
    public async Task RemovingVideosRemotelyUpdatesCache()
    {
        const ushort shardSize = 10;
        Playlist.ShardSize = shardSize;
        var playlist = new Playlist { Title = "test", ThumbnailUrl = "" };

        // add 3 shards
        List<int> videoIds = [.. GenerateShards(0, 3, shardSize)];
        await AddVideos();

        // drop every third video
        videoIds.RemoveAll(x => x % 3 == 0);
        await AddVideos();

        await using (playlist.CreateChangeToken(() => Task.CompletedTask))
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Ids().ToList();
            List<string> expectedVideoIds = videoIds.ConvertAll(i => i.ToString());
            AssertCollectionsEqual(expectedVideoIds, actualVideoIds);

            var actualShardNumbers = videos.ConvertAll(v => v.ShardNumber!.Value);

            var expectedShardNumbers = new short[] { 0, 1, 2 }
                // every third decade has 4 ints cleanly divisible by 3, the others 3
                .SelectMany(n => Enumerable.Repeat(n, shardSize - (n % 3 == 0 ? 4 : 3))).ToList();

            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        // add all videos back, but in reverse order
        videoIds = [.. GenerateShards(0, 3, shardSize).Reverse()];
        await AddVideos();

        await using (playlist.CreateChangeToken(() => Task.CompletedTask))
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Ids().ToList();
            var expectedVideoIds = videoIds.ConvertAll(i => i.ToString());
            AssertCollectionsEqual(expectedVideoIds, actualVideoIds);

            var actualShardNumbers = videos.ConvertAll(v => v.ShardNumber!.Value);
            var expectedShardNumbers = new short[] { 2, 1, 0 }.SelectMany(n => Enumerable.Repeat(n, shardSize)).ToList();

            //shard numbers stay the same as on initial load
            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        async Task AddVideos()
        {
            await using (playlist.CreateChangeToken(() => Task.CompletedTask))
            {
                foreach (var id in videoIds) playlist.TryAddVideoId(id.ToString(), (uint)videoIds.IndexOf(id));
                playlist.UpdateShardNumbers();
            }
        }
    }

    [TestMethod]
    public async Task AdjacentPlaylistIndexesEndUpInTheSameShard()
    {
        const ushort halfShardSize = 5;

        Playlist.ShardSize = halfShardSize * 2;
        var playlist = new Playlist { Title = "test", ThumbnailUrl = "" };
        List<int> videoIds = [];

        // add first half of shard 0
        await AddHalfShard(at: 0, start: 2, count: 1);
        // prepends first half of shard 1 (like after Uploads playlist was added to remotely)
        await AddHalfShard(at: 0, start: 1, count: 1);
        // appends shard 4 (simulating skipping or taking more videos)
        await AddHalfShard(at: 2, start: 3, count: 1);
        // prepends shard 5 (like after Uploads playlist was added to remotely)
        await AddHalfShard(at: 0, start: 0, count: 1);

        AssertCollectionsEqual([.. GenerateShards(0, 4, halfShardSize)], videoIds);

        await using (playlist.CreateChangeToken(() => Task.CompletedTask))
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Ids().ToList();
            AssertCollectionsEqual(videoIds.ConvertAll(i => i.ToString()), actualVideoIds);

            var actualShardNumbers = videos.ConvertAll(v => v.ShardNumber!.Value);
            var expectedShardNumbers = new short[] { -1, 0 }.SelectMany(n => Enumerable.Repeat(n, halfShardSize * 2)).ToList();
            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        async Task AddHalfShard(int at, int start, int count)
        {
            videoIds.InsertRange(at * halfShardSize, GenerateShards(start, count, halfShardSize));

            await using (playlist.CreateChangeToken(() => Task.CompletedTask))
            {
                foreach (var id in videoIds) playlist.TryAddVideoId(id.ToString(), (uint)videoIds.IndexOf(id));
                playlist.UpdateShardNumbers();
            }
        }
    }

    private static void AssertCollectionsEqual<T>(List<T> expected, List<T> actual)
    {
        CollectionAssert.AreEqual(expected, actual,
$@"
Expected: {expected.Select(v => v!.ToString()!).Join(", ")}
 but got: {actual.Select(v => v!.ToString()!).Join(", ")}
");
    }

    private static IEnumerable<int> GenerateShards(int startIndex, int count, ushort shardSize)
        => Enumerable.Range(startIndex * shardSize, count * shardSize);
}
