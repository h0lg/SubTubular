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
            : this(originalCommand, command.OutputHtml, command.FileOutputPath, command.Format(), command.Terms) { }

        internal OutputWriter(string originalCommand, bool outputHtml, string fileOutputPath,
            string fileNameWithoutExtension, IEnumerable<string> terms = null)
        {
            regularForeGround = Console.ForegroundColor; //using current
            this.outputHtml = outputHtml;
            this.fileOutputPath = fileOutputPath;
            this.fileNameWithoutExtension = fileNameWithoutExtension;
            this.terms = terms ?? Enumerable.Empty<string>();
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

            if (writeOutputFile) //write original search into file header
            {
                WriteLine(originalCommand);
                WriteLine();
            }
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
            Console.WriteLine(text);
            if (!writeOutputFile) return;

            if (outputHtml)
                output.InnerHtml += (text ?? string.Empty) + Environment.NewLine;
            else textOut.WriteLine(text);
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

        private void WriteHighlightingMatches(string text)
        {
            var written = 0;

            void writeCounting(int length, bool highlight = false)
            {
                var phrase = text.Substring(written, length);

                if (highlight) WriteHighlighted(phrase);
                else Write(phrase);

                written += length;
            };

            var matches = text.GetMatches(terms).ToArray();

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
            var videoUrl = "https://youtu.be/" + result.Video.Id;

            Write($"{result.Video.Uploaded:g} ");
            WriteUrl(videoUrl);
            WriteLine();

            if (result.TitleMatches)
            {
                Write("  in title: ");
                WriteHighlightingMatches(result.Video.Title);
                WriteLine();
            }

            if (result.MatchingDescriptionLines.Any())
            {
                Write("  in description: ");

                for (int i = 0; i < result.MatchingDescriptionLines.Length; i++)
                {
                    var line = result.MatchingDescriptionLines[i];
                    var prefix = i == 0 ? string.Empty : "    ";
                    WriteHighlightingMatches(prefix + line);
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
                    WriteHighlightingMatches(caption.Text);
                    Write("    ");
                    WriteUrl($"{videoUrl}?t={caption.At}");
                    WriteLine();
                }
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

            var text = outputHtml ? document.DocumentElement.OuterHtml : textOut.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await File.WriteAllTextAsync(path, text);
            return path;
        }

        private void ResetConsoleColor() => Console.ForegroundColor = regularForeGround;

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
    }
}