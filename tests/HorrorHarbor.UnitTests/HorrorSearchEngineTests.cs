using System.Globalization;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Search;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Ranking;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class HorrorSearchEngineTests
{
    [TestMethod]
    public void Execute_ReturnsIndexUnavailableWithoutPublishedSnapshot()
    {
        var engine = new HorrorSearchEngine(
            new SearchIndexStore(),
            new SearchQueryParser());

        SearchIndexUnavailableResponse response =
            Assert.IsInstanceOfType<SearchIndexUnavailableResponse>(
                engine.Execute(CreateRequest("the descent")));

        Assert.AreEqual(
            SearchIndexUnavailableReason.NoPublishedSnapshot,
            response.Reason);
        Assert.AreEqual("no-published-snapshot", response.StableReasonCode);
    }

    [TestMethod]
    public void Execute_ReturnsTypedParserRejection()
    {
        HorrorSearchEngine engine = CreateEngine(
            CreateDocument("descent", "The Descent"));

        SearchRejectedResponse response =
            Assert.IsInstanceOfType<SearchRejectedResponse>(
                engine.Execute(CreateRequest("title:\"the descent")));

        Assert.AreEqual(
            SearchDiagnosticCode.UnterminatedQuote,
            response.Diagnostic.Code);
    }

    [TestMethod]
    public void Execute_RanksCompleteSetThenReturnsRequestedWindow()
    {
        HorrorSearchDocument exact = CreateDocument("exact", "The Descent");
        HorrorSearchDocument weaker = CreateDocument(
            "weaker",
            "The Descent Into Darkness");
        var context = new SearchUserContext(
            statuses: new Dictionary<string, SearchLocalStatus>(StringComparer.Ordinal)
            {
                ["exact"] = SearchLocalStatus.Watched,
            },
            affinities: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["exact"] = 0,
                ["weaker"] = 100,
            });
        HorrorSearchEngine engine = CreateEngine(exact, weaker);

        SearchSuccessResponse response =
            Assert.IsInstanceOfType<SearchSuccessResponse>(engine.Execute(
                new SearchRequest("the descent", 1, context)));

        Assert.AreEqual(SearchIntent.LocalSearch, response.Intent);
        Assert.AreEqual(2, response.TotalMatchingCount);
        Assert.HasCount(1, response.Results);
        SearchResult result = response.Results[0];
        Assert.AreEqual("exact", result.CandidateId);
        Assert.AreEqual(SearchLexicalTier.ExactCanonicalTitle, result.LexicalTier);
        Assert.AreEqual(SearchLocalStatus.Watched, result.LocalStatus);
        Assert.AreEqual(85d, result.HybridScore);
        Assert.IsFalse(result.IsFilterOnly);
        Assert.AreEqual(SearchIntent.LocalSearch, result.Explanation.Intent);
        Assert.AreEqual(
            SearchLexicalTier.ExactCanonicalTitle,
            result.Explanation.StrongestMatchClass);
        AssertExplanationRow(
            result,
            "canonical-title-exact",
            SearchExplanationRowKind.MatchEvidence);
        AssertExplanationRow(
            result,
            "status-watched",
            SearchExplanationRowKind.StatusEvidence);
        AssertExplanationScore(result, "score-title-identity", 100, 30);
        AssertExplanationScore(result, "score-lexical-total", 100, 85);
        AssertExplanationScore(result, "score-affinity", 0, 15);
        AssertExplanationScore(result, "score-hybrid-total", 85, null);
    }

    [TestMethod]
    public void Execute_FilterOnlyResultIsExplicitAndUsesAffinity()
    {
        HorrorSearchDocument document = CreateDocument("film", "A Film");
        var context = new SearchUserContext(
            affinities: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["film"] = 73.456,
            });
        HorrorSearchEngine engine = CreateEngine(document);

        SearchSuccessResponse response =
            Assert.IsInstanceOfType<SearchSuccessResponse>(engine.Execute(
                new SearchRequest("year:>=2000", 10, context)));

        Assert.AreEqual(SearchIntent.FilteredDiscovery, response.Intent);
        Assert.HasCount(1, response.Results);
        Assert.IsTrue(response.Results[0].IsFilterOnly);
        Assert.AreEqual(0d, response.Results[0].LexicalScore.Total);
        Assert.AreEqual(73.46, response.Results[0].HybridScore);
        Assert.IsTrue(response.Results[0].Explanation.IsFilterOnly);
        AssertExplanationRow(
            response.Results[0],
            "year-filter-satisfied",
            SearchExplanationRowKind.FacetEvidence);
        AssertExplanationScore(
            response.Results[0],
            "score-facet-satisfaction",
            100,
            5);
        AssertExplanationScore(
            response.Results[0],
            "score-affinity",
            73.46,
            100);
        Assert.IsFalse(response.Results[0].Explanation.Rows.Any(
            row => row.StableCode == "score-lexical-total"));
    }

    [TestMethod]
    public void Execute_ReturnsTypedCancellationForSuppliedToken()
    {
        HorrorSearchEngine engine = CreateEngine(
            CreateDocument("descent", "The Descent"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        SearchResponse response = engine.Execute(
            CreateRequest("the descent"),
            source.Token);

        Assert.IsInstanceOfType<SearchCanceledResponse>(response);
    }

    [TestMethod]
    public void Execute_RepeatedRunsProduceIdenticalOrderedResults()
    {
        HorrorSearchEngine engine = CreateEngine(
            CreateDocument("third", "The Third Shadow"),
            CreateDocument("first", "The First Shadow"),
            CreateDocument("second", "The Second Shadow"));
        SearchRequest request = CreateRequest("the shadow");
        string[] expected = ExecuteCandidateIds(engine, request);
        string[] expectedExplanations = ExecuteExplanationSignatures(engine, request);

        for (int iteration = 0; iteration < 20; iteration++)
        {
            CollectionAssert.AreEqual(
                expected,
                ExecuteCandidateIds(engine, request));
            CollectionAssert.AreEqual(
                expectedExplanations,
                ExecuteExplanationSignatures(engine, request));
        }
    }

    [TestMethod]
    public void Execute_OrderIsIndependentOfCurrentCulture()
    {
        HorrorSearchEngine engine = CreateEngine(
            CreateDocument("island", "The Island Shadow"),
            CreateDocument("inferno", "The Inferno Shadow"));
        SearchRequest request = CreateRequest("the shadow");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            string[] expected = ExecuteCandidateIds(engine, request);
            string[] expectedExplanations =
                ExecuteExplanationSignatures(engine, request);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            CollectionAssert.AreEqual(
                expected,
                ExecuteCandidateIds(engine, request));
            CollectionAssert.AreEqual(
                expectedExplanations,
                ExecuteExplanationSignatures(engine, request));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public void SearchRequest_RejectsInvalidResultLimit()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new SearchRequest("query", 0, new SearchUserContext()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new SearchRequest(
                "query",
                SearchGrammarLimits.MaximumLocalResults + 1,
                new SearchUserContext()));
    }

    private static SearchRequest CreateRequest(string queryText) => new(
        queryText: queryText,
        resultLimit: 10,
        userContext: new SearchUserContext());

    private static string[] ExecuteCandidateIds(
        HorrorSearchEngine engine,
        SearchRequest request)
    {
        SearchSuccessResponse response =
            Assert.IsInstanceOfType<SearchSuccessResponse>(
                engine.Execute(request));
        return [.. response.Results.Select(result => result.CandidateId)];
    }

    private static string[] ExecuteExplanationSignatures(
        HorrorSearchEngine engine,
        SearchRequest request)
    {
        SearchSuccessResponse response =
            Assert.IsInstanceOfType<SearchSuccessResponse>(
                engine.Execute(request));
        return [.. response.Results.SelectMany(result =>
            result.Explanation.Rows.Select(row => string.Join(
                '|',
                result.CandidateId,
                row.Kind,
                row.StableCode,
                row.Value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
                row.Weight?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
                row.Evidence?.StableCode ?? string.Empty,
                row.Evidence?.NormalizedQueryValue ?? string.Empty,
                row.Evidence?.NormalizedIndexedValue ?? string.Empty)))];
    }

    private static void AssertExplanationRow(
        SearchResult result,
        string stableCode,
        SearchExplanationRowKind expectedKind)
    {
        SearchExplanationRow row = result.Explanation.Rows.Single(
            candidate => candidate.StableCode == stableCode);
        Assert.AreEqual(expectedKind, row.Kind);
        Assert.IsNotNull(row.Evidence);
    }

    private static void AssertExplanationScore(
        SearchResult result,
        string stableCode,
        double expectedValue,
        double? expectedWeight)
    {
        SearchExplanationRow row = result.Explanation.Rows.Single(
            candidate => candidate.StableCode == stableCode);
        Assert.AreEqual(expectedValue, row.Value);
        Assert.AreEqual(expectedWeight, row.Weight);
        Assert.IsNull(row.Evidence);
    }

    private static HorrorSearchEngine CreateEngine(
        params HorrorSearchDocument[] documents)
    {
        SearchIndexBuildSucceeded build =
            Assert.IsInstanceOfType<SearchIndexBuildSucceeded>(
                new SearchIndexBuilder().Build(documents));
        var store = new SearchIndexStore();
        store.Publish(build.Snapshot);
        return new HorrorSearchEngine(store, new SearchQueryParser());
    }

    private static HorrorSearchDocument CreateDocument(
        string candidateId,
        string title)
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate(title) with
        {
            CandidateId = candidateId,
            ReleaseYear = 2020,
        };
        return new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            SearchAvailabilityState.Unknown);
    }
}
