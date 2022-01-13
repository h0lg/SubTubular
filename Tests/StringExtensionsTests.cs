using System;
using System.Linq;

namespace SubTubular.Tests
{
    internal static class StringExtensionsTests
    {
        internal static void RunTests()
        {
            var matches = @"foo
            bar".NormalizeWhiteSpace().GetMatches(new[] { "foo bar" });

            if (!matches.Any()) throw new Exception("GetMatches() doesn't match across lines.");
        }
    }
}