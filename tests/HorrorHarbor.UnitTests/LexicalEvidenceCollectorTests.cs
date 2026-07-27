using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Ranking;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class LexicalEvidenceCollectorTests
{
    private static readonly SearchMetadataProvenance AliasProvenance = new(
        SearchMetadataSourceKind.CuratedCatalog,
        "unit-test-catalog",
        "catalog-v1");

    private readonly SearchQueryParser _parser = new();

    [TestMethod]
    [DataRow(
        "the exorcist returns",
        SearchLexicalTier.ExactCanonicalTitle,
        SearchEvidenceCode.CanonicalTitleExact,
        false)]
    [DataRow(
        "el exorcista vuelve",
        SearchLexicalTier.ExactOriginalTitleOrAlias,
        SearchEvidenceCode.OriginalTitleExact,
        false)]
    [DataRow(
        "exorcist returns",
        SearchLexicalTier.CanonicalTitlePhraseOrAllTokens,
        SearchEvidenceCode.CanonicalTitleAllQueryTokens,
        false)]
    [DataRow(
        "exor",
        SearchLexicalTier.CanonicalTitlePrefixOrToken,
        SearchEvidenceCode.CanonicalTitlePrefix,
        false)]
    [DataRow(
        "tag:\"home invasion\"",
        SearchLexicalTier.ExactMetadataPhrase,
        SearchEvidenceCode.TagExact,
        false)]
    [DataRow(
        "home",
        SearchLexicalTier.ExactTokenMatch,
        SearchEvidenceCode.TagTokenMatch,
        false)]
    [DataRow(
        "exorcst",
        SearchLexicalTier.FuzzyTitleMatch,
        SearchEvidenceCode.FuzzyTitleEditDistance1,
        true)]
    [DataRow(
        "exorcixx",
        SearchLexicalTier.FuzzyTitleMatch,
        SearchEvidenceCode.FuzzyTitleEditDistance2,
        true)]
    [DataRow(
        "tag:psychologixal",
        SearchLexicalTier.FuzzyNonTitleMatch,
        SearchEvidenceCode.FuzzyNonTitleEditDistance1,
        true)]
    [DataRow(
        "tag:psychologicxx",
        SearchLexicalTier.FuzzyNonTitleMatch,
        SearchEvidenceCode.FuzzyNonTitleEditDistance2,
        true)]
    public void Collect_AssignsDocumentedTierAndEvidence(
        string queryText,
        SearchLexicalTier expectedTier,
        SearchEvidenceCode expectedEvidence,
        bool allowFuzzy)
    {
        LexicalEvidenceCollection result = Collect(
            CreateRichDocument(),
            queryText,
            allowFuzzy);

        Assert.AreEqual(expectedTier, result.Tier);
        AssertContains(result, expectedEvidence);
        AssertContains(result, SearchEvidenceCode.AllPositiveClausesMatched);
    }

    [TestMethod]
    public void Collect_PreservesPunctuationDeletionTitleOrigin()
    {
        LexicalEvidenceCollection result = Collect(
            CreateDocument("frontier-s", "Frontier(s)"),
            "frontiers",
            allowFuzzy: false);

        Assert.AreEqual(SearchLexicalTier.ExactCanonicalTitle, result.Tier);
        SearchEvidence evidence = AssertSingleEvidence(
            result,
            SearchEvidenceCode.CanonicalTitlePunctuationAlias);
        Assert.AreEqual(
            SearchTitleVariantOrigin.CanonicalPunctuationDeletion,
            evidence.TitleOrigin);
    }

    [TestMethod]
    public void Collect_PreservesRomanNumeralAliasOrigin()
    {
        LexicalEvidenceCollection result = Collect(
            CreateDocument("saw-vi", "Saw VI"),
            "saw 6",
            allowFuzzy: false);

        Assert.AreEqual(SearchLexicalTier.ExactOriginalTitleOrAlias, result.Tier);
        SearchEvidence evidence = AssertSingleEvidence(
            result,
            SearchEvidenceCode.RomanNumeralTitleAlias);
        Assert.AreEqual(
            SearchTitleVariantOrigin.RomanNumeralAlias,
            evidence.TitleOrigin);
    }

    [TestMethod]
    public void Collect_EmitsFacetExclusionAndAmbientStatusEvidence()
    {
        HorrorSearchDocument document = CreateRichDocument();
        var context = new SearchUserContext(
            new Dictionary<string, SearchLocalStatus>(StringComparer.Ordinal)
            {
                [document.CandidateId] = SearchLocalStatus.Watched,
            });

        LexicalEvidenceCollection result = Collect(
            document,
            "exorcist year:>=2000 -extreme:true",
            allowFuzzy: false,
            context: context);

        Assert.AreEqual(SearchLexicalTier.CanonicalTitlePrefixOrToken, result.Tier);
        AssertContains(result, SearchEvidenceCode.YearFilterSatisfied);
        AssertContains(result, SearchEvidenceCode.ExclusionSatisfied);
        AssertContains(result, SearchEvidenceCode.StatusWatched);
    }

    [TestMethod]
    public void Collect_FilterOnlyQueryHasNoLexicalTier()
    {
        LexicalEvidenceCollection result = Collect(
            CreateRichDocument(),
            "year:>=2000",
            allowFuzzy: false);

        Assert.IsNull(result.Tier);
        AssertContains(result, SearchEvidenceCode.YearFilterSatisfied);
        Assert.IsFalse(result.Evidence.Any(static item =>
            item.Code == SearchEvidenceCode.AllPositiveClausesMatched));
    }

    [TestMethod]
    public void Collect_RepeatedCallsProduceIdenticalEvidenceOrder()
    {
        HorrorSearchDocument document = CreateRichDocument();
        LexicalEvidenceCollection first = Collect(
            document,
            "exorcist returns year:>=2000 -extreme:true",
            allowFuzzy: false);

        for (int iteration = 0; iteration < 20; iteration++)
        {
            LexicalEvidenceCollection repeated = Collect(
                document,
                "exorcist returns year:>=2000 -extreme:true",
                allowFuzzy: false);
            CollectionAssert.AreEqual(
                first.Evidence.ToArray(),
                repeated.Evidence.ToArray());
        }
    }

    private LexicalEvidenceCollection Collect(
        HorrorSearchDocument document,
        string queryText,
        bool allowFuzzy,
        SearchUserContext? context = null)
    {
        SearchIndexSnapshot snapshot = BuildSnapshot(document);
        SearchQueryParsed parsed = Assert.IsInstanceOfType<SearchQueryParsed>(
            _parser.Parse(queryText));
        return LexicalEvidenceCollector.Collect(
            snapshot,
            documentId: 0,
            parsed.Query,
            context ?? new SearchUserContext(),
            allowFuzzy);
    }

    private static SearchIndexSnapshot BuildSnapshot(
        HorrorSearchDocument document)
    {
        SearchIndexBuildSucceeded result =
            Assert.IsInstanceOfType<SearchIndexBuildSucceeded>(
                new SearchIndexBuilder().Build([document]));
        return result.Snapshot;
    }

    private static HorrorSearchDocument CreateRichDocument()
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate(
            title: "The Exorcist Returns",
            tags: ["Home Invasion", "Psychological"],
            primaryLens: "Supernatural Horror",
            originalTitle: "El Exorcista Vuelve",
            originalLanguage: "Spanish") with
        {
            CandidateId = "exorcist-returns",
            Franchise = "The Exorcist",
            ReleaseYear = 2024,
        };

        return new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            SearchAvailabilityState.Unknown,
            aliases:
            [
                new SearchAlias("Demon Priest Returns", AliasProvenance),
            ]);
    }

    private static HorrorSearchDocument CreateDocument(
        string candidateId,
        string title)
    {
        RecommendationCandidate candidate = SearchTestData.CreateCandidate(title) with
        {
            CandidateId = candidateId,
        };
        return new HorrorSearchDocument(
            candidate,
            "catalog-v1",
            SearchAvailabilityState.Unknown);
    }

    private static void AssertContains(
        LexicalEvidenceCollection result,
        SearchEvidenceCode expectedCode) => Assert.IsTrue(
            result.Evidence.Any(item => item.Code == expectedCode),
            $"Expected evidence code '{expectedCode.ToStableCode()}'.");

    private static SearchEvidence AssertSingleEvidence(
        LexicalEvidenceCollection result,
        SearchEvidenceCode expectedCode)
    {
        SearchEvidence[] matches = result.Evidence
            .Where(item => item.Code == expectedCode)
            .ToArray();
        Assert.HasCount(1, matches);
        return matches[0];
    }
}
