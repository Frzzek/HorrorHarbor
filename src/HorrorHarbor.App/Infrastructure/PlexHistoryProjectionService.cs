using System.Globalization;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.ValueObjects;

namespace HorrorHarbor.App.Infrastructure;

public sealed record PlexHistoryProjection(
    IReadOnlyList<TitleCatalogItem> TasteSignals,
    HashSet<string> WatchedExclusionKeys);

public interface IPlexHistoryProjectionService
{
    PlexHistoryProjection Project(
        IReadOnlyList<RecommendationCandidate> candidates,
        IReadOnlyList<TitleCatalogItem> currentTasteSignals,
        IReadOnlyCollection<string> currentExclusionKeys,
        IReadOnlyCollection<string> plexExclusionKeys,
        IReadOnlyList<PlexHistoryItem> plexHistory);
}

public sealed class PlexHistoryProjectionService : IPlexHistoryProjectionService
{
    public PlexHistoryProjection Project(
        IReadOnlyList<RecommendationCandidate> candidates,
        IReadOnlyList<TitleCatalogItem> currentTasteSignals,
        IReadOnlyCollection<string> currentExclusionKeys,
        IReadOnlyCollection<string> plexExclusionKeys,
        IReadOnlyList<PlexHistoryItem> plexHistory)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(currentTasteSignals);
        ArgumentNullException.ThrowIfNull(currentExclusionKeys);
        ArgumentNullException.ThrowIfNull(plexExclusionKeys);
        ArgumentNullException.ThrowIfNull(plexHistory);

        Dictionary<string, RecommendationCandidate> candidatesByIdentity = candidates
            .OrderBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
            .GroupBy(
                static candidate => candidate.WatchExclusionKey,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        TitleCatalogItem[] learnedSignals = plexHistory
            .GroupBy(BuildHistoryIdentity, StringComparer.OrdinalIgnoreCase)
            .Where(group => candidatesByIdentity.ContainsKey(group.Key))
            .Select(group => CreateTasteSignal(
                candidatesByIdentity[group.Key],
                group))
            .OrderBy(static signal => signal.TitleId, StringComparer.Ordinal)
            .ToArray();

        TitleCatalogItem[] tasteSignals = currentTasteSignals
            .Where(static signal =>
                !signal.TitleId.StartsWith("plex:", StringComparison.OrdinalIgnoreCase))
            .Concat(learnedSignals)
            .ToArray();
        var exclusionKeys = new HashSet<string>(
            currentExclusionKeys,
            StringComparer.OrdinalIgnoreCase);
        exclusionKeys.UnionWith(plexExclusionKeys);

        return new PlexHistoryProjection(tasteSignals, exclusionKeys);
    }

    private static string BuildHistoryIdentity(PlexHistoryItem item) =>
        $"movie|{TitleNormalizer.NormalizeForKey(item.Title)}|" +
        (item.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "unknown");

    private static TitleCatalogItem CreateTasteSignal(
        RecommendationCandidate candidate,
        IEnumerable<PlexHistoryItem> history)
    {
        PlexHistoryItem[] ordered = [.. history
            .OrderByDescending(static item => item.ViewedAtUtc)
            .ThenBy(static item => item.RatingKey, StringComparer.Ordinal)
            .ThenBy(
                static item => item.ProviderIdentifier ?? string.Empty,
                StringComparer.Ordinal)];
        PlexHistoryItem mostRecent = ordered[0];
        return new TitleCatalogItem(
            "plex:" + candidate.CandidateId,
            "movie",
            candidate.Title,
            candidate.ReleaseYear,
            mostRecent.ProviderIdentifier ?? string.Empty,
            candidate.WatchExclusionKey,
            ordered.Length,
            Math.Min(4, ordered.Length),
            ordered.Min(static item => item.ViewedAtUtc),
            mostRecent.ViewedAtUtc,
            candidate.ScopeTier,
            candidate.PrimaryLens,
            candidate.SecondaryLens,
            "Imported",
            "Plex history",
            false);
    }
}
