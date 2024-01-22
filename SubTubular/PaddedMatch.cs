using System.Text.RegularExpressions;

namespace SubTubular;

/// <summary>A helper comparable to <see cref="Match"/> including one or multiple
/// <see cref="Included"/> matches padded with a number of characters on each end for context.
/// The <see cref="Range{T}.Start" /> and the (included, i.e. closed interval) <see cref="Range{T}.End" />
/// represent the indexes of the padded match in the full text it was matched in.</summary>
public sealed class PaddedMatch : Range<int>
{
    /// <summary>The text containing the <see cref="Included"/> matches including padding.</summary>
    public string Value { get; }

    /// <summary>Contains the internal match(es) with <see cref="IncludedMatch.Start"/> relative to <see cref="Value"/>.</summary>
    public IncludedMatch[] Included { get; }

    private PaddedMatch(int start, int end, string fullText)
        : base(start, end, endIncluded: true)
        // add 1 to calculate Length because both Start and End are included
        => Value = fullText.Substring(Start, End - Start + 1);

    /// <summary>Used for <paramref name="padding"/> a match with
    /// <paramref name="start"/> relative to <paramref name="fullText"/>.</summary>
    public PaddedMatch(int start, int length, ushort padding, string fullText)
        : this(start: GetPaddedStartIndex(start, padding),
            end: GetPaddedEndIndex(start, length, padding, fullText),
            fullText: fullText)
    {
        /*  recalculate internal match index (relative to Value)
            from padded start and full text index (both relative to full text) */
        Included = new[] { new IncludedMatch { Start = start - Start, Length = length } };
    }

    /// <summary>Used for creating a <see cref="PaddedMatch"/> without padding
    /// for situations in which you want to output the entire full text as <see cref="Value"/>
    /// and the <see cref="IncludedMatch.Start"/> of <paramref name="includedMatches"/>
    /// are already relative to <paramref name="value"/>.</summary>
    public PaddedMatch(string value, IncludedMatch[] includedMatches)
        : base(0, value.Length - 1, endIncluded: true)
    {
        Value = value;
        Included = includedMatches;
    }

    /// <summary>Used for merging <paramref name="overlapping"/> padded matches
    /// into one spanning all of them to avoid repetition in the output.</summary>
    internal PaddedMatch(IEnumerable<PaddedMatch> overlapping, string fullText)
        : this(overlapping.Min(m => m.Start), overlapping.Max(m => m.End), fullText)
    {
        var first = overlapping.First();

        Included = overlapping.SelectMany((paddedMatch, index) =>
        {
            if (paddedMatch == first) return paddedMatch.Included;

            var startDiff = paddedMatch.Start - first.Start;

            return paddedMatch.Included.Select(match => new IncludedMatch
            {
                Start = match.Start + startDiff,
                Length = match.Length
            });
        }).ToArray();
    }

    /// <summary>Used for creating a <see cref="PaddedMatch"/> spanning an entire <paramref name="caption"/>
    /// (so that <see cref="Caption.At"/> fits <see cref="Caption.Text"/> as well as <see cref="Value"/>)
    /// from a transitory <paramref name="paddedMatch"/>
    /// that only contains the minimum <see cref="SearchCommand.Padding"/>.</summary>
    /// <param name="captionStartIndex">The start index of the <see cref="Caption.Text"/>
    /// relative to the <see cref="CaptionTrack.GetFullText()"/>.</param>
    internal PaddedMatch(PaddedMatch paddedMatch, Caption caption, int captionStartIndex)
        : base(captionStartIndex, captionStartIndex + caption.Text.Length, endIncluded: true)
    {
        Value = caption.Text;
        var additionalStartPadding = paddedMatch.Start - captionStartIndex;

        Included = paddedMatch.Included.Select(match => new IncludedMatch
        {
            Start = match.Start + additionalStartPadding,
            Length = match.Length
        }).ToArray();
    }

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Value);
    public override bool Equals(object? obj) => obj == null ? false : obj.GetHashCode() == GetHashCode();

    // starting at zero or index minus padding
    private static int GetPaddedStartIndex(int start, ushort padding)
        => start <= padding ? 0 : start - (int)padding;

    // ending at last index of fullText or last match index plus padding
    private static int GetPaddedEndIndex(int start, int length, ushort padding, string fullText)
    {
        // substract 1 because start is included in length
        var paddedEnd = start + length - 1 + (int)padding;
        var lastIndex = fullText.Length - 1;
        return paddedEnd > lastIndex ? lastIndex : paddedEnd;
    }

    /// <summary>A structure for remembering the locations of matches included in a padded
    /// (and maybe merged) match. Resembles <see cref="Match" /> semantically.</summary>
    public sealed class IncludedMatch
    {
        /// <summary>The start index of a match in <see cref="PaddedMatch.Value" />.</summary>
        public int Start { get; set; }

        /// <summary>The length of the match.</summary>
        public int Length { get; set; }
    }
}

/// <summary>Extensions for <see cref="PaddedMatch"/>.</summary>
public static class MatchExtenions
{
    /// <summary>Merges overlapping <paramref name="orTouching"/> <paramref name="matches"/> together using
    /// <paramref name="fullText"/> to facilitate selecting the <see cref="PaddedMatch.Value"/> of the merged match.</summary>
    public static IEnumerable<PaddedMatch> MergeOverlapping(this IEnumerable<PaddedMatch> matches, string fullText, bool orTouching = true)
        => matches.GroupOverlapping(orTouching).Select(group => group.Count() == 1 ? group.First() : new PaddedMatch(group, fullText));
}