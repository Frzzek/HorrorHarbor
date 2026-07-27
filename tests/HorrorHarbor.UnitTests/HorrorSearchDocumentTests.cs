using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class HorrorSearchDocumentTests
{
    private static readonly SearchMetadataProvenance CuratedProvenance = new(
        SearchMetadataSourceKind.CuratedCatalog,
        "horrorharbor.catalog",
        "catalog-v1");

    private static readonly SearchMetadataProvenance ProviderProvenance = new(
        SearchMetadataSourceKind.OnlineProvider,
        "streaming-provider",
        "provider-v1");

    [TestMethod]
    public void Constructor_CopiesCandidateAndTagsDefensively()
    {
        var sourceTags = new List<string> { "Occult", "Found Footage" };
        RecommendationCandidate source = SearchTestData.CreateCandidate(tags: sourceTags);

        var document = new HorrorSearchDocument(
            source,
            "catalog-v1",
            SearchAvailabilityState.Unknown);

        sourceTags[0] = "Changed";
        sourceTags.Add("Injected");

        string[] expectedTags = ["Found Footage", "Occult"];
        Assert.AreNotSame(source, document.Candidate);
        Assert.AreNotSame(sourceTags, document.Candidate.Tags);
        CollectionAssert.AreEqual(expectedTags, document.Candidate.Tags.ToArray());
    }

    [TestMethod]
    public void Constructor_CollapsesNormalizedTagDuplicatesDeterministically()
    {
        string[] firstOrder = ["seance", "Found Footage", "Séance"];
        string[] secondOrder = ["Séance", "seance", "Found Footage"];

        var first = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(tags: firstOrder),
            "catalog-v1",
            SearchAvailabilityState.Unknown);
        var second = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(tags: secondOrder),
            "catalog-v1",
            SearchAvailabilityState.Unknown);

        string[] expected = ["Found Footage", "Séance"];
        CollectionAssert.AreEqual(expected, first.Candidate.Tags.ToArray());
        CollectionAssert.AreEqual(expected, second.Candidate.Tags.ToArray());
    }

    [TestMethod]
    public void Constructor_CopiesSortsAndCollapsesAliasesByNormalizedValue()
    {
        var aliases = new List<SearchAlias>
        {
            new("Frontières", ProviderProvenance),
            new("frontieres", CuratedProvenance),
            new("The Frontier", CuratedProvenance),
        };

        var document = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(),
            "catalog-v1",
            SearchAvailabilityState.Unknown,
            aliases);

        aliases.Clear();

        Assert.AreEqual(2, document.Aliases.Length);
        Assert.AreEqual("frontieres", document.Aliases[0].NormalizedValue);
        Assert.AreEqual(
            SearchMetadataSourceKind.CuratedCatalog,
            document.Aliases[0].Provenance.SourceKind);
        Assert.AreEqual("the frontier", document.Aliases[1].NormalizedValue);
    }

    [TestMethod]
    public void Constructor_DoesNotPromoteUnverifiedAvailabilityLabel()
    {
        var document = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(),
            "catalog-v1",
            SearchAvailabilityState.Unknown);

        Assert.AreEqual(SearchAvailabilityState.Unknown, document.AvailabilityState);
        Assert.IsTrue(document.AvailabilityFacts.IsEmpty);
        Assert.AreEqual(
            "US streaming availability requires live verification",
            document.Candidate.AvailabilityLabel);
    }

    [TestMethod]
    public void Constructor_RequiresFactsForVerifiedAvailability()
    {
        var fact = new SearchAvailabilityFact("Shudder", ProviderProvenance);
        var document = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(),
            "catalog-v1",
            SearchAvailabilityState.Verified,
            availabilityFacts: [fact]);

        Assert.AreEqual(SearchAvailabilityState.Verified, document.AvailabilityState);
        Assert.AreEqual(1, document.AvailabilityFacts.Length);
        Assert.AreEqual("shudder", document.AvailabilityFacts[0].NormalizedProviderValue);
    }

    [TestMethod]
    public void AvailabilityFact_RejectsCuratedCatalogProvenance() =>
        Assert.Throws<ArgumentException>(() =>
            new SearchAvailabilityFact("Shudder", CuratedProvenance));

    [TestMethod]
    public void Constructor_RejectsInconsistentAvailabilityState()
    {
        var fact = new SearchAvailabilityFact("Shudder", ProviderProvenance);

        Assert.Throws<ArgumentException>(() => new HorrorSearchDocument(
            SearchTestData.CreateCandidate(),
            "catalog-v1",
            SearchAvailabilityState.Unknown,
            availabilityFacts: [fact]));
        Assert.Throws<ArgumentException>(() => new HorrorSearchDocument(
            SearchTestData.CreateCandidate(),
            "catalog-v1",
            SearchAvailabilityState.Verified));
    }

    [TestMethod]
    public void Constructor_RejectsUnsafeIndexedCandidateText()
    {
        RecommendationCandidate source = SearchTestData.CreateCandidate(
            primaryLens: "cosmic\u202Ehorror");

        Assert.Throws<ArgumentException>(() => new HorrorSearchDocument(
            source,
            "catalog-v1",
            SearchAvailabilityState.Unknown));
    }

    [TestMethod]
    public void SearchAlias_RejectsMalformedUtf16()
    {
        string malformedAlias = "host\uD800found";

        Assert.Throws<ArgumentException>(
            () => new SearchAlias(malformedAlias, CuratedProvenance));
    }

    [TestMethod]
    public void Provenance_RejectsUrlShapedSourceIdentifiers() =>
        Assert.Throws<ArgumentException>(() => new SearchMetadataProvenance(
            SearchMetadataSourceKind.OnlineProvider,
            "https://provider.example",
            "provider-v1"));
}
