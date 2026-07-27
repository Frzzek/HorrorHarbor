namespace HorrorHarbor.Core.Models;

public sealed record RecommendationResult(
    RecommendationCandidate Candidate,
    double Score,
    string Explanation,
    RecommendationScoreBreakdown Breakdown);
