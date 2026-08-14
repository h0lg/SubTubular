using System.Text.Json.Serialization;
using SubTubular.Extensions;

namespace SubTubular;

partial class CommandScope
{
    /// <summary>A collection of pre/validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    internal readonly List<ValidationResult> Validated = [];

    /// <summary>Indicates whether there are <see cref="Validated"/> aliases and all of them are remote-validated.</summary>
    [JsonIgnore] public bool IsValid => IsPrevalidated && Validated.All(v => v.IsRemoteValidated);

    [JsonIgnore] public bool IsPrevalidated => Validated.Count > 0;

    /// <summary>Only safe to access if <see cref="IsValid"/>.</summary>
    [JsonIgnore] public ValidationResult SingleValidated => Validated.Single();

    public void AddPrevalidated(string id, string url, string? alias = null)
        => Validated.Add(new ValidationResult { Id = id, Url = url, Alias = alias });

    /*/// <summary>Returns the <see cref="ValidationResult.Id"/> of all <see cref="Validated"/>,
    /// which are either pre- or remote validated depending on <see cref="IsValid"/>.</summary>
    internal string[] GetValidatedIds() => Validated.Ids().ToArray();*/

    public IEnumerable<ValidationResult> GetRemoteValidated(bool isRemoteValidated = true)
        => Validated.Where(vr => vr.IsRemoteValidated == isRemoteValidated);

    public abstract bool RequiresValidation();

    public sealed class ValidationResult
    {
        // pre-validation, checking input syntax
        /// <summary>The validated identifier of the <see cref="CommandScope"/>.</summary>
        public required string Id { get; set; }

        public string? Url { get; set; }

        /// <summary>Remembers the input alias from <see cref="VideosScope.Videos"/> this validation belongs to
        /// because it may not be comparable to <see cref="Id"/> or <see cref="Url"/> after parsing and normalization.
        /// For <see cref="VideosScope"/>s only.</summary>
        internal string? Alias { get; set; }

        /// <summary>Syntactically correct interpretations of <see cref="ChannelScope.Alias"/>
        /// returned by <see cref="Prevalidate.ChannelAlias(string)"/>.
        /// For <see cref="ChannelScope"/>s only.</summary>
        internal object[]? WellStructuredAliases { get; set; }

        // proper validation, including loading from YouTube if required.
        internal string? Title => Playlist?.Title ?? Video?.Title;
        internal bool IsRemoteValidated => Playlist != null || Video != null;

        /// <summary>For <see cref="VideosScope"/>s only.</summary>
        public Video? Video { get; set; }

        /// <summary>For <see cref="PlaylistLikeScope"/>s only.</summary>
        public Playlist? Playlist { get; internal set; }
    }
}

partial class PlaylistLikeScope
{
    /// <summary>Only has a value after Remote Validation, see <see cref="SetPlaylistAsync(Playlist)"/>.</summary>
    internal bool? SpansMultipleIndexShards { get; private set; }

    public override bool RequiresValidation() => Alias.IsNonWhiteSpace() && !IsValid;

    internal async Task SetPlaylistAsync(Playlist playlist)
    {
        SingleValidated.Playlist = playlist;
        Report(VideoList.Status.validated);
        SpansMultipleIndexShards = await LikelySpansMultipleIndexShardsAsync(playlist).ContinueAnywhere();
        playlist.ShardNumbersUpdated += async () => SpansMultipleIndexShards = await this.SpansMultipleIndexShardsAsync().ContinueAnywhere();
    }

    private async Task<bool> LikelySpansMultipleIndexShardsAsync(Playlist playlist)
    {
        if (Playlist.ShardSize < Take) return true;

        int required = playlist.Count == null ? RequiredVideoLoadCount
            : RequiredVideoLoadCount < playlist.Count ? RequiredVideoLoadCount // less than total count requested
            : playlist.Count.Value; // more requested than available, use available count

        var videos = await playlist.GetVideosAsync().ContinueAnywhere();
        if (required <= videos.Count) return await this.SpansMultipleIndexShardsAsync().ContinueAnywhere();

        // required videos not loaded; calculate shard numbers and figure it out
        int firstLoadedIndex = await playlist.GetIndexOfFirstLoadedVideoAsync().ContinueAnywhere();
        short? lowShard = Playlist.CalculateShardNumber(Skip, firstLoadedIndex);
        short? highShard = Playlist.CalculateShardNumber(required, firstLoadedIndex);
        return lowShard != highShard;
    }
}

public static class ScopeExtensions
{
    /// <summary>Filters the incoming <paramref name="scopes"/>
    /// returning those that <see cref="CommandScope.IsValid"/>,
    /// i.e. completely remote-validated.</summary>
    internal static IEnumerable<T> GetValid<T>(this IEnumerable<T> scopes) where T : CommandScope
        => scopes.Where(s => s.IsValid);

    public static IEnumerable<string> Ids(this IEnumerable<CommandScope.ValidationResult> results)
        => results.Select(r => r.Id);

    internal static async Task<bool> SpansMultipleIndexShardsAsync(this PlaylistLikeScope scope)
        => (await scope.SingleValidated.Playlist!.GetRelevantVideosAsync(scope).ContinueAnywhere())
            .GroupBy(v => v.ShardNumber).Count() > 1;
}
