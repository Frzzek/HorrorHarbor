using System.Globalization;
using HorrorHarbor.Core.Models;

namespace HorrorHarbor.App.ViewModels;

public sealed record ScoreContributionViewModel(
    string Name,
    double Value,
    string ValueLabel,
    string Explanation);

public sealed class FilmDetailsViewModel
{
    public FilmDetailsViewModel(RecommendationCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        ScoreContributions = CreateContributions(card.Result.Breakdown);
        RecommendationReasons = CreateReasons(ScoreContributions, card.Explanation);
    }

    public RecommendationCardViewModel Card { get; }
    public RecommendationCandidate Candidate => Card.Candidate;
    public string Title => Candidate.Title;
    public string IdentityLine => $"{Candidate.YearLabel}  •  {Candidate.RuntimeLabel}";
    public string LensLine => string.IsNullOrWhiteSpace(Candidate.SecondaryLens)
        ? Candidate.PrimaryLens ?? "Horror lens not classified"
        : $"{Candidate.PrimaryLens}  •  {Candidate.SecondaryLens}";
    public string FranchiseLine => string.IsNullOrWhiteSpace(Candidate.Franchise)
        ? "Standalone or franchise not listed"
        : $"Franchise: {Candidate.Franchise}";
    public string OriginalTitleLine => string.IsNullOrWhiteSpace(Candidate.OriginalTitle) ||
                                       string.Equals(
                                           Candidate.OriginalTitle,
                                           Candidate.Title,
                                           StringComparison.OrdinalIgnoreCase)
        ? "Original title not separately listed"
        : $"Original title: {Candidate.OriginalTitle}";
    public string LanguageLine => string.IsNullOrWhiteSpace(Candidate.OriginalLanguage)
        ? "Original language not listed"
        : $"Original language: {Candidate.OriginalLanguage.ToUpperInvariant()}";
    public string IntensityLine => Candidate.IntensityLabel;
    public string FullTagLine => Candidate.Tags.Count == 0
        ? "No descriptive tags"
        : string.Join("  •  ", Candidate.Tags);
    public string AvailabilityLine => Candidate.AvailabilityLabel;
    public string ScoreLabel => $"{Card.Score.ToString("0.0", CultureInfo.InvariantCulture)} MATCH";
    public string ScopeLine => $"Catalog scope: {Candidate.ScopeTier}";
    public string FeedbackLine => $"Current feedback: {Card.FeedbackLabel}";
    public string ArtworkSource => Card.PosterSource;
    public string ArtworkStatus => Card.PosterStatus;
    public IReadOnlyList<ScoreContributionViewModel> ScoreContributions { get; }
    public IReadOnlyList<string> RecommendationReasons { get; }

    private static ScoreContributionViewModel[] CreateContributions(
        RecommendationScoreBreakdown breakdown) =>
    [
        Contribution(
            "Taste alignment",
            breakdown.Taste,
            "Match with lenses and patterns in your verified watch profile."),
        Contribution(
            "Franchise affinity",
            breakdown.Franchise,
            "Preference signal from related franchise viewing."),
        Contribution(
            "Intensity fit",
            breakdown.Intensity,
            "Fit with tonight's requested scare, gore, and dread levels."),
        Contribution(
            "Format fit",
            breakdown.Format,
            "Fit with preferred horror format and slasher settings."),
        Contribution(
            "Discovery value",
            breakdown.Discovery,
            "Novelty and hidden-gem value relative to familiar choices."),
        Contribution(
            "Quality signal",
            breakdown.Quality,
            "Curated quality confidence from the offline catalog."),
        Contribution(
            "Availability confidence",
            breakdown.Availability,
            "Confidence that availability information is useful, not a live streaming guarantee."),
        Contribution(
            "Your feedback",
            breakdown.Feedback,
            "Bounded effect from Save for later or Not tonight feedback."),
    ];

    private static ScoreContributionViewModel Contribution(
        string name,
        double value,
        string explanation) => new(
            name,
            value,
            value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture),
            explanation);

    private static string[] CreateReasons(
        IReadOnlyList<ScoreContributionViewModel> contributions,
        string explanation)
    {
        List<string> reasons = contributions
            .Where(static contribution => contribution.Value > 0)
            .OrderByDescending(static contribution => contribution.Value)
            .ThenBy(static contribution => contribution.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(static contribution =>
                $"{contribution.Name} added {contribution.ValueLabel} points. {contribution.Explanation}")
            .ToList();

        ScoreContributionViewModel? negativeFeedback = contributions.FirstOrDefault(
            static contribution => contribution.Name == "Your feedback" && contribution.Value < 0);
        if (negativeFeedback is not null)
        {
            reasons.Add(
                $"Your feedback applied {negativeFeedback.ValueLabel} points. {negativeFeedback.Explanation}");
        }

        if (!string.IsNullOrWhiteSpace(explanation))
        {
            reasons.Add(explanation);
        }

        reasons.Add("The score is calculated locally and is reproducible from the same profile, mood, feedback, and catalog snapshot.");
        return reasons.ToArray();
    }
}
