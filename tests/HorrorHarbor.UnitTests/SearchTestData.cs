using HorrorHarbor.Core.Models;

namespace HorrorHarbor.UnitTests;

internal static class SearchTestData
{
    public static RecommendationCandidate CreateCandidate(
        string title = "Frontier(s)",
        IReadOnlyList<string>? tags = null,
        string? primaryLens = "Survival Horror",
        string? originalTitle = null,
        string? originalLanguage = null) => new(
        CandidateId: "search-test-film",
        Title: title,
        ReleaseYear: 2007,
        ScopeTier: "core",
        PrimaryLens: primaryLens,
        SecondaryLens: "Body Horror",
        Franchise: null,
        ScareIntensity: 4,
        GoreIntensity: 3,
        DreadIntensity: 4,
        IsSlasher: false,
        IsExtreme: false,
        RuntimeMinutes: 108,
        DiscoveryScore: 0.8,
        QualityScore: 0.9,
        AvailabilityScore: 0,
        AvailabilityLabel: "US streaming availability requires live verification",
        PosterTone: "#301A24",
        Tags: tags ?? ["Body Horror", "Survival"],
        OriginalTitle: originalTitle,
        OriginalLanguage: originalLanguage);
}
