using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Ranking;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class HybridRankerTests
{
    private readonly SearchQueryParser _parser = new();

    [TestMethod]
    public void Rank_StrongerTierWinsEvenWithLowerAffinity()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("exact", "The Descent", quality: 0.1, discovery: 0.1),
            CreateDocument("token", "The Descent Into Darkness", quality: 1, discovery: 1));
        var context = new SearchUserContext(
            affinities: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["exact"] = 0,
                ["token"] = 100,
            });
        ParsedSearchQuery query = Parse("the descent");
        CandidateRetrievalResult retrieval = CandidateRetriever.Retrieve(
            snapshot,
            query,
            context,
            requestedResultCount: 10);

        RankedSearchCandidate[] ranked = HybridRanker.Rank(
            snapshot,
            retrieval,
            query,
            context).ToArray();

        Assert.HasCount(2, ranked);
        Assert.AreEqual("exact", ranked[0].CandidateId);
        Assert.AreEqual(SearchLexicalTier.ExactCanonicalTitle, ranked[0].LexicalTier);
        Assert.AreEqual(85d, ranked[0].HybridScore);
    }

    [TestMethod]
    public void Rank_FilterOnlyUsesAffinityBeforeQualityAndTitle()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("alpha", "Alpha", quality: 1, discovery: 1),
            CreateDocument("beta", "Beta", quality: 0, discovery: 0));
        var context = new SearchUserContext(
            affinities: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["alpha"] = 10,
                ["beta"] = 90,
            });
        ParsedSearchQuery query = Parse("year:>=2000");
        CandidateRetrievalResult retrieval = CandidateRetriever.Retrieve(
            snapshot,
            query,
            context,
            requestedResultCount: 1);

        RankedSearchCandidate[] ranked = HybridRanker.Rank(
            snapshot,
            retrieval,
            query,
            context).ToArray();

        Assert.HasCount(2, ranked);
        Assert.AreEqual("beta", ranked[0].CandidateId);
        Assert.IsNull(ranked[0].LexicalTier);
        Assert.AreEqual(90d, ranked[0].HybridScore);
    }

    [TestMethod]
    public void RankWindow_TruncatesOnlyAfterRankingCompleteCandidateSet()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("alpha", "Alpha", quality: 1, discovery: 1),
            CreateDocument("zulu", "Zulu", quality: 0, discovery: 0));
        var context = new SearchUserContext(
            affinities: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["alpha"] = 0,
                ["zulu"] = 100,
            });
        ParsedSearchQuery query = Parse("year:>=2000");
        CandidateRetrievalResult retrieval = CandidateRetriever.Retrieve(
            snapshot,
            query,
            context,
            requestedResultCount: 1);

        RankedSearchCandidate[] window = HybridRanker.RankWindow(
            snapshot,
            retrieval,
            query,
            context,
            requestedResultCount: 1).ToArray();

        Assert.HasCount(1, window);
        Assert.AreEqual("zulu", window[0].CandidateId);
    }

    private ParsedSearchQuery Parse(string queryText)
    {
        SearchQueryParsed parsed = Assert.IsInstanceOfType<SearchQueryParsed>(
            _parser.Parse(queryText));
        return parsed.Query;
    }

    private static SearchIndexSnapshot BuildSnapshot(
        params HorrorSearchDocument[] documents)
    {
        SearchIndexBuildSucceeded result =
            Assert.IsInstanceOfType<SearchIndexBuildSucceeded>(
                new SearchIndexBuilder().Build(documents));
        return result.Snapshot;
    }

    private static HorrorSearchDocument CreateDocument(
        string candidateId,
        string title,
        double quality,
        double discovery)
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate(title) with
        {
            CandidateId = candidateId,
            QualityScore = quality,
            DiscoveryScore = discovery,
            ReleaseYear = 2020,
        };
        return new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            SearchAvailabilityState.Unknown);
    }
}
