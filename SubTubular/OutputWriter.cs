using AngleSharp;
using AngleSharp.Dom;
using SubTubular.Extensions;

namespace SubTubular;

/// <summary>
/// Provides formatted and highlighted output for Console
/// as well as either plain text or HTML.
/// </summary>
internal sealed class OutputWriter : IDisposable
{
    private const ConsoleColor highlightColor = ConsoleColor.Yellow;
    private readonly ConsoleColor regularForeGround;
    private readonly bool outputHtml, hasOutputPath, writeOutputFile;
    private readonly string fileOutputPath;
    private readonly OutputCommand command;
    private readonly IDocument document;
    private readonly IElement output;
    private readonly StringWriter textOut;

    public bool WroteResults { get; private set; }

    internal OutputWriter(OutputCommand command)
    {
        regularForeGround = Console.ForegroundColor; //using current
        this.outputHtml = command.OutputHtml;
        this.fileOutputPath = command.FileOutputPath?.Trim('"');
        this.command = command;
        hasOutputPath = !string.IsNullOrEmpty(fileOutputPath);
        writeOutputFile = outputHtml || hasOutputPath;

        if (outputHtml) //prepare empty document
        {
            var context = BrowsingContext.New(Configuration.Default);
            document = context.OpenNewAsync().Result;

            var style = document.CreateElement("style");
            style.TextContent = "pre > em { background-color: yellow }";
            document.Head.Append(style);

            output = document.CreateElement("pre");
            document.Body.Append(output);
        }
        else textOut = new StringWriter();
    }

    internal void WriteHeader(string originalCommand)
    {
        //write original search into file header for reference and repeating
        if (writeOutputFile) WriteLine(originalCommand);

        //provide link(s) to the searched playlist or videos for debugging IDs
        Write(command.Describe() + " ");

        foreach (var url in command.Scope.ValidUrls)
        {
            WriteUrl(url);
            Write(" ");
        }

        WriteLine();
        WriteLine();
    }

    private void Write(string text)
    {
        Console.Write(text);
        if (!writeOutputFile) return;

        if (outputHtml) output.InnerHtml += text;
        else textOut.Write(text);
    }

    private void WriteLine(string text = null)
    {
        if (text != null) Write(text);
        Console.WriteLine();
        if (!writeOutputFile) return;

        if (outputHtml) output.InnerHtml += Environment.NewLine;
        else textOut.WriteLine();
    }

    private void WriteHighlighted(string text)
    {
        Console.ForegroundColor = highlightColor;
        Console.Write(text);
        ResetConsoleColor();
        if (!writeOutputFile) return;

        if (outputHtml)
        {
            var em = document.CreateElement("em");
            em.TextContent = text;
            output.Append(em);
        }
        else textOut.Write($"*{text}*");
    }

    private void WriteUrl(string url)
    {
        Console.Write(url);
        if (!writeOutputFile) return;

        if (outputHtml)
        {
            var hlink = document.CreateElement("a");
            hlink.SetAttribute("href", url);
            hlink.SetAttribute("target", "_blank");
            hlink.TextContent = url;
            output.Append(hlink);
        }
        else textOut.Write(url);
    }

    private void WriteHighlightingMatches(PaddedMatch paddedMatch, IndentedText indent = null)
    {
        var charsWritten = 0; // counts characters written
        var text = new string(paddedMatch.Value);

        if (indent != null)
        {
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
                    var startInUnwrappedText = paddedMatch.Value.IndexOf(line, startIndex: previousLineEnd);

                    lineInfos.Add((startInWrappedText - startInUnwrappedText,
                        startInUnwrappedText, startInUnwrappedText + line.Length));
                }
            }
            #endregion

            foreach (var match in paddedMatch.Included)
            {
                /*  shift match Length the number of chars inserted between
                    line containing Start and line containing end of match
                    and do so before modifying match Start */
                var end = match.Start + match.Length;
                var lineContainingMatchEnd = lineInfos.LastOrDefault(x => x.firstIndex <= end);

                // shift match Start the number of characters inserted in wrapped text before line
                var lineContainingMatchStart = lineInfos.LastOrDefault(x => x.firstIndex <= match.Start);
                if (lineContainingMatchStart != default) match.Start += lineContainingMatchStart.charOffset;

                if (lineContainingMatchEnd != lineContainingMatchStart)
                    match.Length += lineContainingMatchEnd.charOffset - lineContainingMatchStart.charOffset;
            }
        }

        void writeCounting(int length, bool highlight = false)
        {
            var phrase = text.Substring(charsWritten, length);

            if (highlight) WriteHighlighted(phrase);
            else Write(phrase);

            charsWritten += length;
        }

        foreach (var match in paddedMatch.Included.OrderBy(m => m.Start))
        {
            if (charsWritten < match.Start) writeCounting(match.Start - charsWritten); // write text preceding match

            // matched characters are already written; this may happen if included matches overlap each other
            if (match.Start < charsWritten && match.Start <= charsWritten - match.Length) continue;

            writeCounting(match.Length - (charsWritten - match.Start), true); // write (remaining) matched characters
        }

        if (charsWritten < text.Length) writeCounting(text.Length - charsWritten); // write text trailing last included match
    }

    internal void DisplayVideoResult(VideoSearchResult result)
    {
        var videoUrl = Youtube.GetVideoUrl(result.Video.Id);

        if (result.TitleMatches != null)
            using (var indent = new IndentedText())
                WriteHighlightingMatches(result.TitleMatches, indent);
        else Write(result.Video.Title);

        WriteLine();
        Write($"{result.Video.Uploaded:g} ");
        WriteUrl(videoUrl);
        WriteLine();

        if (result.DescriptionMatches.HasAny())
        {
            Write("  in description: ");

            for (int i = 0; i < result.DescriptionMatches.Length; i++)
            {
                if (i > 0) Write("    ");

                using (var indent = new IndentedText())
                    WriteHighlightingMatches(result.DescriptionMatches[i], indent);

                WriteLine();
            }
        }

        if (result.KeywordMatches.HasAny())
        {
            Write("  in keywords: ");
            var lastKeyword = result.KeywordMatches.Last();

            foreach (var match in result.KeywordMatches)
            {
                WriteHighlightingMatches(match);

                if (match != lastKeyword) Write(", ");
                else WriteLine();
            }
        }

        foreach (var trackResult in result.MatchingCaptionTracks)
        {
            WriteLine("  " + trackResult.Track.LanguageName);
            var displaysHour = trackResult.Matches.Any(c => c.Item2.At > 3600);

            foreach (var (match, caption) in trackResult.Matches)
            {
                var offset = TimeSpan.FromSeconds(caption.At).FormatWithOptionalHours().PadLeft(displaysHour ? 7 : 5);
                Write($"    {offset} ");

                using var indent = new IndentedText();
                WriteHighlightingMatches(match, indent);

                const string padding = "    ";
                var url = $"{videoUrl}?t={caption.At}";

                if (indent.FitsCurrentLine(padding.Length + url.Length)) Write(padding);
                else indent.StartNewLine(this);

                WriteUrl(url);
                WriteLine();
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

    /// <summary>Displays the <paramref name="keywords"/> on the <see cref="Console"/>,
    /// most often occurring keyword first.</summary>
    /// <param name="keywords">The keywords and their corresponding number of occurrences.</param>
    internal void ListKeywords(Dictionary<string, ushort> keywords)
    {
        const string separator = " | ";
        var width = Console.WindowWidth;
        var line = string.Empty;

        /*  prevent breaking line mid-keyword on Console and breaks output into multiple lines for file
            without adding unnecessary separators at the start or end of lines */
        foreach (var tag in keywords.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key))
        {
            var keyword = tag.Value + "x " + tag.Key;

            // does keyword still fit into the current line?
            if ((line.Length + separator.Length + keyword.Length) < width)
                line += separator + keyword; // add it
            else // keyword doesn't fit
            {
                if (line.Length > 0) WriteLine(line); // write current line
                line = keyword; // and start a new one
            }
        }

        WroteResults = true;
    }

    internal async ValueTask<string> WriteOutputFile(Func<string> getDefaultStorageFolder)
    {
        if (!writeOutputFile) return null;

        string path;

        if (!hasOutputPath || fileOutputPath.IsDirectoryPath())
        {
            var extension = outputHtml ? ".html" : ".txt";
            var fileName = command.Describe().ToFileSafe() + extension;
            var folder = hasOutputPath ? fileOutputPath : getDefaultStorageFolder();
            path = Path.Combine(folder, fileName);
        }
        else path = fileOutputPath; // treat as full file path

        await WriteTextToFileAsync(outputHtml ? document.DocumentElement.OuterHtml : textOut.ToString(), path);
        return path;
    }

    private void ResetConsoleColor() => Console.ForegroundColor = regularForeGround;

    internal static Task WriteTextToFileAsync(string text, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return File.WriteAllTextAsync(path, text);
    }

    #region IDisposable implementation
    private bool disposedValue;

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // dispose managed state (managed objects)
                textOut?.Dispose();
                document?.Dispose();
                ResetConsoleColor(); //just to make sure we revert changes to the global Console state if an error occurs while writing sth. highlighted
            }

            // free unmanaged resources (unmanaged objects) and override finalizer
            // set large fields to null
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion

    /// <summary>A helper for writing multiple lines of text at the same indent
    /// in the <see cref="Console"/>. This is not quite block format, but almost.</summary>
    internal sealed class IndentedText : IDisposable
    {
        private readonly int left, width;

        /// <summary>
        /// Creates a new indented text block remembering the <see cref="Console.CursorLeft"/> position
        /// and the <see cref="Console.WindowWidth"/> available for output.
        /// </summary>
        internal IndentedText()
        {
            left = Console.CursorLeft;
            width = Console.WindowWidth - 1; // allowing for a buffer to avoid irregular text wrapping
        }

        /// <summary>Wraps <paramref name="text"/> into multiple lines
        /// indented by the remembered <see cref="Console.CursorLeft"/>
        /// fitting the remembered <see cref="Console.WindowWidth"/>.</summary>
        internal string Wrap(string text) => text.Wrap(width - left).Indent(left).Join(Environment.NewLine).TrimStart();

        /// <summary>Indicates whether the number of <paramref name="characters"/> fit the current line.</summary>
        internal bool FitsCurrentLine(int characters) => characters <= width - Console.CursorLeft;

        /// <summary>Starts a new indented line using the supplied <paramref name="outputWriter"/>.</summary>
        internal void StartNewLine(OutputWriter outputWriter)
        {
            outputWriter.WriteLine(); // start a new one
            outputWriter.Write(string.Empty.PadLeft(left)); //and output the correct padding
        }

        public void Dispose() { } // implementing IDisposable just to enable usage with using() block
    }
}