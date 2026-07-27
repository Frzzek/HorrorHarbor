using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;

namespace HorrorHarbor.Recommendations.Engine;

public sealed class DeterministicRecommendationEngine : IRecommendationEngine
{
    private readonly RecommendationWeights _weights;

    public DeterministicRecommendationEngine()
        : this(new RecommendationWeights())
    {
    }

    public DeterministicRecommendationEngine(RecommendationWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        double[] values =
        {
            weights.PrimaryLens,
            weights.SecondaryLens,
            weights.Franchise,
            weights.ScareIntensity,
            weights.GoreIntensity,
            weights.DreadIntensity,
            weights.Slasher,
            weights.Extreme,
            weights.Discovery,
            weights.Quality,
            weights.Availability,
            weights.Feedback,
        };

        if (values.Any(static value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentException("Recommendation weights must be finite and non-negative.", nameof(weights));
        }

        if (!double.IsFinite(weights.Total) || Math.Abs(weights.Total - 100) > 0.0001)
        {
            throw new ArgumentException("Recommendation weights must total 100.", nameof(weights));
        }

        _weights = weights;
    }

    public IReadOnlyList<RecommendationResult> Rank(
        IEnumerable<RecommendationCandidate> candidates,
        TasteProfile profile,
        IReadOnlySet<string> watchedExclusionKeys,
        int count)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(watchedExclusionKeys);
        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Recommendation count must be between 1 and 100.");
        }

        RecommendationCandidate[] materialized = candidates.ToArray();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecommendationCandidate candidate in materialized)
        {
            RecommendationCandidateValidator.Validate(candidate, nameof(candidates));
            if (!ids.Add(candidate.CandidateId))
            {
                throw new ArgumentException($"Duplicate candidate ID '{candidate.CandidateId}' was supplied.", nameof(candidates));
            }
        }

        RecommendationResult[] ranked = materialized
            .Where(candidate =>
                !watchedExclusionKeys.Contains(candidate.WatchExclusionKey) &&
                !watchedExclusionKeys.Contains(candidate.TitleOnlyExclusionKey))
            .Select(candidate =>
            {
                RecommendationScoreBreakdown breakdown = Score(candidate, profile);
                return new RecommendationResult(
                    candidate,
                    Math.Round(breakdown.Total, 3),
                    ExplanationBuilder.Build(candidate, profile, breakdown),
                    breakdown);
            })
            .OrderByDescending(static result => result.Score)
            .ThenByDescending(static result => result.Candidate.QualityScore)
            .ThenByDescending(static result => result.Candidate.DiscoveryScore)
            .ThenBy(static result => result.Candidate.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return DiversitySelector.Select(ranked, Math.Min(count, ranked.Length));
    }

    private RecommendationScoreBreakdown Score(RecommendationCandidate candidate, TasteProfile profile)
    {
        double primary = Lookup(profile.LensWeights, candidate.PrimaryLens) * _weights.PrimaryLens;
        double secondary = Lookup(profile.LensWeights, candidate.SecondaryLens) * _weights.SecondaryLens;
        double taste = primary + secondary;
        double franchise = Lookup(profile.FranchiseWeights, candidate.Franchise) * _weights.Franchise;

        double scare = Similarity(profile.DesiredScareIntensity, candidate.ClampedScareIntensity) * _weights.ScareIntensity;
        double gore = Similarity(profile.DesiredGoreIntensity, candidate.ClampedGoreIntensity) * _weights.GoreIntensity;
        double dread = Similarity(profile.DesiredDreadIntensity, candidate.ClampedDreadIntensity) * _weights.DreadIntensity;
        double intensity = scare + gore + dread;

        double slasher = profile.PreferSlashers switch
        {
            null => _weights.Slasher * 0.50,
            true when candidate.IsSlasher => _weights.Slasher,
            false when !candidate.IsSlasher => _weights.Slasher,
            _ => 0,
        };

        double extreme = profile.PreferExtreme switch
        {
            true when candidate.IsExtreme => _weights.Extreme,
            false when !candidate.IsExtreme => _weights.Extreme,
            _ => 0,
        };

        double format = slasher + extreme;
        double discovery = Math.Clamp(candidate.DiscoveryScore, 0, 1) *
                           Math.Clamp(profile.NoveltyPreference, 0, 1) *
                           _weights.Discovery;
        double quality = Math.Clamp(candidate.QualityScore, 0, 1) * _weights.Quality;
        double availability = Math.Clamp(candidate.AvailabilityScore, 0, 1) * _weights.Availability;
        double feedback = Math.Clamp(candidate.UserFeedbackBoost, -1, 1) * _weights.Feedback;

        return new RecommendationScoreBreakdown(
            Math.Round(taste, 3),
            Math.Round(franchise, 3),
            Math.Round(intensity, 3),
            Math.Round(format, 3),
            Math.Round(discovery, 3),
            Math.Round(quality, 3),
            Math.Round(availability, 3),
            Math.Round(feedback, 3));
    }

    private static double Similarity(double desired, double actual)
    {
        double distance = Math.Abs(Math.Clamp(desired, 0, 5) - Math.Clamp(actual, 0, 5));
        return Math.Max(0, 1 - (distance / 5.0));
    }

    private static double Lookup(IReadOnlyDictionary<string, double> values, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return 0;
        }

        return values.TryGetValue(key, out double value) && double.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : 0;
    }
}
