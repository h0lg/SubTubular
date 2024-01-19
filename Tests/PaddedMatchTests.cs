using System.Text.RegularExpressions;

namespace Tests;

[TestClass]
public class PaddedMatchTests
{
    const string singleLine = "A helper comparable to Match including one or multiple PaddedMatch.Included matches";

    const string multiLineText = @"A helper comparable to Match including one or multiple PaddedMatch.Included matches
padded with a number of characters on each end for context.
Range<T>.Start and Range<T>.End represent the indexes of the padded match in the full text it was matched in.";

    [TestMethod]
    public void Run()
    {
        Ranges();

        const string singleMatched = "comparable to Match";

        OneInSingleLine(search: singleMatched, padding: 0, expect: singleMatched);
        OneInSingleLine(search: singleMatched, padding: 5, expect: "lper comparable to Match incl");

        const string multiMatched = "match";

        MultipleInSingleLine(multiMatched, new ExpectedMatch(0, singleLine.Length - 1, singleLine,
            multiMatched.Length, 23, 61, 76));

        MultipleInMultiLine(multiMatched, padding: 0, new[] {
            new ExpectedMatch(23, 27, "Match", multiMatched.Length, 0),
            new ExpectedMatch(61, 65, "Match", multiMatched.Length, 0),
            new ExpectedMatch(76, 80, "match", multiMatched.Length, 0),
            new ExpectedMatch(214, 218, "match", multiMatched.Length, 0),
            new ExpectedMatch(244, 248, "match", multiMatched.Length, 0)
        });

        MultipleInMultiLine(multiMatched, padding: 5, new[] {
            new ExpectedMatch(18, 32, "e to Match incl", multiMatched.Length, 5),
            new ExpectedMatch(56, 85, @"addedMatch.Included matches
p", multiMatched.Length, 5, 20),
            new ExpectedMatch(209, 223, "dded match in t", multiMatched.Length, 5),
            new ExpectedMatch(239, 253, " was matched in", multiMatched.Length, 5)
        });

        MultipleInMultiLine(multiMatched, padding: 13, new[] {
            new ExpectedMatch(10, 40, "omparable to Match including on", multiMatched.Length, 13),
            new ExpectedMatch(48, 93, @"ltiple PaddedMatch.Included matches
padded wi", multiMatched.Length, 13, 28),
            new ExpectedMatch(201, multiLineText.Length - 1, "f the padded match in the full text it was matched in.",
                multiMatched.Length, 13, 43)
        });

        MultipleInMultiLine(multiMatched, padding: 17, new[] {
            new ExpectedMatch(6, 97, @"er comparable to Match including one or multiple PaddedMatch.Included matches
padded with a", multiMatched.Length, 17, 55, 70),
            new ExpectedMatch(197, multiLineText.Length - 1, "es of the padded match in the full text it was matched in.",
                multiMatched.Length, 17, 47)
        });
    }

    private static void Ranges()
    {
        var range = new ExpectedMatch(08, 15);
        var hash = range.GetHashCode();

        Assert.AreEqual(new ExpectedMatch(15, 8).GetHashCode(), hash, "hash codes should match");
        Assert.AreNotEqual(new ExpectedMatch(9, 16).GetHashCode(), hash, "hash codes shouldn't match");
        Assert.AreNotEqual(new ExpectedMatch(9, 14).GetHashCode(), hash, "hash codes shouldn't match");

        Assert.IsFalse(range.Intersects(new ExpectedMatch(0, 6), orTouches: true), "ranges shouldn't touch");
        Assert.IsTrue(range.Intersects(new ExpectedMatch(0, 7), orTouches: true), "ranges should touch");
        Assert.IsFalse(range.Intersects(new ExpectedMatch(0, 7)), "ranges shouldn't intersect");
        Assert.IsTrue(range.Intersects(new ExpectedMatch(0, 8)), "ranges should intersect");
        Assert.IsTrue(range.Intersects(new ExpectedMatch(8, 15)), "ranges should intersect");
        Assert.IsTrue(range.Intersects(new ExpectedMatch(15, 20)), "ranges should intersect");
        Assert.IsFalse(range.Intersects(new ExpectedMatch(16, 20)), "ranges shouldn't intersect");
        Assert.IsTrue(range.Intersects(new ExpectedMatch(16, 20), orTouches: true), "ranges should touch");
        Assert.IsFalse(range.Intersects(new ExpectedMatch(17, 20), orTouches: true), "ranges shouldn't touch");

        new[] { range, new ExpectedMatch(0, 6) }.GroupOverlapping(orTouching: true)
            .ShouldYield(new[] { new[] { range }, new[] { new ExpectedMatch(0, 6) } });

        new[] { range, new ExpectedMatch(0, 7) }.GroupOverlapping(orTouching: true)
            .ShouldYield(new[] { new[] { range, new ExpectedMatch(0, 7) } });

        new[] { range, new ExpectedMatch(0, 7), new ExpectedMatch(16, 20) }.GroupOverlapping(orTouching: true)
            .ShouldYield(new[] { new[] { range, new ExpectedMatch(0, 7), new ExpectedMatch(16, 20) } });

        new[] { range, new ExpectedMatch(0, 6), new ExpectedMatch(17, 20) }.GroupOverlapping(orTouching: true)
            .ShouldYield(new[] { new[] { range }, new[] { new ExpectedMatch(0, 6) }, new[] { new ExpectedMatch(17, 20) } });
    }

    private static void OneInSingleLine(string search, byte padding, string expect)
    {
        var match = Regex.Match(singleLine, search);
        var paddedMatch = new PaddedMatch(match.Index, match.Length, padding, singleLine);

        Assert.AreEqual(expect, paddedMatch.Value, "unexpected Value");
        Assert.AreEqual(match.Index - padding, paddedMatch.Start, "unexpected Start");

        // substract 1 because start is included in length
        Assert.AreEqual(match.Index + search.Length - 1 + padding, paddedMatch.End, "unexpected End");

        Assert.AreEqual(1, paddedMatch.Included.Length, "unexpected Included.Length");
        Assert.AreEqual(padding, paddedMatch.Included[0].Start, "unexpected Included.Start");
        Assert.AreEqual(search.Length, paddedMatch.Included[0].Length, "unexpected Included.Length");
    }

    private static void MultipleInSingleLine(string searched, ExpectedMatch expected)
    {
        var matches = Regex.Matches(singleLine, searched, RegexOptions.IgnoreCase);

        var actual = new PaddedMatch(singleLine, matches
            .Select(match => new PaddedMatch.IncludedMatch { Start = match.Index, Length = match.Length })
            .ToArray());

        Compare(expected, actual);
    }

    private static void Compare(ExpectedMatch expected, PaddedMatch actual)
    {
        Assert.AreEqual(expected.Value, actual.Value, "unexpected Value");
        Assert.AreEqual(expected.Start, actual.Start, "unexpected Start");
        Assert.AreEqual(expected.End, actual.End, "unexpected End");
        Assert.AreEqual(expected.Included.Length, actual.Included.Length, "unexpected Included.Length");

        for (var i = 0; i < expected.Included.Length; i++)
        {
            var actualIncluded = actual.Included[i];
            var expectedIncluded = expected.Included[i];
            Assert.AreEqual(expectedIncluded.Start, actualIncluded.Start, "unexpected Included.Start");
            Assert.AreEqual(expectedIncluded.Length, actualIncluded.Length, "unexpected Included.Length");
        }
    }

    private static void MultipleInMultiLine(string searched, byte padding, ExpectedMatch[] expected)
    {
        var matches = Regex.Matches(multiLineText, searched, RegexOptions.IgnoreCase | RegexOptions.Multiline);

        var actual = matches.Select(match => new PaddedMatch(match.Index, match.Length, padding, multiLineText))
            .MergeOverlapping(multiLineText)
            .ToArray();

        Assert.AreEqual(expected.Length, actual.Length, "unexpected number of matches");

        for (var i = 0; i < expected.Length; i++)
            Compare(expected[i], actual[i]);
    }

    private sealed class ExpectedMatch : Range<int>
    {
        /// <summary>The text containing the <see cref="Included"/> matches including padding.</summary>
        internal string Value { get; set; }

        /// <summary>Contains the internal match(es) with <see cref="PaddedMatch.IncludedMatch.Start"/> relative to <see cref="Value"/>.</summary>
        internal PaddedMatch.IncludedMatch[] Included { get; set; }

        internal ExpectedMatch(int start, int end,
            string value = "", int matchLength = 0, params int[] includedStarts)
            : base(start, end, endIncluded: true)
        {
            Value = value;
            Included = includedStarts.Select(start => new PaddedMatch.IncludedMatch { Start = start, Length = matchLength }).ToArray();
        }
    }
}

internal static class RangeTestExtensions
{
    internal static void ShouldYield<T>(this IEnumerable<IEnumerable<T>> actual, T[][] expected) where T : Range<int>
    {
        Assert.AreEqual(expected.Length, actual.Count(), "unexpected group number");

        for (int i = 0; i < expected.Length; i++)
        {
            var actualGroup = actual.ElementAt(i);
            var expectedGroup = expected[i];

            Assert.AreEqual(expectedGroup.Length, actualGroup.Count(), "unexpected group length");

            for (int j = 0; j < expectedGroup.Length; j++)
            {
                var actualRange = actualGroup.ElementAt(j);
                var expectedRange = expectedGroup[j];
                Assert.AreEqual(expectedRange.Start, actualRange.Start, "unexpected Start");
                Assert.AreEqual(expectedRange.End, actualRange.End, "unexpected End");
            }
        }
    }
}
