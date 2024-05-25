namespace SubTubular;

partial class CommandScope
{
    /// <summary>A collection of validated URLs for the entities included in the scope.
    /// It translates non-URI identifiers in the scope of YouTube into URIs for <see cref="OutputCommand"/>s.</summary>
    internal readonly List<ValidationResult> Validated = new();

    internal bool IsValid => IsPrevalidated && Validated.All(v => v.IsRemoteValidated);
    internal bool IsPrevalidated => Validated.Count > 0;
    public ValidationResult SingleValidated => Validated.Single();

    internal void AddPrevalidated(string id, string url)
        => Validated.Add(new ValidationResult { Id = id, Url = url });

    /// <summary>Returns all pre-validated or validated <see cref="ValidationResult.Id"/> depending on <see cref="IsValid"/>.</summary>
    internal string[] GetValidatedIds() => Validated.Select(v => v.Id).ToArray();

    internal string GetValidatedId() => GetValidatedIds().Single();

    public sealed class ValidationResult
    {
        // pre-validation, checking input syntax
        /// <summary>The validated identifier of the <see cref="CommandScope"/>.</summary>
        public required string Id { get; set; }

        internal string? Url { get; set; }

        /// <summary>Syntactically correct interpretations of <see cref="ChannelScope.Alias"/>
        /// returned by <see cref="Prevalidate.ChannelAlias(string)"/>.
        /// For <see cref="ChannelScope"/>s only.</summary>
        internal object[]? WellStructuredAliases { get; set; }

        // proper validation, including loading from YouTube if required.
        internal string? Title => Playlist?.Title ?? Video?.Title;
        internal bool IsRemoteValidated => Playlist != null || Video != null;

        /// <summary>For <see cref="VideosScope"/>s only.</summary>
        internal Video? Video { get; set; }

        /// <summary>For <see cref="PlaylistLikeScope"/>s only.</summary>
        public Playlist? Playlist { get; internal set; }
    }
}

internal static class ScopeExtensions
{
    internal static IEnumerable<T> GetValid<T>(this IEnumerable<T> scopes) where T : CommandScope
        => scopes.Where(s => s.IsValid);
}
