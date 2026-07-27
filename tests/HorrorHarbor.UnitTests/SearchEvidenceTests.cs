using HorrorHarbor.Search.Contracts;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchEvidenceTests
{
    [TestMethod]
    public void ToStableCode_MapsEveryCodeToUniqueOrdinalValue()
    {
        SearchEvidenceCode[] codes = Enum.GetValues<SearchEvidenceCode>();

        string[] stableCodes = codes
            .Select(static code => code.ToStableCode())
            .ToArray();

        Assert.AreEqual(codes.Length, stableCodes.Length);
        Assert.AreEqual(codes.Length, stableCodes.Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(stableCodes.All(static value => value.Length > 0));
    }

    [TestMethod]
    public void Constructor_RequiresDistanceThatMatchesFuzzyCode()
    {
        var evidence = new SearchEvidence(
            SearchEvidenceCode.FuzzyTitleEditDistance2,
            SearchEvidenceField.CanonicalTitle,
            "exorcst",
            "exorcist",
            2,
            SearchTitleVariantOrigin.CanonicalTitle);

        Assert.AreEqual("fuzzy-title-edit-distance-2", evidence.StableCode);
        Assert.AreEqual(2, evidence.EditDistance);
        Assert.ThrowsExactly<ArgumentException>(() => new SearchEvidence(
            SearchEvidenceCode.FuzzyTitleEditDistance2,
            SearchEvidenceField.CanonicalTitle,
            editDistance: 1));
        Assert.ThrowsExactly<ArgumentException>(() => new SearchEvidence(
            SearchEvidenceCode.CanonicalTitleToken,
            SearchEvidenceField.CanonicalTitle,
            editDistance: 1));
    }

    [TestMethod]
    public void Constructor_RejectsTitleOriginOnMismatchedField()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SearchEvidence(
            SearchEvidenceCode.FuzzyTitleEditDistance1,
            SearchEvidenceField.Tag,
            "martyes",
            "martyrs",
            1,
            SearchTitleVariantOrigin.CanonicalTitle));
    }

    [TestMethod]
    public void Constructor_RejectsCodeOnUnsupportedField()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SearchEvidence(
            SearchEvidenceCode.YearFilterSatisfied,
            SearchEvidenceField.CanonicalTitle));
        Assert.ThrowsExactly<ArgumentException>(() => new SearchEvidence(
            SearchEvidenceCode.FuzzyNonTitleEditDistance1,
            SearchEvidenceField.OriginalTitle,
            editDistance: 1));
        Assert.ThrowsExactly<ArgumentException>(() => new SearchEvidence(
            SearchEvidenceCode.StatusWatched,
            SearchEvidenceField.Availability));
    }
}
