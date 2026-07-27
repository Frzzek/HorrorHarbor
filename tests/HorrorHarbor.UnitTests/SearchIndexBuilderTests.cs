using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchIndexBuilderTests
{
    private static readonly int[] DocumentZero = [0];
    private static readonly int[] DocumentOne = [1];
    private static readonly int[] FirstTwoDocuments = [0, 1];

    private static readonly SearchMetadataProvenance ProviderProvenance = new(
        SearchMetadataSourceKind.OnlineProvider,
        "streaming-provider",
        "provider-v1");

    private readonly SearchIndexBuilder _builder = new();

    [TestMethod]
    public void Build_SortsDocumentsByCandidateIdOrdinally()
    {
        HorrorSearchDocument second = CreateDocument(
            candidateId: "z-film",
            title: "Zulu Horror");
        HorrorSearchDocument first = CreateDocument(
            candidateId: "A-film",
            title: "Alpha Horror");

        SearchIndexSnapshot snapshot = BuildSnapshot(second, first);

        Assert.AreEqual("A-film", snapshot.GetDocument(0).CandidateId);
        Assert.AreEqual("z-film", snapshot.GetDocument(1).CandidateId);
        Assert.IsTrue(snapshot.TryGetDocumentId("A-film", out int documentId));
        Assert.AreEqual(0, documentId);
    }

    [TestMethod]
    public void Build_ProducesDeterministicFingerprintAcrossInputOrder()
    {
        HorrorSearchDocument first = CreateDocument(
            candidateId: "alpha-film",
            title: "Alpha Horror");
        HorrorSearchDocument second = CreateDocument(
            candidateId: "zulu-film",
            title: "Zulu Horror");

        SearchIndexSnapshot forward = BuildSnapshot(first, second);
        SearchIndexSnapshot reverse = BuildSnapshot(second, first);

        Assert.AreEqual(
            forward.CatalogFingerprint,
            reverse.CatalogFingerprint);
        Assert.AreEqual(forward.SnapshotVersion, reverse.SnapshotVersion);
        Assert.AreEqual(64, forward.CatalogFingerprint.Length);
    }

    [TestMethod]
    public void Build_ChangesFingerprintWhenSourceRevisionChanges()
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate();
        var first = new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            SearchAvailabilityState.Unknown);
        var second = new HorrorSearchDocument(
            candidate,
            "catalog-v2",
            SearchAvailabilityState.Unknown);

        SearchIndexSnapshot firstSnapshot = BuildSnapshot(first);
        SearchIndexSnapshot secondSnapshot = BuildSnapshot(second);

        Assert.AreNotEqual(
            firstSnapshot.CatalogFingerprint,
            secondSnapshot.CatalogFingerprint);
    }

    [TestMethod]
    public void Build_CreatesSortedDuplicateFreeLexicalPostings()
    {
        HorrorSearchDocument first = CreateDocument(
            candidateId: "a-film",
            title: "Frontier(s)",
            tags: ["Survival", "survíval"],
            primaryLens: "Body Horror",
            franchise: "Frontier Saga",
            originalLanguage: "French");
        HorrorSearchDocument second = CreateDocument(
            candidateId: "b-film",
            title: "Saw VI",
            tags: ["Survival"],
            primaryLens: "Body Horror");

        SearchIndexSnapshot snapshot = BuildSnapshot(first, second);

        CollectionAssert.AreEqual(
            DocumentZero,
            snapshot.GetCanonicalTitlePostings("frontier s").ToArray());
        CollectionAssert.AreEqual(
            DocumentZero,
            snapshot.GetTitleVariantPostings("frontiers").ToArray());
        CollectionAssert.AreEqual(
            DocumentOne,
            snapshot.GetTitleVariantPostings("saw 6").ToArray());
        CollectionAssert.AreEqual(
            FirstTwoDocuments,
            snapshot.GetExactTagPostings("survival").ToArray());
        CollectionAssert.AreEqual(
            FirstTwoDocuments,
            snapshot.GetLensTokenPostings("body").ToArray());
        CollectionAssert.AreEqual(
            DocumentZero,
            snapshot.GetFranchiseTokenPostings("saga").ToArray());
        CollectionAssert.AreEqual(
            DocumentZero,
            snapshot.GetOriginalLanguagePostings("french").ToArray());
    }

    [TestMethod]
    public void Build_IndexesVerifiedAvailabilityAndStaticCoverage()
    {
        var fact = new SearchAvailabilityFact("Shudder", ProviderProvenance);
        var verified = new HorrorSearchDocument(
            SearchTestData.CreateCandidate() with
            {
                CandidateId = "verified-film",
                OriginalTitle = "Frontières",
                OriginalLanguage = "French",
                Franchise = "Frontier Saga",
                IsSlasher = true,
                IsExtreme = true,
            },
            "catalog-v1",
            SearchAvailabilityState.Verified,
            availabilityFacts: [fact]);

        SearchIndexSnapshot snapshot = BuildSnapshot(verified);

        CollectionAssert.AreEqual(
            DocumentZero,
            snapshot.GetAvailabilityPostings("shudder").ToArray());
        Assert.AreEqual(1, snapshot.Coverage.OriginalTitle);
        Assert.AreEqual(1, snapshot.Coverage.OriginalLanguage);
        Assert.AreEqual(1, snapshot.Coverage.Franchise);
        Assert.AreEqual(1, snapshot.Coverage.VerifiedAvailability);
        Assert.AreEqual(1, snapshot.Coverage.Slasher);
        Assert.AreEqual(1, snapshot.Coverage.Extreme);
    }

    [TestMethod]
    public void Build_RejectsDuplicateCandidateIdsWithoutSnapshot()
    {
        HorrorSearchDocument first = CreateDocument(
            candidateId: "duplicate-film",
            title: "First Film");
        HorrorSearchDocument second = CreateDocument(
            candidateId: "duplicate-film",
            title: "Second Film");

        SearchIndexBuildRejected result =
            Assert.IsInstanceOfType<SearchIndexBuildRejected>(
                _builder.Build([first, second]));

        Assert.AreEqual(
            SearchIndexBuildIssueCode.DuplicateCandidateId,
            result.Issues[0].Code);
        Assert.AreEqual("duplicate-candidate-id", result.Issues[0].StableCode);
        Assert.AreEqual("duplicate-film", result.Issues[0].CandidateId);
        Assert.AreEqual(1, result.Issues[0].DocumentIndex);
        Assert.AreEqual(0, result.Issues[0].ConflictingDocumentIndex);
    }

    [TestMethod]
    public void Build_RejectsDuplicateNormalizedTitleAndYear()
    {
        HorrorSearchDocument first = CreateDocument(
            candidateId: "first-film",
            title: "You're Next");
        HorrorSearchDocument second = CreateDocument(
            candidateId: "second-film",
            title: "You’re Next");

        SearchIndexBuildRejected result =
            Assert.IsInstanceOfType<SearchIndexBuildRejected>(
                _builder.Build([first, second]));

        Assert.AreEqual(
            SearchIndexBuildIssueCode.DuplicateCanonicalIdentity,
            result.Issues[0].Code);
        Assert.AreEqual("second-film", result.Issues[0].CandidateId);
        Assert.AreEqual(1, result.Issues[0].DocumentIndex);
        Assert.AreEqual(0, result.Issues[0].ConflictingDocumentIndex);
    }

    [TestMethod]
    public void Build_DuplicateReportRetainsOriginalSourcePositionsAfterSort()
    {
        HorrorSearchDocument first = CreateDocument("z-first", "Same Film");
        HorrorSearchDocument unrelated = CreateDocument("a-film", "Other Film");
        HorrorSearchDocument duplicate = CreateDocument("m-duplicate", "Same Film");

        SearchIndexBuildRejected result =
            Assert.IsInstanceOfType<SearchIndexBuildRejected>(
                _builder.Build([first, unrelated, duplicate]));

        SearchIndexBuildIssue issue = result.Issues.Single(candidate =>
            candidate.Code == SearchIndexBuildIssueCode.DuplicateCanonicalIdentity);
        Assert.AreEqual("m-duplicate", issue.CandidateId);
        Assert.AreEqual(2, issue.DocumentIndex);
        Assert.AreEqual(0, issue.ConflictingDocumentIndex);
    }

    [TestMethod]
    public void Build_EveryDuplicateCandidateIdPointsToFirstSourceRecord()
    {
        HorrorSearchDocument first = CreateDocument("duplicate", "First");
        HorrorSearchDocument second = CreateDocument("duplicate", "Second");
        HorrorSearchDocument third = CreateDocument("duplicate", "Third");

        SearchIndexBuildRejected result =
            Assert.IsInstanceOfType<SearchIndexBuildRejected>(
                _builder.Build([first, second, third]));

        SearchIndexBuildIssue[] duplicateIssues = [.. result.Issues.Where(issue =>
            issue.Code == SearchIndexBuildIssueCode.DuplicateCandidateId)];
        Assert.HasCount(2, duplicateIssues);
        Assert.AreEqual(1, duplicateIssues[0].DocumentIndex);
        Assert.AreEqual(0, duplicateIssues[0].ConflictingDocumentIndex);
        Assert.AreEqual(2, duplicateIssues[1].DocumentIndex);
        Assert.AreEqual(0, duplicateIssues[1].ConflictingDocumentIndex);
    }

    [TestMethod]
    public void Build_RejectsEmptyCatalog()
    {
        SearchIndexBuildRejected result =
            Assert.IsInstanceOfType<SearchIndexBuildRejected>(
                _builder.Build([]));

        Assert.AreEqual(0, result.ObservedDocumentCount);
        Assert.AreEqual(
            SearchIndexBuildIssueCode.EmptyCatalog,
            result.Issues[0].Code);
    }

    [TestMethod]
    public void Build_BoundsMalformedRecordReportWithoutPartialSnapshot()
    {
        HorrorSearchDocument?[] documents = new HorrorSearchDocument?[40];
        documents[39] = CreateDocument("valid-film", "Valid Film");

        SearchIndexBuildRejected result =
            Assert.IsInstanceOfType<SearchIndexBuildRejected>(
                _builder.Build(documents!));

        Assert.AreEqual(40, result.ObservedDocumentCount);
        Assert.HasCount(SearchIndexBuilder.MaximumReportedIssues, result.Issues);
        Assert.IsTrue(result.HasAdditionalIssues);
        Assert.IsTrue(result.Issues.All(issue =>
            issue.Code == SearchIndexBuildIssueCode.NullDocument));
        CollectionAssert.AreEqual(
            Enumerable.Range(0, SearchIndexBuilder.MaximumReportedIssues).ToArray(),
            result.Issues.Select(issue => issue.DocumentIndex!.Value).ToArray());
    }

    [TestMethod]
    public void Build_HonorsPreCanceledRequest()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            _builder.Build(
                [CreateDocument("film", "Film")],
                source.Token));
    }

    [TestMethod]
    public void Store_PublishesOnlyCompleteSnapshots()
    {
        SearchIndexSnapshot first = BuildSnapshot(
            CreateDocument("first-film", "First Film"));
        SearchIndexSnapshot second = BuildSnapshot(
            CreateDocument("second-film", "Second Film"));
        var store = new SearchIndexStore();

        Assert.IsNull(store.Current);
        store.Publish(first);
        Assert.AreSame(first, store.Current);
        store.Publish(second);
        Assert.AreSame(second, store.Current);
    }

    private SearchIndexSnapshot BuildSnapshot(
        params HorrorSearchDocument[] documents)
    {
        SearchIndexBuildSucceeded result =
            Assert.IsInstanceOfType<SearchIndexBuildSucceeded>(
                _builder.Build(documents));
        return result.Snapshot;
    }

    private static HorrorSearchDocument CreateDocument(
        string candidateId,
        string title,
        IReadOnlyList<string>? tags = null,
        string? primaryLens = "Survival Horror",
        string? franchise = null,
        string? originalLanguage = null)
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate(
            title,
            tags,
            primaryLens,
            originalLanguage: originalLanguage) with
        {
            CandidateId = candidateId,
            Franchise = franchise,
        };

        return new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            SearchAvailabilityState.Unknown);
    }
}
