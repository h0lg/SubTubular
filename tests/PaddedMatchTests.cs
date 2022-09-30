#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace SubTubular.Tests
{
    internal static class PaddedMatchTests
    {
        const string singleLine = "A helper comparable to Match including one or multiple PaddedMatch.Included matches";

        const string multiLineText = @"A helper comparable to Match including one or multiple PaddedMatch.Included matches
padded with a number of characters on each end for context.
Range<T>.Start and Range<T>.End represent the indexes of the padded match in the full text it was matched in.";

        internal static void Run()
        {
            Ranges();

            const string singleMatched = "comparable to Match";

            OneInSingleLine(search: singleMatched, padding: 0, expect: singleMatched);
            OneInSingleLine(search: singleMatched, padding: 5, expect: "lper comparable to Match incl");

            const string multiMatched = "match";

            MultipleInMultiLine(multiMatched, padding: 0, new[] {
                new ExpectedMatch(23, 27, "Match"),
                new ExpectedMatch(61, 65, "Match"),
                new ExpectedMatch(76, 80, "match"),
                new ExpectedMatch(212, 216, "match"),
                new ExpectedMatch(242, 246, "match")
            });

            MultipleInMultiLine(multiMatched, padding: 6, new[] {
                new ExpectedMatch(17, 33, "le to Match inclu"),
                new ExpectedMatch(55, 86, @"PaddedMatch.Included matches
pad"),
                new ExpectedMatch(206, 222, "added match in th"),
                new ExpectedMatch(236, multiLineText.Length - 1, "t was matched in.")
            });

            MultipleInMultiLine(multiMatched, padding: 13, new[] {
                new ExpectedMatch(10, 40, "omparable to Match including on"),
                new ExpectedMatch(48, 93, @"ltiple PaddedMatch.Included matches
padded wit"),
                new ExpectedMatch(199, multiLineText.Length - 1, "f the padded match in the full text it was matched in.")
            });

            MultipleInMultiLine(multiMatched, padding: 17, new[] {
                new ExpectedMatch(6, 97, @"er comparable to Match including one or multiple PaddedMatch.Included matches
padded with a "),
                new ExpectedMatch(195, multiLineText.Length - 1, "es of the padded match in the full text it was matched in.")
            });
        }

        private static void Ranges()
        {
            var range = new ExpectedMatch(08, 15);
            var hash = range.GetHashCode();

            Debug.Assert(hash == new ExpectedMatch(15, 8).GetHashCode(), "hash codes should match");
            Debug.Assert(hash != new ExpectedMatch(9, 16).GetHashCode(), "hash codes shouldn't match");
            Debug.Assert(hash != new ExpectedMatch(9, 14).GetHashCode(), "hash codes shouldn't match");

            Debug.Assert(!range.Intersects(new ExpectedMatch(0, 7)), "ranges shouldn't intersect");
            Debug.Assert(range.Intersects(new ExpectedMatch(0, 8)), "ranges should intersect");
            Debug.Assert(range.Intersects(new ExpectedMatch(8, 15)), "ranges should intersect");
            Debug.Assert(range.Intersects(new ExpectedMatch(15, 20)), "ranges should intersect");
            Debug.Assert(!range.Intersects(new ExpectedMatch(16, 20)), "ranges shouldn't intersect");

            new[] { range, new ExpectedMatch(0, 7) }.GroupOverlapping()
                .ShouldYield(new[] { new[] { range }, new[] { new ExpectedMatch(0, 7) } });

            new[] { range, new ExpectedMatch(0, 8) }.GroupOverlapping()
                .ShouldYield(new[] { new[] { range, new ExpectedMatch(0, 8) } });

            new[] { range, new ExpectedMatch(0, 8), new ExpectedMatch(15, 20) }.GroupOverlapping()
                .ShouldYield(new[] { new[] { range, new ExpectedMatch(0, 8), new ExpectedMatch(15, 20) } });

            new[] { range, new ExpectedMatch(0, 7), new ExpectedMatch(16, 20) }.GroupOverlapping()
                .ShouldYield(new[] { new[] { range }, new[] { new ExpectedMatch(0, 7) }, new[] { new ExpectedMatch(16, 20) } });
        }

        private static void OneInSingleLine(string search, byte padding, string expect)
        {
            var match = Regex.Match(singleLine, search);
            var paddedMatch = new PaddedMatch(match, padding, singleLine);

            Debug.Assert(paddedMatch.Value == expect, "unexpected Value");
            Debug.Assert(paddedMatch.Start == match.Index - padding, "unexpected Start");
            // substract 1 because start is included in length
            Debug.Assert(paddedMatch.End == match.Index + search.Length - 1 + padding, "unexpected End");
        }

        private static void Compare(ExpectedMatch expected, PaddedMatch actual)
        {
            Debug.Assert(actual.Value == expected.Value, "unexpected Value");
            Debug.Assert(actual.Start == expected.Start, "unexpected Start");
            Debug.Assert(actual.End == expected.End, "unexpected End");
        }

        private static void MultipleInMultiLine(string searched, byte padding, ExpectedMatch[] expected)
        {
            var matches = Regex.Matches(multiLineText, searched, RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var actual = matches.Select(match => new PaddedMatch(match, padding, multiLineText))
                .MergeOverlapping(multiLineText)
                .ToArray();

            Debug.Assert(actual.Length == expected.Length, "unexpected number of matches");

            for (var i = 0; i < expected.Length; i++)
                Compare(expected[i], actual[i]);
        }

        private sealed class ExpectedMatch : Range<int>
        {
            /// <summary>The text containing the <see cref="Included"/> matches including padding.</summary>
            internal string Value { get; set; }

            internal ExpectedMatch(int start, int end, string value = null)
                : base(start, end, endIncluded: true) => Value = value;
        }
    }

    internal static class RangeTestExtensions
    {
        internal static void ShouldYield<T>(this IEnumerable<IEnumerable<T>> actual, T[][] expected) where T : Range<int>
        {
            Debug.Assert(actual.Count() == expected.Length, "unexpected group number");

            for (int i = 0; i < expected.Length; i++)
            {
                var actualGroup = actual.ElementAt(i);
                var expectedGroup = expected[i];

                Debug.Assert(actualGroup.Count() == expectedGroup.Length, "unexpected group length");

                for (int j = 0; j < expectedGroup.Length; j++)
                {
                    var actualRange = actualGroup.ElementAt(j);
                    var expectedRange = expectedGroup[j];
                    Debug.Assert(actualRange.Start == expectedRange.Start, "unexpected Start");
                    Debug.Assert(actualRange.End == expectedRange.End, "unexpected End");
                }
            }
        }
    }
}
#endif