using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.App.ViewModels;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class DiscoveryDepthTests
{
    private static readonly string[] ExpectedContributionNames =
    [
        "Taste alignment",
        "Franchise affinity",
        "Intensity fit",
        "Format fit",
        "Discovery value",
        "Quality signal",
        "Availability confidence",
        "Your feedback",
    ];

    [TestMethod]
    public void Query_RejectsInvalidBounds()
    {
        RecommendationQueryService service = CreateService();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => service.Execute(CreateQuery(recommendationCount: 0)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => service.Execute(CreateQuery(recommendationCount: 101)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => service.Execute(CreateQuery(desiredScare: -0.1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => service.Execute(CreateQuery(desiredGore: 5.1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => service.Execute(CreateQuery(desiredDread: -0.1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => service.Execute(CreateQuery(novelty: 1.1)));
    }

    [TestMethod]
    public void Query_AppliesFeedbackAndExcludesRejectedCandidates()
    {
        RecommendationCandidate interested = Candidate("interested", "Interested Film");
        RecommendationCandidate notNow = Candidate("not-now", "Not Now Film");
        RecommendationCandidate disliked = Candidate("disliked", "Disliked Film");
        RecommendationCandidate watched = Candidate("watched", "Watched Film");
        CapturingEngine engine = new();
        RecommendationQueryService service = new(engine);
        Dictionary<string, RecommendationFeedback> feedback = new(StringComparer.OrdinalIgnoreCase)
        {
            [interested.CandidateId] = RecommendationFeedback.Interested,
            [notNow.CandidateId] = RecommendationFeedback.NotNow,
            [disliked.CandidateId] = RecommendationFeedback.Dislike,
            [watched.CandidateId] = RecommendationFeedback.Watched,
        };

        RecommendationQueryResult result = service.Execute(
            CreateQuery(
                candidates: [interested, notNow, disliked, watched],
                feedback: feedback));

        Assert.HasCount(2, result.Results);
        Assert.HasCount(2, engine.LastCandidates);
        Assert.AreEqual(0.75, engine.LastCandidates.Single(candidate => candidate.CandidateId == "interested").UserFeedbackBoost);
        Assert.AreEqual(-0.40, engine.LastCandidates.Single(candidate => candidate.CandidateId == "not-now").UserFeedbackBoost);
        Assert.IsFalse(engine.LastCandidates.Any(candidate => candidate.CandidateId == "disliked"));
        Assert.IsFalse(engine.LastCandidates.Any(candidate => candidate.CandidateId == "watched"));
    }

    [TestMethod]
    [DataRow("Moon", "moon-title")]
    [DataRow("folk", "tag-match")]
    [DataRow("cosmic", "lens-match")]
    public void Query_SearchesTitleTagsAndPrimaryLens(string searchText, string expectedId)
    {
        RecommendationCandidate[] candidates =
        [
            Candidate("moon-title", "Moon House"),
            Candidate("tag-match", "Harvest", tags: ["Folk Horror", "Ritual"]),
            Candidate("lens-match", "The Signal", primaryLens: "Cosmic Horror"),
            Candidate("other", "Basement"),
        ];

        RecommendationQueryResult result = CreateService().Execute(
            CreateQuery(candidates: candidates, searchText: searchText));

        Assert.HasCount(1, result.Results);
        Assert.AreEqual(expectedId, result.Results[0].Candidate.CandidateId);
    }

    [TestMethod]
    [DataRow("Newest first", "newer", "same-a", "same-b", "older")]
    [DataRow("Scariest first", "same-a", "same-b", "older", "newer")]
    [DataRow("Shortest first", "older", "same-a", "same-b", "newer")]
    public void Query_SortsWithStableDeterministicTies(
        string sort,
        string first,
        string second,
        string third,
        string fourth)
    {
        RecommendationCandidate[] candidates =
        [
            Candidate("same-b", "Same", year: 2020, scare: 5, runtime: 100),
            Candidate("newer", "Newer", year: 2024, scare: 1, runtime: null),
            Candidate("older", "Older", year: 1990, scare: 3, runtime: 80),
            Candidate("same-a", "Same", year: 2020, scare: 5, runtime: 100),
        ];

        RecommendationQueryResult result = CreateService().Execute(
            CreateQuery(candidates: candidates, selectedSort: sort));

        CollectionAssert.AreEqual(
            new[] { first, second, third, fourth },
            result.Results.Select(item => item.Candidate.CandidateId).ToArray());
    }

    [TestMethod]
    public void Query_ReportsWatchedExclusionsAndIsRepeatable()
    {
        RecommendationCandidate exact = Candidate("exact", "Exact Match", year: 2020);
        RecommendationCandidate titleOnly = Candidate("title-only", "Title Only", year: 2021);
        RecommendationCandidate visible = Candidate("visible", "Visible", year: 2022);
        HashSet<string> watchedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            exact.WatchExclusionKey,
            titleOnly.TitleOnlyExclusionKey,
        };
        RecommendationQuery query = CreateQuery(
            candidates: [visible, titleOnly, exact],
            watchedExclusionKeys: watchedKeys,
            selectedSort: "Newest first");
        RecommendationQueryService service = CreateService();

        RecommendationQueryResult first = service.Execute(query);
        RecommendationQueryResult second = service.Execute(query);

        Assert.AreEqual(2, first.WatchedExclusionCount);
        CollectionAssert.AreEqual(
            first.Results.Select(result => result.Candidate.CandidateId).ToArray(),
            second.Results.Select(result => result.Candidate.CandidateId).ToArray());
        CollectionAssert.AreEqual(
            first.Results.Select(result => result.Score).ToArray(),
            second.Results.Select(result => result.Score).ToArray());
    }

    [TestMethod]
    public void Details_ExposesAllContributionsAndDeterministicReasons()
    {
        RecommendationScoreBreakdown breakdown = new(
            Taste: 2,
            Franchise: 2,
            Intensity: 5,
            Format: 1,
            Discovery: 3,
            Quality: 4,
            Availability: 0.5,
            Feedback: -0.4);
        FilmDetailsViewModel details = Details(
            Candidate("details", "The Details"),
            breakdown,
            "Engine explanation.",
            RecommendationFeedback.NotNow);

        Assert.HasCount(8, details.ScoreContributions);
        CollectionAssert.AreEqual(
            ExpectedContributionNames,
            details.ScoreContributions.Select(contribution => contribution.Name).ToArray());
        Assert.AreEqual("+5.0", details.ScoreContributions[2].ValueLabel);
        Assert.AreEqual("-0.4", details.ScoreContributions[7].ValueLabel);
        StringAssert.StartsWith(details.RecommendationReasons[0], "Intensity fit added +5.0 points.");
        StringAssert.StartsWith(details.RecommendationReasons[1], "Quality signal added +4.0 points.");
        StringAssert.StartsWith(details.RecommendationReasons[2], "Discovery value added +3.0 points.");
        Assert.IsTrue(details.RecommendationReasons.Any(reason => reason.StartsWith("Your feedback applied -0.4 points.", StringComparison.Ordinal)));
        CollectionAssert.Contains(details.RecommendationReasons.ToArray(), "Engine explanation.");
        StringAssert.Contains(details.RecommendationReasons[^1], "calculated locally and is reproducible");
    }

    [TestMethod]
    public void Details_FormatsCompleteMetadataAndReusesCardState()
    {
        RecommendationCandidate candidate = Candidate(
            "metadata",
            "The Long Foreign Title",
            year: 2023,
            runtime: 117,
            primaryLens: "Folk Horror",
            secondaryLens: "Isolation",
            franchise: "Harbor Cycle",
            tags: ["Ritual", "Coastal", "Slow Burn"],
            originalTitle: "Le Port Sombre",
            originalLanguage: "fr");
        RecommendationCardViewModel card = Card(candidate, RecommendationFeedback.Interested);
        FilmDetailsViewModel details = new(card);

        Assert.AreSame(card, details.Card);
        Assert.AreSame(candidate, details.Candidate);
        Assert.AreEqual("2023  •  117 min", details.IdentityLine);
        Assert.AreEqual("Folk Horror  •  Isolation", details.LensLine);
        Assert.AreEqual("Franchise: Harbor Cycle", details.FranchiseLine);
        Assert.AreEqual("Original title: Le Port Sombre", details.OriginalTitleLine);
        Assert.AreEqual("Original language: FR", details.LanguageLine);
        Assert.AreEqual("Ritual  •  Coastal  •  Slow Burn", details.FullTagLine);
        Assert.AreEqual("Current feedback: Interested", details.FeedbackLine);
        Assert.AreEqual(card.PosterSource, details.ArtworkSource);
        Assert.AreEqual(card.PosterStatus, details.ArtworkStatus);
    }

    [TestMethod]
    public void Details_UsesHonestFallbacksForMissingMetadata()
    {
        RecommendationCandidate candidate = Candidate(
            "fallbacks",
            "Unknown Film",
            year: null,
            runtime: null,
            primaryLens: null,
            secondaryLens: null,
            franchise: null,
            tags: [],
            originalTitle: "Unknown Film",
            originalLanguage: null);

        FilmDetailsViewModel details = Details(candidate);

        Assert.AreEqual("Year unknown  •  Runtime unknown", details.IdentityLine);
        Assert.AreEqual("Horror lens not classified", details.LensLine);
        Assert.AreEqual("Standalone or franchise not listed", details.FranchiseLine);
        Assert.AreEqual("Original title not separately listed", details.OriginalTitleLine);
        Assert.AreEqual("Original language not listed", details.LanguageLine);
        Assert.AreEqual("No descriptive tags", details.FullTagLine);
    }

    [TestMethod]
    public void Mood_RoundTripsInvariantValuesAndClampsLoadedBounds()
    {
        DiscoveryMood original = new(4.5, 2.25, 3.75, 0.875, true, false, 18);

        string serialized = DiscoveryMoodService.Serialize(original);
        bool parsed = DiscoveryMoodService.TryDeserialize(serialized, out DiscoveryMood roundTrip);
        bool clamped = DiscoveryMoodService.TryDeserialize(
            "-1|8|3|2|False|True|99",
            out DiscoveryMood bounded);

        Assert.IsTrue(parsed);
        Assert.AreEqual(original, roundTrip);
        Assert.IsTrue(clamped);
        Assert.AreEqual(new DiscoveryMood(0, 5, 3, 1, false, true, 30), bounded);
    }

    [TestMethod]
    public void Mood_RejectsMalformedSettingsWithoutPartialState()
    {
        bool parsed = DiscoveryMoodService.TryDeserialize(
            "4|not-a-number|3|0.8|True|False|12",
            out DiscoveryMood mood);

        Assert.IsFalse(parsed);
        Assert.AreEqual(DiscoveryMoodService.CreateReset(), mood);
    }

    [TestMethod]
    public void Mood_SurpriseIsReproducibleWithSeededRandomSource()
    {
        DiscoveryMoodService first = new(new Random(1729));
        DiscoveryMoodService second = new(new Random(1729));

        DiscoveryMood firstMood = first.CreateSurprise();
        DiscoveryMood secondMood = second.CreateSurprise();

        Assert.AreEqual(firstMood, secondMood);
        Assert.IsGreaterThanOrEqualTo(5, firstMood.DesiredScare + firstMood.DesiredGore + firstMood.DesiredDread);
        Assert.AreEqual(0.92, firstMood.Novelty);
        Assert.AreEqual(12, firstMood.RecommendationCount);
    }

    private static RecommendationQueryService CreateService() => new(new CapturingEngine());

    private static RecommendationQuery CreateQuery(
        IReadOnlyList<RecommendationCandidate>? candidates = null,
        IReadOnlyDictionary<string, RecommendationFeedback>? feedback = null,
        IReadOnlySet<string>? watchedExclusionKeys = null,
        double desiredScare = 4,
        double desiredGore = 4,
        double desiredDread = 3,
        double novelty = 0.75,
        int recommendationCount = 12,
        string searchText = "",
        string selectedSort = "Best match") => new(
            candidates ?? [Candidate("default", "Default")],
            Array.Empty<TitleCatalogItem>(),
            feedback ?? new Dictionary<string, RecommendationFeedback>(StringComparer.OrdinalIgnoreCase),
            watchedExclusionKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            desiredScare,
            desiredGore,
            desiredDread,
            PreferSlashers: true,
            PreferExtreme: false,
            novelty,
            recommendationCount,
            searchText,
            selectedSort);

    private static RecommendationCandidate Candidate(
        string id,
        string title,
        int? year = 2000,
        int scare = 3,
        int? runtime = 100,
        string? primaryLens = "Supernatural",
        string? secondaryLens = null,
        string? franchise = null,
        IReadOnlyList<string>? tags = null,
        string? originalTitle = null,
        string? originalLanguage = "en") => new(
            id,
            title,
            year,
            "Core",
            primaryLens,
            secondaryLens,
            franchise,
            scare,
            GoreIntensity: 2,
            DreadIntensity: 4,
            IsSlasher: false,
            IsExtreme: false,
            runtime,
            DiscoveryScore: 0.7,
            QualityScore: 0.8,
            AvailabilityScore: 0.6,
            AvailabilityLabel: "Catalog availability only",
            PosterTone: "#111111",
            tags ?? ["Atmospheric"],
            UserFeedbackBoost: 0,
            originalTitle,
            originalLanguage);

    private static FilmDetailsViewModel Details(
        RecommendationCandidate candidate,
        RecommendationScoreBreakdown? breakdown = null,
        string explanation = "Deterministic explanation.",
        RecommendationFeedback feedback = RecommendationFeedback.None) =>
        new(Card(candidate, feedback, breakdown, explanation));

    private static RecommendationCardViewModel Card(
        RecommendationCandidate candidate,
        RecommendationFeedback feedback,
        RecommendationScoreBreakdown? breakdown = null,
        string explanation = "Deterministic explanation.")
    {
        RecommendationScoreBreakdown score = breakdown ?? new(
            Taste: 2,
            Franchise: 0,
            Intensity: 3,
            Format: 1,
            Discovery: 2,
            Quality: 2,
            Availability: 1,
            Feedback: 0);
        return new RecommendationCardViewModel(
            new RecommendationResult(candidate, score.Total, explanation, score),
            feedback);
    }

    private sealed class CapturingEngine : IRecommendationEngine
    {
        public IReadOnlyList<RecommendationCandidate> LastCandidates { get; private set; } =
            Array.Empty<RecommendationCandidate>();

        public IReadOnlyList<RecommendationResult> Rank(
            IEnumerable<RecommendationCandidate> candidates,
            TasteProfile profile,
            IReadOnlySet<string> watchedExclusionKeys,
            int count)
        {
            _ = profile;
            LastCandidates = candidates.ToArray();
            RecommendationScoreBreakdown breakdown = new(
                Taste: 1,
                Franchise: 1,
                Intensity: 1,
                Format: 1,
                Discovery: 1,
                Quality: 1,
                Availability: 1,
                Feedback: 0);
            return LastCandidates
                .Where(candidate =>
                    !watchedExclusionKeys.Contains(candidate.WatchExclusionKey) &&
                    !watchedExclusionKeys.Contains(candidate.TitleOnlyExclusionKey))
                .Select(candidate => new RecommendationResult(
                    candidate,
                    10,
                    $"Reason for {candidate.Title}",
                    breakdown with { Feedback = candidate.UserFeedbackBoost }))
                .Take(count)
                .ToArray();
        }
    }
}
