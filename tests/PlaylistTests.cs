using SubTubular.Extensions;

namespace Tests;

[TestClass]
public class PlaylistTests
{
    [TestMethod]
    public void RefreshOrdersVideosAndUpdatesShardNumbersCorrectly()
    {
        const ushort shardSize = 10;
        Playlist.ShardSize = shardSize;
        var playlist = new Playlist { Title = "test", ThumbnailUrl = "" };
        List<int> videoIds = [];

        // loads initial two shards 0, 1
        AddVideoShards(at: 0, start: 3, count: 2);
        // prepends two shards -2, -1 (like after Uploads playlist was added to remotely)
        AddVideoShards(at: 0, start: 1, count: 2);
        // appends shard 2 (simulating skipping or taking more videos)
        AddVideoShards(at: 4, start: 5, count: 1);
        // prepends shard -3 (like after Uploads playlist was added to remotely)
        AddVideoShards(at: 0, start: 0, count: 1);

        AssertCollectionsEqual(GenerateShards(0, 6, shardSize).ToList(), videoIds);

        using (playlist.CreateChangeToken())
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Select(v => v.Id).ToList();
            AssertCollectionsEqual(videoIds.ConvertAll(i => i.ToString()), actualVideoIds);

            var actualShardNumbers = videos.Select(v => v.ShardNumber!.Value).ToList();
            var expectedShardNumbers = new short[] { -3, -2, -1, 0, 1, 2 }.SelectMany(n => Enumerable.Repeat(n, shardSize)).ToList();
            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        void AddVideoShards(int at, int start, int count)
        {
            videoIds.InsertRange(at * shardSize, GenerateShards(start, count, shardSize));

            using (playlist.CreateChangeToken())
            {
                foreach (var id in videoIds) playlist.TryAddVideoId(id.ToString(), (uint)videoIds.IndexOf(id));
                playlist.UpdateShardNumbers();
            }
        }
    }

    [TestMethod]
    public void RemovingVideosRemotelyUpdatesCache()
    {
        const ushort shardSize = 10;
        Playlist.ShardSize = shardSize;
        var playlist = new Playlist { Title = "test", ThumbnailUrl = "" };

        // add 3 shards
        List<int> videoIds = GenerateShards(0, 3, shardSize).ToList();
        AddVideos();

        // drop every third video
        videoIds.RemoveAll(x => x % 3 == 0);
        AddVideos();

        using (playlist.CreateChangeToken())
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Select(v => v.Id).ToList();
            List<string> expectedVideoIds = videoIds.ConvertAll(i => i.ToString());
            AssertCollectionsEqual(expectedVideoIds, actualVideoIds);

            var actualShardNumbers = videos.Select(v => v.ShardNumber!.Value).ToList();

            var expectedShardNumbers = new short[] { 0, 1, 2 }
                // every third decade has 4 ints cleanly divisible by 3, the others 3
                .SelectMany(n => Enumerable.Repeat(n, shardSize - (n % 3 == 0 ? 4 : 3))).ToList();

            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        // add all videos back, but in reverse order
        videoIds = GenerateShards(0, 3, shardSize).Reverse().ToList();
        AddVideos();

        using (playlist.CreateChangeToken())
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Select(v => v.Id).ToList();
            var expectedVideoIds = videoIds.ConvertAll(i => i.ToString());
            AssertCollectionsEqual(expectedVideoIds, actualVideoIds);

            var actualShardNumbers = videos.Select(v => v.ShardNumber!.Value).ToList();
            var expectedShardNumbers = new short[] { 2, 1, 0 }.SelectMany(n => Enumerable.Repeat(n, shardSize)).ToList();

            //shard numbers stay the same as on initial load
            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        void AddVideos()
        {
            using (playlist.CreateChangeToken())
            {
                foreach (var id in videoIds) playlist.TryAddVideoId(id.ToString(), (uint)videoIds.IndexOf(id));
                playlist.UpdateShardNumbers();
            }
        }
    }

    [TestMethod]
    public void AdjacentPlaylistIndexesEndUpInTheSameShard()
    {
        const ushort halfShardSize = 5;

        Playlist.ShardSize = halfShardSize * 2;
        var playlist = new Playlist { Title = "test", ThumbnailUrl = "" };
        List<int> videoIds = [];

        // add first half of shard 0
        AddHalfShard(at: 0, start: 2, count: 1);
        // prepends first half of shard 1 (like after Uploads playlist was added to remotely)
        AddHalfShard(at: 0, start: 1, count: 1);
        // appends shard 4 (simulating skipping or taking more videos)
        AddHalfShard(at: 2, start: 3, count: 1);
        // prepends shard 5 (like after Uploads playlist was added to remotely)
        AddHalfShard(at: 0, start: 0, count: 1);

        AssertCollectionsEqual(GenerateShards(0, 4, halfShardSize).ToList(), videoIds);

        using (playlist.CreateChangeToken())
        {
            var videos = playlist.GetVideos().ToList();
            var actualVideoIds = videos.Select(v => v.Id).ToList();
            AssertCollectionsEqual(videoIds.ConvertAll(i => i.ToString()), actualVideoIds);

            var actualShardNumbers = videos.Select(v => v.ShardNumber!.Value).ToList();
            var expectedShardNumbers = new short[] { -1, 0 }.SelectMany(n => Enumerable.Repeat(n, halfShardSize * 2)).ToList();
            AssertCollectionsEqual(expectedShardNumbers, actualShardNumbers);
        }

        void AddHalfShard(int at, int start, int count)
        {
            videoIds.InsertRange(at * halfShardSize, GenerateShards(start, count, halfShardSize));

            using (playlist.CreateChangeToken())
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
