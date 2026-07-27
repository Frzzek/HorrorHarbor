using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class BoundedEditDistanceTests
{
    [TestMethod]
    [DataRow("martyes", "martyrs", 1, 1)]
    [DataRow("gonjam", "gonjiam", 1, 1)]
    [DataRow("exorcst", "exorcist", 1, 1)]
    [DataRow("hose", "house", 1, 1)]
    [DataRow("abcdefgh", "abxdxfgh", 2, 2)]
    [DataRow("identical", "identical", 0, 0)]
    public void TryCalculate_ReturnsDistanceWithinBound(
        string source,
        string target,
        int maximumDistance,
        int expectedDistance)
    {
        bool matched = BoundedEditDistance.TryCalculate(
            source,
            target,
            maximumDistance,
            out int distance);

        Assert.IsTrue(matched);
        Assert.AreEqual(expectedDistance, distance);
    }

    [TestMethod]
    [DataRow("martyes", "martyrs", 0)]
    [DataRow("abcdefgh", "abxdxfgh", 1)]
    [DataRow("short", "muchlonger", 2)]
    public void TryCalculate_ReturnsOneBeyondBoundWhenNoMatch(
        string source,
        string target,
        int maximumDistance)
    {
        bool matched = BoundedEditDistance.TryCalculate(
            source,
            target,
            maximumDistance,
            out int distance);

        Assert.IsFalse(matched);
        Assert.AreEqual(maximumDistance + 1, distance);
    }

    [TestMethod]
    public void TryCalculate_UsesUnicodeScalarsRatherThanUtf16CodeUnits()
    {
        bool substitutionMatched = BoundedEditDistance.TryCalculate(
            "a😀bc",
            "a😁bc",
            1,
            out int substitutionDistance);
        bool insertionMatched = BoundedEditDistance.TryCalculate(
            "abc",
            "a😀bc",
            1,
            out int insertionDistance);

        Assert.IsTrue(substitutionMatched);
        Assert.AreEqual(1, substitutionDistance);
        Assert.IsTrue(insertionMatched);
        Assert.AreEqual(1, insertionDistance);
    }

    [TestMethod]
    public void TryCalculate_RejectsMalformedUtf16()
    {
        Assert.Throws<ArgumentException>(() =>
            BoundedEditDistance.TryCalculate(
                "valid",
                "bad\uD800",
                1,
                out _));
    }

    [TestMethod]
    public void TryCalculate_RejectsTokensBeyondScalarLimit()
    {
        string oversized = new(
            'a',
            SearchGrammarLimits.MaximumNormalizedTokenRunes + 1);

        Assert.Throws<ArgumentException>(() =>
            BoundedEditDistance.TryCalculate(
                oversized,
                "valid",
                1,
                out _));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(BoundedEditDistance.MaximumSupportedDistance + 1)]
    public void TryCalculate_RejectsUnsupportedDistance(int maximumDistance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BoundedEditDistance.TryCalculate(
                "source",
                "target",
                maximumDistance,
                out _));
    }
}
