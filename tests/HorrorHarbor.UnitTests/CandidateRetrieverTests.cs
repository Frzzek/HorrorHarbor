using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class CandidateRetrieverTests
{
    private static readonly string[] CosmicIsolationCandidateIds =
        ["cosmic-isolation"];
    private static readonly string[] DescentCandidateIds = ["descent-film"];
    private static readonly string[] EligibleCandidateIds = ["eligible-film"];
    private static readonly string[] ExactFilmCandidateIds = ["exact-film"];
    private static readonly string[] ExactTagCandidateIds = ["exact-tag"];
    private static readonly string[] ExorcistCandidateIds =
        ["the-exorcist-iii-1990"];
    private static readonly string[] GonjiamCandidateIds =
        ["gonjiam-haunted-asylum-2018"];
    private static readonly string[] HellHouseCandidateIds =
        ["hell-house-llc-2015"];
    private static readonly string[] MartyrsCandidateIds = ["martyrs-2008"];
    private static readonly string[] PsychologicalCandidateIds =
        ["psychological-film"];
    private static readonly string[] ResurrectionCandidateIds =
        ["resurrection-film"];
    private static readonly string[] UnknownAvailabilityCandidateIds =
        ["unknown-film"];
    private static readonly string[] UnseenCandidateIds = ["unseen-film"];
    private static readonly string[] VerifiedAvailabilityCandidateIds =
        ["verified-film"];
    private static readonly string[] WatchedCandidateIds = ["watched-film"];

    private static readonly SearchMetadataProvenance ProviderProvenance = new(
        SearchMetadataSourceKind.OnlineProvider,
        "streaming-provider",
        "provider-v1");

    private readonly SearchQueryParser _parser = new();

    [TestMethod]
    [DataRow("frontiers", "frontier-s-2007")]
    [DataRow("saw 6", "saw-vi-2009")]
    [DataRow("title:saw", "saw-vi-2009")]
    public void RetrieveExact_MatchesBoundedTitleVariants(
        string queryText,
        string expectedCandidateId)
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("frontier-s-2007", "Frontier(s)"),
            CreateDocument("saw-vi-2009", "Saw VI"));

        string[] candidateIds = RetrieveCandidateIds(snapshot, queryText);

        CollectionAssert.AreEqual(new[] { expectedCandidateId }, candidateIds);
    }

    [TestMethod]
    public void RetrieveExact_RequiresEveryPositiveClauseAcrossAllowedFields()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument(
                "cosmic-isolation",
                "Void Signal",
                tags: ["Isolation"],
                primaryLens: "Cosmic Horror"),
            CreateDocument(
                "cosmic-only",
                "Empty Signal",
                tags: ["Ritual"],
                primaryLens: "Cosmic Horror"),
            CreateDocument(
                "isolation-only",
                "Deep Station",
                tags: ["Isolation"],
                primaryLens: "Survival Horror"));

        string[] candidateIds = RetrieveCandidateIds(
            snapshot,
            "cosmic isolation");

        CollectionAssert.AreEqual(
            CosmicIsolationCandidateIds,
            candidateIds);
    }

    [TestMethod]
    public void RetrieveExact_RequiresExactNormalizedTagForQuotedTag()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument(
                "exact-tag",
                "Exact",
                tags: ["Found Footage"]),
            CreateDocument(
                "longer-tag",
                "Longer",
                tags: ["Found Footage Mystery"]));

        string[] candidateIds = RetrieveCandidateIds(
            snapshot,
            "tag:\"found footage\"");

        CollectionAssert.AreEqual(ExactTagCandidateIds, candidateIds);
    }

    [TestMethod]
    public void RetrieveExact_AppliesFacetsBeforeExclusions()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument(
                "eligible-film",
                "Eligible",
                tags: ["Home Invasion"],
                releaseYear: 2022,
                isExtreme: false),
            CreateDocument(
                "excluded-film",
                "Excluded",
                tags: ["Home Invasion"],
                releaseYear: 2022,
                isExtreme: true),
            CreateDocument(
                "old-film",
                "Old",
                tags: ["Home Invasion"],
                releaseYear: 2010,
                isExtreme: false));

        string[] candidateIds = RetrieveCandidateIds(
            snapshot,
            "tag:\"home invasion\" year:>=2020 -extreme:true");

        CollectionAssert.AreEqual(EligibleCandidateIds, candidateIds);
    }

    [TestMethod]
    public void RetrieveExact_DoesNotInferLanguageFromTags()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument(
                "korean-tag-only",
                "The Signal",
                tags: ["Korean Horror"],
                originalLanguage: null));

        string[] candidateIds = RetrieveCandidateIds(snapshot, "language:ko");

        Assert.AreEqual(0, candidateIds.Length);
    }

    [TestMethod]
    public void RetrieveExact_PreservesAvailabilityHonesty()
    {
        var verifiedFact = new SearchAvailabilityFact(
            "Shudder",
            ProviderProvenance);
        HorrorSearchDocument unknown = CreateDocument(
            "unknown-film",
            "Unknown Film");
        HorrorSearchDocument verified = CreateDocument(
            "verified-film",
            "Verified Film",
            availabilityState: SearchAvailabilityState.Verified,
            availabilityFacts: [verifiedFact]);
        SearchIndexSnapshot snapshot = BuildSnapshot(unknown, verified);

        CollectionAssert.AreEqual(
            UnknownAvailabilityCandidateIds,
            RetrieveCandidateIds(snapshot, "availability:unknown"));
        CollectionAssert.AreEqual(
            VerifiedAvailabilityCandidateIds,
            RetrieveCandidateIds(snapshot, "availability:verified"));
        CollectionAssert.AreEqual(
            VerifiedAvailabilityCandidateIds,
            RetrieveCandidateIds(snapshot, "availability:shudder"));
    }

    [TestMethod]
    public void RetrieveExact_UsesDefensiveUserStatusSnapshot()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("watched-film", "Watched Film"),
            CreateDocument("unseen-film", "Unseen Film"));
        var mutableStatuses = new Dictionary<string, SearchLocalStatus>
        {
            ["watched-film"] = SearchLocalStatus.Watched,
        };
        var context = new SearchUserContext(mutableStatuses);
        mutableStatuses["watched-film"] = SearchLocalStatus.Disliked;

        CollectionAssert.AreEqual(
            WatchedCandidateIds,
            RetrieveCandidateIds(snapshot, "status:watched", context));
        CollectionAssert.AreEqual(
            UnseenCandidateIds,
            RetrieveCandidateIds(snapshot, "status:unseen", context));
    }

    [TestMethod]
    public void RetrieveExact_SupportsDeterministicPrefixMatching()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("descent-film", "The Descent"),
            CreateDocument("other-film", "The Deep"));

        string[] candidateIds = RetrieveCandidateIds(snapshot, "desce");

        CollectionAssert.AreEqual(DescentCandidateIds, candidateIds);
    }

    [TestMethod]
    public void RetrieveExact_HonorsCancellation()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("film", "Film"));
        ParsedSearchQuery query = Parse("film");
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            CandidateRetriever.RetrieveExact(
                snapshot,
                query,
                new SearchUserContext(),
                source.Token));
    }

    [TestMethod]
    [DataRow("martyes", "martyrs-2008")]
    [DataRow("gonjam asylum", "gonjiam-haunted-asylum-2018")]
    [DataRow("exorcst iii", "the-exorcist-iii-1990")]
    [DataRow("hell hose llc", "hell-house-llc-2015")]
    public void Retrieve_RecoversAcceptanceCorpusTitleTypos(
        string queryText,
        string expectedCandidateId)
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("martyrs-2008", "Martyrs"),
            CreateDocument(
                "gonjiam-haunted-asylum-2018",
                "Gonjiam: Haunted Asylum"),
            CreateDocument(
                "the-exorcist-iii-1990",
                "The Exorcist III"),
            CreateDocument("hell-house-llc-2015", "Hell House LLC"));

        CandidateRetrievalResult result = Retrieve(snapshot, queryText);
        string[] candidateIds = GetCandidateIds(snapshot, result.DocumentIds);

        CollectionAssert.AreEqual(new[] { expectedCandidateId }, candidateIds);
        Assert.AreEqual(0, result.ExactDocumentIds.Length);
        Assert.IsTrue(result.FuzzyFallbackAttempted);
    }

    [TestMethod]
    public void Retrieve_UsesDistanceTwoOnlyForEightOrMoreScalars()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("resurrection-film", "Resurrection"),
            CreateDocument("martyrs-2008", "Martyrs"));

        CollectionAssert.AreEqual(
            ResurrectionCandidateIds,
            GetCandidateIds(
                snapshot,
                Retrieve(snapshot, "resurecton").DocumentIds));
        Assert.AreEqual(
            0,
            Retrieve(snapshot, "martxes").DocumentIds.Length);
    }

    [TestMethod]
    public void Retrieve_DoesNotFuzzyMatchTokensOfThreeOrFewerScalars()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("saw-film", "Saw"));

        CandidateRetrievalResult result = Retrieve(snapshot, "sow");

        Assert.AreEqual(0, result.DocumentIds.Length);
        Assert.AreEqual(0, result.ExactDocumentIds.Length);
        Assert.IsFalse(result.FuzzyFallbackAttempted);
    }

    [TestMethod]
    public void Retrieve_DoesNotFuzzyExpandQuotedPhrases()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("martyrs-2008", "Martyrs"));

        CandidateRetrievalResult result = Retrieve(snapshot, "\"martyes\"");

        Assert.AreEqual(0, result.DocumentIds.Length);
        Assert.IsFalse(result.FuzzyFallbackAttempted);
    }

    [TestMethod]
    public void Retrieve_PreservesFieldScopeForFuzzyMatches()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument(
                "psychological-film",
                "Mind Chamber",
                primaryLens: "Psychological Horror"),
            CreateDocument(
                "title-only",
                "Psychological",
                primaryLens: "Survival Horror"));

        CandidateRetrievalResult result = Retrieve(
            snapshot,
            "lens:psycholgical");

        CollectionAssert.AreEqual(
            PsychologicalCandidateIds,
            GetCandidateIds(snapshot, result.DocumentIds));
    }

    [TestMethod]
    public void Retrieve_ReappliesFacetsAndExclusionsAfterFuzzyExpansion()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument(
                "eligible-film",
                "Martyrs",
                releaseYear: 2008,
                isExtreme: false),
            CreateDocument(
                "excluded-film",
                "Martyrs Legacy",
                releaseYear: 2009,
                isExtreme: true),
            CreateDocument(
                "old-film",
                "Martyrs Origin",
                releaseYear: 1999,
                isExtreme: false));

        CandidateRetrievalResult result = Retrieve(
            snapshot,
            "martyes year:>=2000 -extreme:true");

        CollectionAssert.AreEqual(
            EligibleCandidateIds,
            GetCandidateIds(snapshot, result.DocumentIds));
    }

    [TestMethod]
    public void Retrieve_SkipsFuzzyFallbackWhenExactWindowIsFull()
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("exact-film", "Martyes"),
            CreateDocument("fuzzy-film", "Martyrs"));

        CandidateRetrievalResult result = Retrieve(
            snapshot,
            "martyes",
            requestedResultCount: 1);

        CollectionAssert.AreEqual(
            ExactFilmCandidateIds,
            GetCandidateIds(snapshot, result.DocumentIds));
        Assert.IsFalse(result.FuzzyFallbackAttempted);
        CollectionAssert.AreEqual(
            result.DocumentIds.ToArray(),
            result.ExactDocumentIds.ToArray());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(SearchGrammarLimits.MaximumLocalResults + 1)]
    public void Retrieve_RejectsInvalidRequestedResultCount(
        int requestedResultCount)
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(
            CreateDocument("film", "Film"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CandidateRetriever.Retrieve(
                snapshot,
                Parse("film"),
                new SearchUserContext(),
                requestedResultCount));
    }

    private string[] RetrieveCandidateIds(
        SearchIndexSnapshot snapshot,
        string queryText,
        SearchUserContext? context = null)
    {
        CandidateRetrievalResult result = CandidateRetriever.RetrieveExact(
            snapshot,
            Parse(queryText),
            context ?? new SearchUserContext());

        return result.DocumentIds
            .Select(documentId => snapshot.GetDocument(documentId).CandidateId)
            .ToArray();
    }

    private CandidateRetrievalResult Retrieve(
        SearchIndexSnapshot snapshot,
        string queryText,
        int requestedResultCount = 10,
        SearchUserContext? context = null) => CandidateRetriever.Retrieve(
            snapshot,
            Parse(queryText),
            context ?? new SearchUserContext(),
            requestedResultCount);

    private static string[] GetCandidateIds(
        SearchIndexSnapshot snapshot,
        IEnumerable<int> documentIds) => documentIds
            .Select(documentId => snapshot.GetDocument(documentId).CandidateId)
            .ToArray();

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
        IReadOnlyList<string>? tags = null,
        string? primaryLens = "Survival Horror",
        int releaseYear = 2007,
        bool isExtreme = false,
        string? originalLanguage = null,
        SearchAvailabilityState availabilityState = SearchAvailabilityState.Unknown,
        IEnumerable<SearchAvailabilityFact>? availabilityFacts = null)
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate(
            title,
            tags,
            primaryLens,
            originalLanguage: originalLanguage) with
        {
            CandidateId = candidateId,
            ReleaseYear = releaseYear,
            IsExtreme = isExtreme,
        };

        return new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            availabilityState,
            availabilityFacts: availabilityFacts);
    }
}
