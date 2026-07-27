using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Ranking;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class LexicalScorerTests
{
    [TestMethod]
    public void Score_PerfectCanonicalIdentityProducesOneHundred()
    {
        LexicalScoreBreakdown score = LexicalScorer.Score(CreateCollection(
            SearchLexicalTier.ExactCanonicalTitle,
            new SearchEvidence(
                SearchEvidenceCode.CanonicalTitleExact,
                SearchEvidenceField.CanonicalTitle,
                "the descent",
                "the descent",
                titleOrigin: SearchTitleVariantOrigin.CanonicalTitle),
            new SearchEvidence(
                SearchEvidenceCode.LexicalTokenMatch,
                SearchEvidenceField.CanonicalTitle,
                "the",
                "the",
                titleOrigin: SearchTitleVariantOrigin.CanonicalTitle),
            new SearchEvidence(
                SearchEvidenceCode.LexicalTokenMatch,
                SearchEvidenceField.CanonicalTitle,
                "descent",
                "descent",
                titleOrigin: SearchTitleVariantOrigin.CanonicalTitle)));

        Assert.AreEqual(100d, score.Total);
        Assert.AreEqual(100d, score.TitleIdentity);
        Assert.AreEqual(100d, score.LexicalCoverage);
        Assert.AreEqual(100d, score.FieldImportance);
        Assert.AreEqual(100d, score.Exactness);
    }

    [TestMethod]
    public void Score_DistanceTwoNonTitleMatchAppliesBoundedPenalty()
    {
        LexicalScoreBreakdown score = LexicalScorer.Score(CreateCollection(
            SearchLexicalTier.FuzzyNonTitleMatch,
            new SearchEvidence(
                SearchEvidenceCode.FuzzyNonTitleEditDistance2,
                SearchEvidenceField.Tag,
                "psychologicxx",
                "psychological",
                editDistance: 2)));

        Assert.AreEqual(0d, score.TitleIdentity);
        Assert.AreEqual(60d, score.FieldImportance);
        Assert.AreEqual(50d, score.Exactness);
        Assert.AreEqual(52d, score.Total);
    }

    [TestMethod]
    public void Score_FilterOnlyResultReportsZeroLexicalTotal()
    {
        var collection = new LexicalEvidenceCollection(
            tier: null,
            ImmutableArray.Create(new SearchEvidence(
                SearchEvidenceCode.YearFilterSatisfied,
                SearchEvidenceField.ReleaseYear)),
            positiveHardFacetCount: 1);

        LexicalScoreBreakdown score = LexicalScorer.Score(collection);

        Assert.AreEqual(0d, score.Total);
        Assert.AreEqual(100d, score.FacetSatisfaction);
    }

    [TestMethod]
    public void Score_DuplicateSpecializedAndFormEvidenceCountsOneFact()
    {
        LexicalScoreBreakdown score = LexicalScorer.Score(CreateCollection(
            SearchLexicalTier.ExactTokenMatch,
            new SearchEvidence(
                SearchEvidenceCode.PrimaryLensMatch,
                SearchEvidenceField.PrimaryLens,
                "cosmic",
                "cosmic"),
            new SearchEvidence(
                SearchEvidenceCode.LexicalTokenMatch,
                SearchEvidenceField.PrimaryLens,
                "cosmic",
                "cosmic")));

        Assert.AreEqual(75d, score.FieldImportance);
        Assert.AreEqual(100d, score.Exactness);
    }

    private static LexicalEvidenceCollection CreateCollection(
        SearchLexicalTier tier,
        params SearchEvidence[] evidence) => new(
            tier,
            ImmutableArray.CreateRange(evidence),
            positiveHardFacetCount: 0);
}
