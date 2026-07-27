using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Recommendations.Engine;

internal static class DiversitySelector
{
    public static IReadOnlyList<RecommendationResult> Select(
        IEnumerable<RecommendationResult> ranked,
        int count)
    {
        ArgumentNullException.ThrowIfNull(ranked);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        RecommendationResult[] source = ranked.ToArray();
        var selected = new List<RecommendationResult>(Math.Min(count, source.Length));
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lensCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var franchiseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (RecommendationResult result in source)
        {
            if (selected.Count >= count)
            {
                break;
            }

            string lens = result.Candidate.PrimaryLens ?? "Unclassified";
            string? franchise = result.Candidate.Franchise;
            lensCounts.TryGetValue(lens, out int lensCount);
            int franchiseCount = 0;
            if (!string.IsNullOrWhiteSpace(franchise))
            {
                franchiseCounts.TryGetValue(franchise, out franchiseCount);
            }

            int lensLimit = count <= 5 ? 2 : Math.Max(2, count / 4);
            int franchiseLimit = count <= 10 ? 1 : 2;
            if (lensCount >= lensLimit ||
                (!string.IsNullOrWhiteSpace(franchise) && franchiseCount >= franchiseLimit))
            {
                continue;
            }

            selected.Add(result);
            selectedIds.Add(result.Candidate.CandidateId);
            lensCounts[lens] = lensCount + 1;
            if (!string.IsNullOrWhiteSpace(franchise))
            {
                franchiseCounts[franchise] = franchiseCount + 1;
            }
        }

        if (selected.Count < count)
        {
            foreach (RecommendationResult result in source)
            {
                if (selected.Count >= count)
                {
                    break;
                }

                if (selectedIds.Add(result.Candidate.CandidateId))
                {
                    selected.Add(result);
                }
            }
        }

        return selected.AsReadOnly();
    }
}
