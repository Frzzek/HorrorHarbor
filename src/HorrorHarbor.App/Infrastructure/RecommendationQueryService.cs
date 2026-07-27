using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Recommendations.Engine;

namespace HorrorHarbor.App.Infrastructure;

public sealed record RecommendationQuery(
    IReadOnlyList<RecommendationCandidate> Candidates,
    IReadOnlyList<TitleCatalogItem> TasteSource,
    IReadOnlyDictionary<string, RecommendationFeedback> Feedback,
    IReadOnlySet<string> WatchedExclusionKeys,
    double DesiredScare,
    double DesiredGore,
    double DesiredDread,
    bool PreferSlashers,
    bool PreferExtreme,
    double Novelty,
    int RecommendationCount,
    string SearchText,
    string SelectedSort);

public sealed record RecommendationQueryResult(
    IReadOnlyList<RecommendationResult> Results,
    int WatchedExclusionCount);

public sealed class RecommendationQueryService(IRecommendationEngine recommendationEngine)
{
    public RecommendationQueryResult Execute(RecommendationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);

        TasteProfile profile = TasteProfileBuilder.Build(
            query.TasteSource,
            UserTasteSeed.HorrorHarborDefault,
            query.DesiredScare,
            query.DesiredGore,
            query.DesiredDread,
            query.PreferSlashers,
            query.PreferExtreme,
            query.Novelty);

        HashSet<string> excludedCandidateIds = query.Feedback
            .Where(static pair =>
                pair.Value is RecommendationFeedback.Dislike or RecommendationFeedback.Watched)
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RecommendationCandidate[] candidates = query.Candidates
            .Where(candidate => !excludedCandidateIds.Contains(candidate.CandidateId))
            .Select(candidate => candidate with
            {
                UserFeedbackBoost = FeedbackBoost(query.Feedback, candidate.CandidateId),
            })
            .ToArray();

        IReadOnlyList<RecommendationResult> ranked = recommendationEngine.Rank(
            candidates,
            profile,
            query.WatchedExclusionKeys,
            Math.Max(query.RecommendationCount, 30));

        IEnumerable<RecommendationResult> visible = ApplySearch(ranked, query.SearchText);
        visible = ApplySort(visible, query.SelectedSort);

        RecommendationResult[] results = visible
            .Take(query.RecommendationCount)
            .ToArray();
        int watchedExclusionCount = query.Candidates.Count(candidate =>
            query.WatchedExclusionKeys.Contains(candidate.WatchExclusionKey) ||
            query.WatchedExclusionKeys.Contains(candidate.TitleOnlyExclusionKey));

        return new RecommendationQueryResult(results, watchedExclusionCount);
    }

    private static double FeedbackBoost(
        IReadOnlyDictionary<string, RecommendationFeedback> feedback,
        string candidateId)
    {
        feedback.TryGetValue(candidateId, out RecommendationFeedback value);
        return value switch
        {
            RecommendationFeedback.Interested => 0.75,
            RecommendationFeedback.NotNow => -0.40,
            _ => 0,
        };
    }

    private static IEnumerable<RecommendationResult> ApplySearch(
        IEnumerable<RecommendationResult> results,
        string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return results;
        }

        return results.Where(result =>
            result.Candidate.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            result.Candidate.Tags.Any(tag =>
                tag.Contains(searchText, StringComparison.OrdinalIgnoreCase)) ||
            (result.Candidate.PrimaryLens?.Contains(
                searchText,
                StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static IEnumerable<RecommendationResult> ApplySort(
        IEnumerable<RecommendationResult> results,
        string selectedSort) => selectedSort switch
        {
            "Newest first" => results
                .OrderByDescending(static result => result.Candidate.ReleaseYear ?? int.MinValue)
                .ThenByDescending(static result => result.Score)
                .ThenBy(static result => result.Candidate.Title, StringComparer.Ordinal)
                .ThenBy(static result => result.Candidate.CandidateId, StringComparer.Ordinal),
            "Scariest first" => results
                .OrderByDescending(static result => result.Candidate.ScareIntensity)
                .ThenByDescending(static result => result.Score)
                .ThenBy(static result => result.Candidate.Title, StringComparer.Ordinal)
                .ThenBy(static result => result.Candidate.CandidateId, StringComparer.Ordinal),
            "Shortest first" => results
                .OrderBy(static result => result.Candidate.RuntimeMinutes ?? int.MaxValue)
                .ThenByDescending(static result => result.Score)
                .ThenBy(static result => result.Candidate.Title, StringComparer.Ordinal)
                .ThenBy(static result => result.Candidate.CandidateId, StringComparer.Ordinal),
            _ => results,
        };

    private static void Validate(RecommendationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query.Candidates);
        ArgumentNullException.ThrowIfNull(query.TasteSource);
        ArgumentNullException.ThrowIfNull(query.Feedback);
        ArgumentNullException.ThrowIfNull(query.WatchedExclusionKeys);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.RecommendationCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.RecommendationCount, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.DesiredScare, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.DesiredScare, 5);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.DesiredGore, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.DesiredGore, 5);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.DesiredDread, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.DesiredDread, 5);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Novelty, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Novelty, 1);
    }
}
