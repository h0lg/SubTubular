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
        //from https://stackoverflow.com/a/2642406
        internal static IEnumerable<int> IndexesOf(this string hayStack, string needle, RegexOptions options = RegexOptions.None)
            => Regex.Matches(hayStack, Regex.Escape(needle), options).Cast<Match>().Select(m => m.Index);

        internal static bool ContainsAny(this string text, IEnumerable<string> terms,
            StringComparison stringComparison = StringComparison.InvariantCultureIgnoreCase)
            => terms.Any(t => text.Contains(t, stringComparison));
    }
}