using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchTextNormalizerTests
{
    [TestMethod]
    [DataRow("You're Next", "youre next", "youre|next")]
    [DataRow("Frontier(s)", "frontier s", "frontier|s")]
    [DataRow("séance", "seance", "seance")]
    [DataRow("Noroi: The Curse", "noroi the curse", "noroi|the|curse")]
    [DataRow("ＳＡＷ Ⅵ", "saw vi", "saw|vi")]
    [DataRow("𐐀 Horror", "𐐨 horror", "𐐨|horror")]
    public void Normalize_ProducesExpectedMovieSearchCopy(
        string source,
        string expectedValue,
        string expectedTokens)
    {
        NormalizedSearchText result = SearchTextNormalizerV1.Normalize(source);

        Assert.AreEqual(expectedValue, result.Value);
        CollectionAssert.AreEqual(
            expectedTokens.Split('|'),
            result.Tokens.ToArray());
    }

    [TestMethod]
    public void Normalize_LeavesAlreadyNormalizedTextUnchanged()
    {
        NormalizedSearchText result = SearchTextNormalizerV1.Normalize("noroi the curse");

        Assert.AreEqual("noroi the curse", result.Value);
        Assert.AreEqual(SearchNormalizationChanges.None, result.Changes);
    }

    [TestMethod]
    public void Normalize_ReportsOnlyTransformationsThatOccurred()
    {
        NormalizedSearchText result = SearchTextNormalizerV1.Normalize("You're Next");

        Assert.AreEqual(
            SearchNormalizationChanges.ApostropheRemoved,
            result.Changes & SearchNormalizationChanges.ApostropheRemoved);
        Assert.AreEqual(
            SearchNormalizationChanges.InvariantLowercased,
            result.Changes & SearchNormalizationChanges.InvariantLowercased);
        Assert.AreEqual(
            SearchNormalizationChanges.None,
            result.Changes & SearchNormalizationChanges.WhitespaceNormalized);
        Assert.AreEqual(
            SearchNormalizationChanges.None,
            result.Changes & SearchNormalizationChanges.PunctuationOrSymbolNormalized);
    }

    [TestMethod]
    public void Normalize_PreservesNonLatinCombiningMarks()
    {
        const string Source = "कि";

        NormalizedSearchText result = SearchTextNormalizerV1.Normalize(Source);

        Assert.AreEqual(Source, result.Value);
        Assert.AreEqual(
            SearchNormalizationChanges.None,
            result.Changes & SearchNormalizationChanges.LatinDiacriticRemoved);
    }

    [TestMethod]
    public void Normalize_PreservesJoinControlsNeededBySomeScripts()
    {
        const string Source = "क्‍ष";

        NormalizedSearchText result = SearchTextNormalizerV1.Normalize(Source);

        Assert.AreEqual(Source, result.Value);
    }

    [TestMethod]
    public void Normalize_IsIdempotentForItsMatchingCopy()
    {
        NormalizedSearchText first = SearchTextNormalizerV1.Normalize("  Noroi:  The Curse  ");
        NormalizedSearchText second = SearchTextNormalizerV1.Normalize(first.Value);

        Assert.AreEqual(first.Value, second.Value);
        CollectionAssert.AreEqual(first.Tokens.ToArray(), second.Tokens.ToArray());
        Assert.AreEqual(SearchNormalizationChanges.None, second.Changes);
    }

    [TestMethod]
    public void Normalize_RejectsUnsafeFormatCharacters() =>
        Assert.Throws<ArgumentException>(
            () => SearchTextNormalizerV1.Normalize("Noroi\u202EThe Curse"));

    [TestMethod]
    public void Normalize_RejectsControlCharacters() =>
        Assert.Throws<ArgumentException>(
            () => SearchTextNormalizerV1.Normalize("Noroi\nThe Curse"));
}
