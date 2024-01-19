namespace Tests;

[TestClass]
public class SplitIntoPaddedGroupsTests
{
    [TestMethod]
    public void NoMatches_ReturnsNothing()
    {
        // Arrange
        MatchedText noMatches = new("This is a test without matches.");

        // Act
        var result = noMatches.SplitIntoPaddedGroups(5).ToList();

        // Assert
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void SingleMatch_ReturnsOriginalMatches()
    {
        // Arrange
        MatchedText singleMatch = new("This is a test with a match.", new MatchedText.Match(start: 10, length: 4));

        // Act
        var result = singleMatch.SplitIntoPaddedGroups(5).ToList();

        // Assert
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(singleMatch.Text, result[0].Text);
        CollectionAssert.AreEqual(singleMatch.Matches, result[0].Matches);
        Assert.AreEqual(singleMatch, result[0]);
    }

    [TestMethod]
    public void TwoMatchesWithOverlap_ReturnsOriginalMatches()
    {
        // Arrange
        MatchedText multipleMatches = new("This is a test with overlapping matches.",
            new MatchedText.Match(start: 5, length: 2),
            new MatchedText.Match(start: 10, length: 4));

        // Act
        var result = multipleMatches.SplitIntoPaddedGroups(5).ToList();

        // Assert
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(multipleMatches.Text, result[0].Text);
        CollectionAssert.AreEqual(multipleMatches.Matches, result[0].Matches);
    }

    [TestMethod]
    public void MatchesAtBeginningAndEnd_ReturnsTwoGroups()
    {
        // Arrange
        MatchedText matchesAtBeginningAndEnd = new("Matches at the beginning and end.",
            new MatchedText.Match(start: 0, length: 7),
            new MatchedText.Match(start: 29, length: 4));

        // Act
        var result = matchesAtBeginningAndEnd.SplitIntoPaddedGroups(5).ToList();

        // Assert
        Assert.AreEqual(2, result.Count);
        CollectionAssert.AreEqual(new[] { new MatchedText.Match(start: 0, length: 7) }, result[0].Matches);
        CollectionAssert.AreEqual(new[] { new MatchedText.Match(start: 29, length: 4) }, result[1].Matches);
    }
}

[TestClass]
public class WriteHighlightingMatchesTests
{
    [TestMethod]
    public void NoMatches_NoOutput()
        => Test(new MatchedText("This is a test without matches."),
            shouldOutput: string.Empty, padding: 5);

    [TestMethod]
    public void Single_Match()
        => Test(new MatchedText("This is a test with a match.",
                new MatchedText.Match(start: 5, length: 2)),
            shouldOutput: "This *is* a te", padding: 5);

    [TestMethod]
    public void Multiple_Matches_With_Overlap()
        => Test(new MatchedText("This is a test with overlapping matches.",
                new MatchedText.Match(start: 5, length: 2),
                new MatchedText.Match(start: 10, length: 4)),
            shouldOutput: "This *is* a *test* with", padding: 5);

    [TestMethod]
    public void Matches_At_Beginning_And_End()
        => Test(new MatchedText("Matches at the beginning and end.",
                new MatchedText.Match(start: 0, length: 7),
                new MatchedText.Match(start: 29, length: 4)),
            shouldOutput: "*Matches* at the beginning and *end.*", padding: 5);

    [TestMethod]
    public void Null_Padding_Outputs_Full_Text()
        => Test(new MatchedText("This is a test with a match.",
                new MatchedText.Match(start: 5, length: 2)),
            shouldOutput: "This *is* a test with a match.");

    private void Test(MatchedText matches, string shouldOutput, uint? padding = null)
    {
        using StringWriter sw = new();
        matches.WriteHighlightingMatches(write: sw.Write, highlight: text => sw.Write($"*{text}*"), padding);
        var actualOutput = sw.ToString();
        Assert.AreEqual(shouldOutput, actualOutput);
    }
}
