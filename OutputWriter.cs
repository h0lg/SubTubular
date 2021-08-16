using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AngleSharp;
using AngleSharp.Dom;

namespace SubTubular
{
    internal sealed class OutputWriter
    {
        private const ConsoleColor highlightColor = ConsoleColor.Yellow;
        private readonly ConsoleColor regularForeGround;
        private readonly bool hasOutputPath, writeOutputFile;
        private readonly SearchCommand command;
        private readonly IEnumerable<string> terms;
        private readonly IDocument document;
        private readonly IElement output;
        private readonly StringWriter textOut;

        internal OutputWriter(SearchCommand command, string originalCommand)
        {
            regularForeGround = Console.ForegroundColor; //using current
            hasOutputPath = !string.IsNullOrEmpty(command.FileOutputPath);
            writeOutputFile = command.OutputHtml || hasOutputPath;
            this.command = command;
            terms = command.Terms;

            if (command.OutputHtml)
            {
                var context = BrowsingContext.New(Configuration.Default);
                document = context.OpenNewAsync().Result;

                var style = document.CreateElement("style");
                style.TextContent = "pre > em { background-color: yellow }";
                document.Head.Append(style);

                output = document.CreateElement("pre");
                document.Body.Append(output);
            }
            else
            {
                textOut = new StringWriter();
            }

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

            if (command.OutputHtml) output.InnerHtml += text;
            else textOut.Write(text);
        }

        private void WriteLine(string text = null)
        {
            Console.WriteLine(text);
            if (!writeOutputFile) return;

            if (command.OutputHtml)
                output.InnerHtml += (text ?? string.Empty) + Environment.NewLine;
            else textOut.WriteLine(text);
        }

        private void WriteHighlighted(string text)
        {
            Console.ForegroundColor = highlightColor;
            Console.Write(text);
            Console.ForegroundColor = regularForeGround;
            if (!writeOutputFile) return;

            if (command.OutputHtml)
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

            if (command.OutputHtml)
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

        internal async void WriteOutputFile(Func<string> getDefaultStorageFolder)
        {
            if (!writeOutputFile) return;

            string path;

            if (!hasOutputPath || command.FileOutputPath.IsPathDirectory())
            {
                var extension = command.OutputHtml ? ".html" : ".txt";
                var fileName = command.Format() + extension;
                var folder = hasOutputPath ? command.FileOutputPath : getDefaultStorageFolder();
                path = Path.Combine(folder, fileName);
            }
            else path = command.FileOutputPath; // treat as full file path

            var text = command.OutputHtml ? document.DocumentElement.OuterHtml : textOut.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await File.WriteAllTextAsync(path, text);
            Console.WriteLine("Search results were written to " + path);
        }
    }
}