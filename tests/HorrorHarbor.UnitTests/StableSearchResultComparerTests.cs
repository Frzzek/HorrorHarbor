using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Ranking;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class StableSearchResultComparerTests
{
    [TestMethod]
    public void Compare_StrongerTierAlwaysWinsRegardlessOfScore()
    {
        RankedSearchCandidate stronger = Create(
            "stronger",
            SearchLexicalTier.ExactCanonicalTitle,
            hybrid: 1);
        RankedSearchCandidate weaker = Create(
            "weaker",
            SearchLexicalTier.FuzzyNonTitleMatch,
            hybrid: 100);

        Assert.IsTrue(StableSearchResultComparer.Instance.Compare(stronger, weaker) < 0);
    }

    [TestMethod]
    public void Compare_UsesCompleteDocumentedTieBreakOrder()
    {
        RankedSearchCandidate earlierYear = Create(
            "z-id",
            SearchLexicalTier.ExactTokenMatch,
            normalizedTitle: "same",
            releaseYear: 1980);
        RankedSearchCandidate missingYear = Create(
            "a-id",
            SearchLexicalTier.ExactTokenMatch,
            normalizedTitle: "same",
            releaseYear: null);

        Assert.IsTrue(
            StableSearchResultComparer.Instance.Compare(earlierYear, missingYear) < 0);

        RankedSearchCandidate firstId = Create(
            "a-id",
            SearchLexicalTier.ExactTokenMatch,
            normalizedTitle: "same",
            releaseYear: 1980);
        Assert.IsTrue(
            StableSearchResultComparer.Instance.Compare(firstId, earlierYear) < 0);
    }

    [TestMethod]
    public void Compare_FilterOnlyResultsUseAffinityAsPrimaryScore()
    {
        RankedSearchCandidate higherAffinity = Create(
            "high",
            tier: null,
            hybrid: 90,
            affinity: 90);
        RankedSearchCandidate lowerAffinity = Create(
            "low",
            tier: null,
            hybrid: 10,
            affinity: 10);

        Assert.IsTrue(
            StableSearchResultComparer.Instance.Compare(
                higherAffinity,
                lowerAffinity) < 0);
    }

    private static RankedSearchCandidate Create(
        string candidateId,
        SearchLexicalTier? tier,
        double hybrid = 50,
        double affinity = 50,
        string normalizedTitle = "title",
        int? releaseYear = 2000) => new(
            documentId: 0,
            candidateId,
            normalizedTitle,
            releaseYear,
            tier,
            new LexicalScoreBreakdown(50, 100, 50, 100, 100, 70),
            affinity,
            hybrid,
            qualityScore: 50,
            discoveryScore: 50,
            ImmutableArray<SearchEvidence>.Empty);
}
