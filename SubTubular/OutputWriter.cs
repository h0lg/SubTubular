using SubTubular.Extensions;

namespace SubTubular;

public abstract class OutputWriter(OutputCommand command)
{
    protected readonly OutputCommand command = command;

    public bool WroteResults { get; private set; }

    public virtual short GetWidth() => command.OutputWidth;
    public abstract short GetWrittenInCurrentLine();
    private IndentedText CreateIndent() => new(GetWrittenInCurrentLine(), GetWidth(), () => GetWrittenInCurrentLine());

    public abstract void Write(string text);
    public abstract void WriteHighlighted(string text);
    public abstract void WriteLine(string? text = null);
    public abstract void WriteUrl(string url);

    public virtual void WriteHeader()
    {
        Write(command.Describe(withScopes: false));
        WriteLine();
        WriteScopes("channels", command.Channels);
        WriteScopes("playlists", command.Playlists);
        WriteScopes("videos", command.Videos);
        WriteLine();

        void WriteScopes(string label, params CommandScope?[]? scopes)
        {
            if (scopes == null) return;
            var validScopes = scopes.WithValue().GetValid();
            if (!validScopes.Any()) return;
            Write(label + " ");
            var indent = CreateIndent();
            foreach (var scope in validScopes) Describe(scope, indent);
            WriteLine();
        }
    }

    private void Describe(CommandScope scope, IndentedText indent)
    {
        foreach (var line in scope.Describe())
        {
            Write(line);
            indent.StartNewLine(this);
        }
    }

    private void WriteHighlightingMatches(MatchedText matched, IndentedText? indent = null, uint? padding = null)
    {
        if (indent == null)
        {
            matched.WriteHighlightingMatches(Write, WriteHighlighted, padding);
            return;
        }

        var text = matched.Text;
        text = indent.Wrap(text); // inserts line breaks to wrap and spaces to indent into block

        #region accumulate info about wrapped lines for mapping padded matches (relative to unwrapped text)

        /*  a list of infos about each wrapped line with each item containing:
            1 number of characters inserted before text in wrapped line compared to unwrapped text
                (index diff between wrapped and unwrapped text)
            2 first index of line in unwrapped text
            3 last index of line in unwrapped text */
        var lineInfos = new List<(int charOffset, int firstIndex, int lastIndex)>();

        foreach (var line in text.Split(Environment.NewLine).Select(line => line.Trim()))
        {
            var previousLine = lineInfos.LastOrDefault();

            if (previousLine == default) lineInfos.Add((0, 0, line.Length)); // first line
            else
            {
                // start search from previous line's end index to avoid accidental matches in duplicate lines
                var previousLineEnd = previousLine.lastIndex; // in unwrapped text
                var startInWrappedText = text.IndexOf(line, startIndex: previousLine.charOffset + previousLineEnd);
                var startInUnwrappedText = matched.Text.IndexOf(line, startIndex: previousLineEnd);

                lineInfos.Add((startInWrappedText - startInUnwrappedText,
                    startInUnwrappedText, startInUnwrappedText + line.Length));
            }
        }
        #endregion

        List<MatchedText.Match> indentedMatches = new();

        foreach (var match in matched.Matches)
        {
            /*  shift match Length the number of chars inserted between
                line containing Start and line containing end of match
                and do so before modifying match Start */
            var end = match.Start + match.Length;
            var lineContainingMatchEnd = lineInfos.LastOrDefault(x => x.firstIndex <= end);

            // shift match Start the number of characters inserted in wrapped text before line
            var lineContainingMatchStart = lineInfos.LastOrDefault(x => x.firstIndex <= match.Start);
            var start = lineContainingMatchStart == default ? match.Start : match.Start + lineContainingMatchStart.charOffset;

            var length = lineContainingMatchEnd == lineContainingMatchStart ? match.Length
                : match.Length + lineContainingMatchEnd.charOffset - lineContainingMatchStart.charOffset;

            indentedMatches.Add(new MatchedText.Match(start, length));
        }

        var indented = new MatchedText(text, indentedMatches.ToArray());
        indented.WriteHighlightingMatches(Write, WriteHighlighted, padding);
    }

    public void WriteVideoResult(VideoSearchResult result, uint matchPadding)
    {
        var videoUrl = Youtube.GetVideoUrl(result.Video.Id);

        if (result.TitleMatches != null) WriteHighlightingMatches(result.TitleMatches, CreateIndent());
        else Write(result.Video.Title);

        WriteLine();
        Write($"{result.Video.Uploaded:g} ");
        WriteUrl(videoUrl);
        WriteLine();

        if (result.DescriptionMatches != null)
        {
            Write("  in description: ");

            var splitMatches = result.DescriptionMatches.SplitIntoPaddedGroups(matchPadding).ToArray();

            for (int i = 0; i < splitMatches.Length; i++)
            {
                if (i > 0) Write("    ");

                WriteHighlightingMatches(splitMatches[i], CreateIndent(), matchPadding);
                WriteLine();
            }
        }

        if (result.KeywordMatches.HasAny())
        {
            Write("  in keywords: ");
            var lastKeyword = result.KeywordMatches!.Last();

            foreach (var match in result.KeywordMatches!)
            {
                WriteHighlightingMatches(match);

                if (match != lastKeyword) Write(", ");
                else WriteLine();
            }
        }

        if (result.MatchingCaptionTracks.HasAny())
        {
            foreach (var trackResult in result.MatchingCaptionTracks!)
            {
                WriteLine("  " + trackResult.Track.LanguageName);

                var displaysHour = trackResult.HasMatchesWithHours(matchPadding);
                var splitMatches = trackResult.Matches.SplitIntoPaddedGroups(matchPadding);

                foreach (var matched in splitMatches)
                {
                    var (synced, captionAt) = trackResult.SyncWithCaptions(matched, matchPadding);
                    var offset = TimeSpan.FromSeconds(captionAt).FormatWithOptionalHours().PadLeft(displaysHour ? 7 : 5);
                    Write($"    {offset} ");

                    var indent = CreateIndent();
                    WriteHighlightingMatches(synced, indent, matchPadding);

                    const string urlPadding = "    ";
                    var url = $"{videoUrl}?t={captionAt}";

                    if (indent == null) WriteLine();
                    else if (indent.FitsCurrentLine(urlPadding.Length + url.Length)) Write(urlPadding);
                    else indent.StartNewLine(this);

                    WriteUrl(url);
                    WriteLine();
                }
            }
        }

        var tracksWithErrors = result.Video.CaptionTracks.Where(t => t.Error != null).ToArray();

        if (tracksWithErrors.Length > 0)
        {
            foreach (var track in tracksWithErrors)
                WriteLine($"  {track.LanguageName}: " + track.ErrorMessage);
        }

        WriteLine();
        WroteResults = true;
    }

    /// <summary>Displays the <paramref name="keywordsByScope"/> on the <see cref="Console"/>,
    /// most often occurring keyword first.</summary>
    /// <param name="keywordsByScope">The keywords and their corresponding number of occurrences.</param>
    public void ListKeywords(Dictionary<CommandScope, (string keyword, int foundInVideos)[]> keywordsByScope)
    {
        const string separator = " | ";
        var width = GetWidth();
        var indent = CreateIndent();

        foreach (var scope in keywordsByScope)
        {
            if (scope.Value.Length == 0) continue;
            Describe(scope.Key, indent);

            var line = string.Empty;

            /*  prevent breaking line mid-keyword on Console and breaks output into multiple lines for file
                without adding unnecessary separators at the start or end of lines */
            foreach (var tag in scope.Value)
            {
                var keyword = tag.foundInVideos + "x " + tag.keyword;

                // does keyword still fit into the current line?
                if ((line.Length + separator.Length + keyword.Length) < width)
                    line += separator + keyword; // add it
                else // keyword doesn't fit
                {
                    if (line.Length > 0) WriteLine(line); // write current line
                    line = keyword; // and start a new one
                }
            }

            WriteLine(line); // write remaining keywords
            WriteLine();
        }

        WroteResults = true;
    }

    /// <summary>A helper for writing multiple lines of text at the same indent.
    /// This is not quite block format, but almost.</summary>
    /// <remarks>Creates a new indented text block remembering the <paramref name="left"/> position
    /// and the <see cref="width"/> available for output.</remarks>
    public sealed class IndentedText(int left, int width, Func<int> getLeft)
    {
        /// <summary>Wraps <paramref name="text"/> into multiple lines
        /// indented by the remembered <see cref="left"/>
        /// fitting the remembered <see cref="width"/>.</summary>
        internal string Wrap(string text) => text.Wrap(width - left).Indent(left).Join(Environment.NewLine).TrimStart();

        /// <summary>Indicates whether the number of <paramref name="characters"/> fit the current line.</summary>
        internal bool FitsCurrentLine(int characters) => characters <= width - getLeft();

        /// <summary>Starts a new indented line using the supplied <paramref name="outputWriter"/>.</summary>
        internal void StartNewLine(OutputWriter outputWriter)
        {
            outputWriter.WriteLine(); // start a new one
            outputWriter.Write(string.Empty.PadLeft(left)); //and output the correct padding
        }
    }
}
