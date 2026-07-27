using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Core.Abstractions;

public interface IRecommendationEngine
{
    IReadOnlyList<RecommendationResult> Rank(
        IEnumerable<RecommendationCandidate> candidates,
        TasteProfile profile,
        IReadOnlySet<string> watchedExclusionKeys,
        int count);
}
