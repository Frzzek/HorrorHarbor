using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Recommendations.Engine;

internal static class ExplanationBuilder
{
    public static string Build(
        RecommendationCandidate candidate,
        TasteProfile profile,
        RecommendationScoreBreakdown breakdown)
    {
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(candidate.PrimaryLens))
        {
            var weight = Lookup(profile.LensWeights, candidate.PrimaryLens);
            if (weight >= 0.75)
            {
                reasons.Add($"strong {candidate.PrimaryLens} match");
            }
            else if (weight >= 0.40)
            {
                reasons.Add($"solid {candidate.PrimaryLens} match");
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.Franchise) &&
            Lookup(profile.FranchiseWeights, candidate.Franchise) >= 0.50)
        {
            reasons.Add($"connects to the {candidate.Franchise} signal");
        }

        if (Math.Abs(candidate.ClampedScareIntensity - profile.DesiredScareIntensity) <= 1 &&
            Math.Abs(candidate.ClampedGoreIntensity - profile.DesiredGoreIntensity) <= 1)
        {
            reasons.Add("lands near the requested scare and gore level");
        }

        if (profile.PreferSlashers is true && candidate.IsSlasher)
        {
            reasons.Add("delivers the requested slasher energy");
        }

        if (profile.PreferExtreme && candidate.IsExtreme)
        {
            reasons.Add("pushes into extreme territory");
        }

        if (candidate.DiscoveryScore >= 0.80)
        {
            reasons.Add("has high hidden-gem value");
        }

        if (candidate.QualityScore >= 0.85)
        {
            reasons.Add("has high quality confidence");
        }

        if (candidate.UserFeedbackBoost > 0)
        {
            reasons.Add("was previously marked interesting");
        }

        return reasons.Count == 0
            ? $"Balanced match. {breakdown.Summary}."
            : string.Join("; ", reasons) + ".";
    }

    private static double Lookup(IReadOnlyDictionary<string, double> values, string key) =>
        values.TryGetValue(key, out var value) ? Math.Clamp(value, 0, 1) : 0;
}
