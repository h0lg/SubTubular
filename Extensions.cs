using System;
using System.Collections.Generic;
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

    internal static class StringExtensions
    {
        //inspired by https://stackoverflow.com/a/2642406
        internal static IOrderedEnumerable<Match> GetMatches(this string text, IEnumerable<string> terms,
            RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase) => terms
            .SelectMany(term => Regex.Matches(text, Regex.Escape(term), options))
            .OrderBy(match => match.Index);

        internal static bool ContainsAny(this string text, IEnumerable<string> terms,
            StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
            => terms.Any(t => text.Contains(t, stringComparison));

        internal static string Join(this IEnumerable<string> pieces, string glue) => string.Join(glue, pieces);

        /// <summary>
        /// Splits a string into an array by new line characters.
        /// Inspired by https://stackoverflow.com/a/1547483 
        /// </summary>
        internal static string[] SplitOnNewLines(this string text)
            => text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }
}