using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SubTubular
{
    internal static class TimeSpanExtensions
    {
        private const string minSec = "mm':'ss";

        //inspired by https://stackoverflow.com/a/4709641
        internal static string FormatWithOptionalHours(this TimeSpan timeSpan)
            => timeSpan.ToString(timeSpan.TotalHours >= 1 ? "h':'" + minSec : minSec);
    }

    /// <summary>Extension methods for <see cref="string"/>s.</summary>
    internal static class StringExtensions
    {
        /// <summary>Replaces all consecutive white space characters in
        /// <paramref name="input"/> with <paramref name="normalizeTo"/>.</summary>
        internal static string NormalizeWhiteSpace(this string input, string normalizeTo = " ")
            => System.Text.RegularExpressions.Regex.Replace(input, @"\s+", normalizeTo);

        /// <summary>Returns matches for all occurances of <paramref name="terms"/> in <paramref name="text"/>
        /// ordered by first occurance while applying <paramref name="options"/> to the search.
        /// Inspired by https://stackoverflow.com/a/2642406 .</summary>
        /// <param name="modifyTermRegex">Optional. Use this for changing the escaped regular expression
        /// for each term in <paramref name="terms"/> before it is matched against <paramref name="text"/>.</param>
        internal static IOrderedEnumerable<Match> GetMatches(this string text,
            IEnumerable<string> terms, Func<string, string> modifyTermRegex = null,
            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) => terms
            .SelectMany(term =>
            {
                var regex = Regex.Escape(term.NormalizeWhiteSpace());
                if (modifyTermRegex != null) regex = modifyTermRegex(regex);
                return Regex.Matches(text, regex, options);
            })
            .OrderBy(match => match.Index);

        /// <summary>Indicates whether <paramref name="text"/> contains any of the supplied
        /// <paramref name="terms"/> using <paramref name="stringComparison"/> to compare.</summary>
        internal static bool ContainsAny(this string text, IEnumerable<string> terms,
            StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
            => terms.Any(t => text.Contains(t, stringComparison));

        /// <summary>Concatenates the <paramref name="pieces"/> into a single string
        /// putting <paramref name="glue"/> in between them.</summary>
        internal static string Join(this IEnumerable<string> pieces, string glue) => string.Join(glue, pieces);

        /// <summary>Indicates whether <paramref name="path"/> points to a directory rather than a file.
        /// From https://stackoverflow.com/a/19596821 .</summary>
        internal static bool IsDirectoryPath(this string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            path = path.Trim();

            if (Directory.Exists(path)) return true;
            if (File.Exists(path)) return false;

            // neither file nor directory exists. guess intention

            // if has trailing slash then it's a directory
            if (new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }.Any(x => path.EndsWith(x)))
                return true;

            // if has extension then its a file; directory otherwise
            return string.IsNullOrWhiteSpace(Path.GetExtension(path));
        }

        /// <summary>Replaces all characters unsafe for file or directory names in <paramref name="value"/>
        /// with <paramref name="replacement"/>.</summary>
        internal static string ToFileSafe(this string value, string replacement = "_")
            => Regex.Replace(value, "[" + Regex.Escape(new string(Path.GetInvalidFileNameChars())) + "]", replacement);

        /// <summary>Splits <paramref name="text"/> into pieces of the maximum given <paramref name="chunkSize"/>
        /// respecting word boundaries if <paramref name="preserveWords"/> is true.</summary>
        internal static IEnumerable<string> Chunk(this string text, int chunkSize, bool preserveWords = false)
        {
            if (preserveWords)
            {
                // from https://stackoverflow.com/a/4398471
                var charCount = 0;

                return text.Split(' ', StringSplitOptions.RemoveEmptyEntries) // split into words
                    .GroupBy(word => (charCount += word.Length + 1) / chunkSize)
                    .Select(line => line.Join(" "));
            }

            // inspired by https://stackoverflow.com/a/1450797
            else return Enumerable.Range(0, Math.Max(text.Length / chunkSize, 1)).Select(i =>
            {
                var startIndex = i * chunkSize;
                return text.Substring(startIndex, Math.Min(chunkSize, text.Length - startIndex));
            });
        }
    }

    /// <summary>Extension methods for <see cref="IEnumerable{T}"/> types.</summary>
    internal static class EnumerableExtenions
    {
        /// <summary>Indicates whether <paramref name="collection"/>
        /// contains any of the supplied <paramref name="values"/>.</summary>
        internal static bool ContainsAny<T>(this IEnumerable<T> collection, IEnumerable<T> values)
            => values.Any(value => collection.Contains(value));
    }

    /// <summary>Extension methods for <see cref="IComparable"/> types.</summary>
    internal static class ComparableExtensions
    {
        /// <summary>Determines whether <paramref name="other"/> is greater than
        /// <paramref name="orEqualTo"/> the <paramref name="other"/>.</summary>
        internal static bool IsGreaterThan(this IComparable comparable, IComparable other, bool orEqualTo = false)
        {
            var position = comparable.CompareTo(other);
            return orEqualTo ? position >= 0 : position > 0;
        }

        /// <summary>Determines whether <paramref name="other"/> is less than
        /// <paramref name="orEqualTo"/> the <paramref name="other"/>.</summary>
        internal static bool IsLessThan(this IComparable comparable, IComparable other, bool orEqualTo = false)
        {
            var position = comparable.CompareTo(other);
            return orEqualTo ? position <= 0 : position < 0;
        }
    }
}