namespace SubTubular;

/// <summary>A group of <see cref="Matches"/> in a <see cref="Text"/>.</summary>
public sealed class MatchedText
{
    /// <summary>The complete original text containing the <see cref="Matches"/>.</summary>
    public string Text { get; }

    /// <summary>Contains the internal match(es) with <see cref="Match.Start"/>
    /// relative to <see cref="Text"/> ordered by <see cref="Match.Start"/>.</summary>
    public Match[] Matches { get; }

    public MatchedText(string text, params Match[] matches)
    {
        Text = text;
        Matches = matches.OrderBy(m => m.Start).ToArray();
    }

    /// <summary>A structure for remembering the locations of matches included in a <see cref="Text" />.
    /// Resembles a <see cref="System.Text.RegularExpressions.Match" /> semantically.</summary>
    public sealed class Match
    {
        /// <summary>The (included) start index of a match in <see cref="Text" />.</summary>
        public int Start { get; }

        /// <summary>The length of the match.</summary>
        public int Length { get; }

        public Match(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int ExcludedEnd => Start + Length;
        public int IncludedEnd => ExcludedEnd - 1;

        public override int GetHashCode() => HashCode.Combine(Start, Length);
        public override bool Equals(object? obj) => obj?.GetHashCode() == GetHashCode();
    }
}

public static class MatchedTextExtensions
{
    /// <summary>Splits the <see cref="MatchedText.Matches"/> of <paramref name="matchedText"/> into groups
    /// taking into account the <paramref name="matchPadding"/> the <see cref="MatchedText.Matches"/> will be displayed with
    /// and returns a new <see cref="MatchedText"/> for each group.
    /// I.e. if two <see cref="MatchedText.Matches"/> are so close together that their <paramref name="matchPadding"/>
    /// would overlap or touch, they're merged into the same group. Otherwise they end up in different groups.
    /// If <paramref name="matchedText"/> contains a single match, it is returned unaltered.
    /// If it contains no <see cref="MatchedText.Matches"/>, no new <see cref="MatchedText"/> is returned.</summary>
    /// <param name="matchPadding">The number of characters each <see cref="MatchedText.Match"/> will be displayed in for context.</param>
    public static IEnumerable<MatchedText> SplitIntoPaddedGroups(this MatchedText matchedText, uint matchPadding)
    {
        // no sorting or splitting required
        if (matchedText.Matches.Length < 2)
        {
            if (matchedText.Matches.Length == 1) yield return matchedText;
            yield break;
        }

        var sortedMatches = matchedText.Matches.OrderBy(m => m.Start).ToList();

        // seed groups with one containing the first match
        List<List<MatchedText.Match>> seed = [new() { sortedMatches[0] }];

        // group remaining matches
        var groupedMatches = sortedMatches.Skip(1).Aggregate(seed, (groups, currentMatch) =>
        {
            var lastGroup = groups.Last();
            var lastMatch = lastGroup.Last();

            // if the current match overlaps or touches (considering the padding) the last match of the last group, add it to the group
            if (currentMatch.Start <= lastMatch.Start + lastMatch.Length + matchPadding) lastGroup.Add(currentMatch);
            else groups.Add([currentMatch]); // otherwise add it to a new group

            return groups;
        });

        // yield a new MatchedText for each group
        foreach (var group in groupedMatches)
            // while passing on the entire Text so that Match.Start indexes remain correct
            yield return new MatchedText(matchedText.Text, [.. group]);
    }

    /// <summary>Writes the <see cref="MatchedText.Matches"/> in <paramref name="matchedText"/>
    /// using <paramref name="write"/> for the <paramref name="matchPadding"/>
    /// and <paramref name="highlight"/> for each <see cref="MatchedText.Match"/> itself.
    /// If no <paramref name="matchPadding"/> is provided the entire <see cref="MatchedText.Text"/> is written;
    /// otherwise it will be cropped to the <paramref name="matchPadding"/>
    /// around the first and last <see cref="MatchedText.Match"/>.</summary>
    /// <typeparam name="T">The type of widget <paramref name="write"/> and <paramref name="highlight"/> return.</typeparam>
    /// <param name="write">A function returning a widget for the unmatched parts of the output <see cref="MatchedText.Text"/>.</param>
    /// <param name="highlight">A function returning a widget for each <see cref="MatchedText.Match"/>.</param>
    /// <param name="matchPadding">The number of characters each <see cref="MatchedText.Match"/> will be displayed in for context.</param>
    /// <returns>A sequence of widgets of type <typeparamref name="T"/> for the un-matched and matched parts of
    /// <paramref name="matchedText"/> in the order they appear in <see cref="MatchedText.Text"/>.</returns>
    public static IEnumerable<T> WriteHighlightingMatches<T>(this MatchedText matchedText,
        Func<string, T> write, Func<string, T> highlight, uint? matchPadding = null)
    {
        if (matchedText.Matches.Length == 0) return Enumerable.Empty<T>();

        var results = new List<T>();

        // counts characters written, starting from 0 or first match start - matchPadding
        var charsWritten = matchPadding == null ? 0
            : Math.Max(0, matchedText.Matches.Min(m => m.Start) - (int)matchPadding.Value);

        // writes a length of the text either highlit or not while counting the characters written
        void writeCounting(int length, bool highlit = false)
        {
            var phrase = matchedText.Text.Substring(charsWritten, length);

            if (highlit) results.Add(highlight(phrase));
            else results.Add(write(phrase));

            charsWritten += length;
        }

        foreach (var match in matchedText.Matches)
        {
            if (charsWritten < match.Start) writeCounting(match.Start - charsWritten); // write text preceding match
            writeCounting(match.Length - (charsWritten - match.Start), true); // write matched characters
        }

        // the excluded end of the text to write
        var end = matchPadding == null ? matchedText.Text.Length
            : Math.Min(matchedText.Text.Length, matchedText.Matches.Max(m => m.ExcludedEnd) + (int)matchPadding.Value);

        if (charsWritten < end) writeCounting(end - charsWritten); // write text trailing last match

        return results;
    }

    /// <summary>Writes the <see cref="MatchedText.Matches"/> in <paramref name="matchedText"/>
    /// using <paramref name="write"/> for the <paramref name="matchPadding"/>
    /// and <paramref name="highlight"/> for each <see cref="MatchedText.Match"/> itself.
    /// If no <paramref name="matchPadding"/> is provided the entire <see cref="MatchedText.Text"/> is written;
    /// otherwise it will be cropped to the <paramref name="matchPadding"/>
    /// around the first and last <see cref="MatchedText.Match"/>.</summary>
    /// <param name="write">A strategy for writing the unmatched parts of the output.</param>
    /// <param name="highlight">A strategy for writing each <see cref="MatchedText.Match"/>.</param>
    /// <param name="matchPadding">The number of characters each <see cref="MatchedText.Match"/> will be displayed in for context.</param>
    public static void WriteHighlightingMatches(this MatchedText matches,
        Action<string> write, Action<string> highlight, uint? matchPadding = null)
    {
        if (matches.Matches.Length == 0) return;

        matches.WriteHighlightingMatches(
            // just returning text here to point the compiler to the correct override and avoid recursive loop
            text => { write(text); return text; },
            text => { highlight(text); return text; },
            matchPadding);
    }
}
