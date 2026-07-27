using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class CatalogSearchServiceTests
{
    [TestMethod]
    public void Search_UsesAdvancedGrammarAcrossTheFullCatalog()
    {
        RecommendationCandidate[] candidates =
        [
            Candidate("one", "The Witch", 2015, "Folk Horror", ["folk", "occult"]),
            Candidate("two", "Witchboard", 1986, "Supernatural", ["occult"]),
            Candidate("three", "The Witches", 2020, "Dark Fantasy", ["witch"]),
        ];
        var service = new CatalogSearchService();
        service.PublishCatalog(candidates);

        CatalogSearchOutcome result = service.Search(
            new Dictionary<string, RecommendationFeedback>(),
            "witch year:>=2000 -lens:fantasy",
            20);

        Assert.IsNull(result.Diagnostic);
        Assert.AreEqual("search-success", result.StatusCode);
        Assert.AreEqual(1, result.TotalMatchingCount);
        Assert.HasCount(1, result.Results);
        Assert.AreEqual("one", result.Results[0].CandidateId);
    }

    [TestMethod]
    public void Search_ReturnsTypedDiagnosticForInvalidQuery()
    {
        var service = new CatalogSearchService();
        service.PublishCatalog(
            [Candidate("one", "The Witch", 2015, "Folk Horror", ["folk"])]);

        CatalogSearchOutcome result = service.Search(
            new Dictionary<string, RecommendationFeedback>(),
            "year:not-a-year",
            20);

        Assert.IsNotNull(result.Diagnostic);
        Assert.AreEqual("query-rejected", result.StatusCode);
        Assert.IsEmpty(result.Results);
    }

    private static RecommendationCandidate Candidate(
        string id,
        string title,
        int year,
        string lens,
        IReadOnlyList<string> tags) => new(
        id,
        title,
        year,
        "Core",
        lens,
        null,
        null,
        3,
        2,
        4,
        false,
        false,
        100,
        0.7,
        0.8,
        0.5,
        "Unknown",
        "#111111",
        tags);
}
