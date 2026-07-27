using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Models;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class PlexHistoryProjectionServiceTests
{
    private readonly PlexHistoryProjectionService _service = new();

    [TestMethod]
    public void Project_IsDeterministicIdempotentAndDoesNotMutateInputs()
    {
        RecommendationCandidate candidate = CreateCandidate();
        TitleCatalogItem curated = CreateCuratedSignal();
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "movie|existing|2000",
        };
        PlexHistoryItem[] history =
        [
            CreateHistory("z-key", "provider-z"),
            CreateHistory("a-key", "provider-a"),
        ];

        PlexHistoryProjection first = _service.Project(
            [candidate],
            [curated],
            exclusions,
            [candidate.WatchExclusionKey],
            history);
        PlexHistoryProjection second = _service.Project(
            [candidate],
            first.TasteSignals,
            first.WatchedExclusionKeys,
            [candidate.WatchExclusionKey],
            [.. history.Reverse()]);

        Assert.HasCount(1, exclusions);
        Assert.HasCount(2, second.TasteSignals);
        Assert.AreEqual("curated", second.TasteSignals[0].TitleId);
        Assert.AreEqual("plex:test-film", second.TasteSignals[1].TitleId);
        Assert.AreEqual("provider-a", second.TasteSignals[1].PlexGuid);
        Assert.AreEqual(2, second.TasteSignals[1].WeightedTasteEventCount);
        CollectionAssert.AreEquivalent(
            first.WatchedExclusionKeys.ToArray(),
            second.WatchedExclusionKeys.ToArray());
    }

    private static RecommendationCandidate CreateCandidate() => new(
        "test-film",
        "Test Film",
        2020,
        "core",
        "Cosmic Horror",
        null,
        null,
        3,
        2,
        4,
        false,
        false,
        90,
        0.5,
        0.8,
        0,
        "Unknown",
        "cold",
        ["Cosmic"]);

    private static PlexHistoryItem CreateHistory(
        string ratingKey,
        string providerIdentifier) => new(
        ratingKey,
        "movie",
        "Test Film",
        2020,
        providerIdentifier,
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        null,
        null);

    private static TitleCatalogItem CreateCuratedSignal() => new(
        "curated",
        "movie",
        "Curated Film",
        2010,
        string.Empty,
        "movie|curated film|2010",
        1,
        1,
        DateTime.UnixEpoch,
        DateTime.UnixEpoch,
        "core",
        null,
        null,
        "Curated",
        "Catalog",
        false);
}
