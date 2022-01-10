using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;

namespace SubTubular
{
    /// <summary>
    /// Provides formatted and highlighted output for Console
    /// as well as either plain text or HTML.
    /// </summary>
    internal sealed class OutputWriter : IDisposable
    {
        private const ConsoleColor highlightColor = ConsoleColor.Yellow;
        private readonly ConsoleColor regularForeGround;
        private readonly bool outputHtml, hasOutputPath, writeOutputFile;
        private readonly string fileOutputPath, fileNameWithoutExtension;
        private readonly IEnumerable<string> terms;
        private readonly IDocument document;
        private readonly IElement output;
        private readonly StringWriter textOut;

        internal OutputWriter(string originalCommand, SearchCommand command)
        {
            regularForeGround = Console.ForegroundColor; //using current
            this.outputHtml = command.OutputHtml;
            this.fileOutputPath = command.FileOutputPath;
            this.fileNameWithoutExtension = command.Format();
            this.terms = command.Terms ?? Enumerable.Empty<string>();
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

            //write original search into file header for reference and repeating
            if (writeOutputFile) WriteLine(originalCommand);

            //provide link(s) to the searched playlist or videos for debugging IDs
            Write("searching " + command.Label);

            foreach (var url in command.GetUrls())
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

        internal void WriteLine(string text = null)
        {
            if (text != null) Write(text);
            Console.WriteLine();
            if (!writeOutputFile) return;

            if (outputHtml)
                output.InnerHtml += Environment.NewLine;
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

        private void WriteHighlightingMatches(string text, IndentedText indent = null)
        {
            var written = 0;
            if (indent != null) text = indent.BreakLines(text).Join(Environment.NewLine);

            void writeCounting(int length, bool highlight = false)
            {
                var phrase = text.Substring(written, length);

                if (highlight) WriteHighlighted(phrase);
                else Write(phrase);

                written += length;
            };

            // match multi-word phrases across line breaks because we've inserted indent and line breaks above
            var matches = text.GetMatches(terms, termRegex => termRegex.Replace("\\ ", "\\s+")).ToArray();

            foreach (var match in matches)
            {
                if (written < match.Index) writeCounting(match.Index - written); //write text preceding match
                if (match.Index < written && match.Index <= written - match.Length) continue; //letters already matched
                writeCounting(match.Length - (written - match.Index), true); //write remaining matched letters
            }

            if (written < text.Length) writeCounting(text.Length - written); //write text trailing last match
        }

        internal void DisplayVideoResult(VideoSearchResult result)
        {
            var videoUrl = SearchVideos.GetVideoUrl(result.Video.Id);

            if (result.TitleMatches)
                using (var indent = new IndentedText())
                    WriteHighlightingMatches(result.Video.Title, indent);
            else Write(result.Video.Title);

            WriteLine();
            Write($"{result.Video.Uploaded:g} ");
            WriteUrl(videoUrl);
            WriteLine();

            if (result.DescriptionMatches.Any())
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

            if (result.MatchingKeywords.Any())
            {
                Write("  in keywords: ");
                var lastKeyword = result.MatchingKeywords.Last();

                foreach (var keyword in result.MatchingKeywords)
                {
                    WriteHighlightingMatches(keyword);

                    if (keyword != lastKeyword) Write(", ");
                    else WriteLine();
                }
            }

            foreach (var track in result.MatchingCaptionTracks)
            {
                WriteLine("  " + track.LanguageName);

                var displaysHour = track.Captions.Any(c => c.At > 3600);

                foreach (var caption in track.Captions)
                {
                    var offset = TimeSpan.FromSeconds(caption.At).FormatWithOptionalHours().PadLeft(displaysHour ? 7 : 5);
                    Write($"    {offset} ");

                    using (var indent = new IndentedText())
                    {
                        WriteHighlightingMatches(caption.Text, indent);

                        const string padding = "    ";
                        var url = $"{videoUrl}?t={caption.At}";

                        if (indent.FitsCurrentLine(padding.Length + url.Length)) Write(padding);
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
        }

        internal async ValueTask<string> WriteOutputFile(Func<string> getDefaultStorageFolder)
        {
            if (!writeOutputFile) return null;

            string path;

            if (!hasOutputPath || fileOutputPath.IsDirectoryPath())
            {
                var extension = outputHtml ? ".html" : ".txt";
                var fileName = fileNameWithoutExtension.ToFileSafe() + extension;
                var folder = hasOutputPath ? fileOutputPath : getDefaultStorageFolder();
                path = Path.Combine(folder, fileName);
            }
            else path = fileOutputPath; // treat as full file path

            await WriteTextToFileAsync(outputHtml ? document.DocumentElement.OuterHtml : textOut.ToString(), path);
            return path;
        }

        private void ResetConsoleColor() => Console.ForegroundColor = regularForeGround;

        internal static async ValueTask WriteTextToFileAsync(string text, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await File.WriteAllTextAsync(path, text);
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
                width = Console.WindowWidth - 6; // allowing for a buffer to avoid irregular text wrapping
            }

            /// <summary>Wraps <paramref name="text"/> into multiple lines
            /// indented by the remembered <see cref="Console.CursorLeft"/>
            /// fitting the remembered <see cref="Console.WindowWidth"/>.</summary>
            internal string[] BreakLines(string text)
            {
                var chunks = text.Chunk(width - left, preserveWords: true).ToArray();

                return chunks.Select((line, index) =>
                {
                    if (index > 0) line = GetLeftPadded(line); // left-pad every but the first line
                    return line;
                }).ToArray();
            }

            private string GetLeftPadded(string line) => line.PadLeft(left + line.Length);

            /// <summary>Indicates whether the number of <paramref name="characters"/> fit the current line.</summary>
            internal bool FitsCurrentLine(int characters) => characters <= width - Console.CursorLeft;

            /// <summary>Starts a new indented line using the supplied <paramref name="outputWriter"/>.</summary>
            internal void StartNewLine(OutputWriter outputWriter)
            {
                outputWriter.WriteLine(); // start a new one
                outputWriter.Write(GetLeftPadded(string.Empty)); //and output the correct padding
            }

            public void Dispose() { } // implementing IDisposable just to enable usage with using() block
        }
    }
}