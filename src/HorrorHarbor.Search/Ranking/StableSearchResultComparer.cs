namespace HorrorHarbor.Search.Ranking;

internal sealed class StableSearchResultComparer : IComparer<RankedSearchCandidate>
{
    public static StableSearchResultComparer Instance { get; } = new();

    public int Compare(RankedSearchCandidate? left, RankedSearchCandidate? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        int result = GetTierRank(left.LexicalTier).CompareTo(
            GetTierRank(right.LexicalTier));
        if (result != 0)
        {
            return result;
        }

        result = right.HybridScore.CompareTo(left.HybridScore);
        if (result != 0)
        {
            return result;
        }

        result = right.LexicalScore.Total.CompareTo(left.LexicalScore.Total);
        if (result != 0)
        {
            return result;
        }

        result = right.AffinityScore.CompareTo(left.AffinityScore);
        if (result != 0)
        {
            return result;
        }

        result = right.QualityScore.CompareTo(left.QualityScore);
        if (result != 0)
        {
            return result;
        }

        result = right.DiscoveryScore.CompareTo(left.DiscoveryScore);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(
            left.NormalizedTitle,
            right.NormalizedTitle);
        if (result != 0)
        {
            return result;
        }

        result = CompareReleaseYears(left.ReleaseYear, right.ReleaseYear);
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(left.CandidateId, right.CandidateId);
    }

    private static int GetTierRank(SearchLexicalTier? tier) =>
        tier.HasValue ? (int)tier.Value : int.MaxValue;

    private static int CompareReleaseYears(int? left, int? right)
    {
        if (!left.HasValue)
        {
            return right.HasValue ? 1 : 0;
        }

        return !right.HasValue ? -1 : left.Value.CompareTo(right.Value);
    }
}
