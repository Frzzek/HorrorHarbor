namespace HorrorHarbor.Tools.PosterAudit;

public static class EvaluationCorpusSelector
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Select(
        IReadOnlyList<AuditCandidate> candidates,
        int maximumCount = 100)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (maximumCount is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var reasons = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        AddGroups(
            candidates.Where(static candidate => LanguageMarket(candidate) is not null),
            reasons,
            static candidate => LanguageMarket(candidate)!,
            3,
            "language-market",
            maximumCount);
        AddDuplicateTitles(candidates, reasons, maximumCount);
        AddGroups(candidates, reasons, static candidate => candidate.PrimaryLens ?? "Unclassified", 4, "lens", maximumCount);
        AddGroups(candidates, reasons, static candidate => candidate.ReleaseYear is null
            ? "unknown-era"
            : $"{candidate.ReleaseYear.Value / 10 * 10}s", 3, "era", maximumCount);
        AddGroups(candidates.Where(static candidate => candidate.IsExtreme), reasons,
            static _ => "extreme", 8, "extreme", maximumCount);
        AddGroups(candidates.Where(static candidate => candidate.IsSlasher), reasons,
            static _ => "slasher", 8, "slasher", maximumCount);
        AddGroups(candidates.Where(static candidate => !string.IsNullOrWhiteSpace(candidate.Franchise)), reasons,
            static candidate => candidate.Franchise!, 2, "franchise", maximumCount);

        foreach (AuditCandidate candidate in candidates
                     .OrderBy(static candidate => candidate.CandidateId, StringComparer.Ordinal))
        {
            if (reasons.Count >= maximumCount)
            {
                break;
            }

            Add(reasons, candidate.CandidateId, "deterministic-fill", maximumCount);
        }

        return reasons
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(maximumCount)
            .ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AddGroups(
        IEnumerable<AuditCandidate> candidates,
        IDictionary<string, HashSet<string>> reasons,
        Func<AuditCandidate, string> groupKey,
        int perGroup,
        string reasonPrefix,
        int maximumCount)
    {
        foreach (IGrouping<string, AuditCandidate> group in candidates
                     .GroupBy(groupKey, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (AuditCandidate candidate in group
                         .OrderBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
                         .Take(perGroup))
            {
                Add(reasons, candidate.CandidateId, $"{reasonPrefix}:{group.Key}", maximumCount);
            }
        }
    }

    private static void AddDuplicateTitles(
        IReadOnlyList<AuditCandidate> candidates,
        IDictionary<string, HashSet<string>> reasons,
        int maximumCount)
    {
        IEnumerable<AuditCandidate> duplicateTitles = candidates
            .GroupBy(static candidate => candidate.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(static group => group)
            .OrderBy(static candidate => candidate.CandidateId, StringComparer.Ordinal);
        foreach (AuditCandidate candidate in duplicateTitles)
        {
            Add(reasons, candidate.CandidateId, "duplicate-title", maximumCount);
        }
    }

    private static void Add(
        IDictionary<string, HashSet<string>> reasons,
        string id,
        string reason,
        int maximumCount)
    {
        if (!reasons.TryGetValue(id, out HashSet<string>? values))
        {
            if (reasons.Count >= maximumCount)
            {
                return;
            }

            values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            reasons[id] = values;
        }

        values.Add(reason);
    }

    private static string? LanguageMarket(AuditCandidate candidate) => candidate.Tags
        .Select(static tag => tag.Trim())
        .FirstOrDefault(static tag => tag.ToLowerInvariant() is
            "french extremity" or
            "indonesian horror" or
            "japanese horror" or
            "korean horror" or
            "norwegian horror" or
            "spanish horror" or
            "turkish horror");
}
