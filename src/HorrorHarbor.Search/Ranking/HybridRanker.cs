using System.Collections.Immutable;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.Search.Ranking;

internal static class HybridRanker
{
    private const double LexicalWeight = 0.85;
    private const double AffinityWeight = 0.15;
    private const double UnitIntervalToPercentage = 100;

    public static ImmutableArray<RankedSearchCandidate> Rank(
        SearchIndexSnapshot snapshot,
        CandidateRetrievalResult retrieval,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(retrieval);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(userContext);

        var ranked = new RankedSearchCandidate[retrieval.DocumentIds.Length];
        for (int index = 0; index < retrieval.DocumentIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int documentId = retrieval.DocumentIds[index];
            SearchIndexDocumentData document = snapshot.GetDocumentData(documentId);
            bool allowFuzzy = !ContainsSorted(
                retrieval.ExactDocumentIds,
                documentId);
            LexicalEvidenceCollection evidence = LexicalEvidenceCollector.Collect(
                snapshot,
                documentId,
                query,
                userContext,
                allowFuzzy,
                cancellationToken);
            LexicalScoreBreakdown lexicalScore = LexicalScorer.Score(evidence);
            RecommendationCandidate candidate = document.Document.Candidate;
            double affinity = userContext.GetAffinityUnchecked(candidate.CandidateId);
            double hybrid = evidence.Tier.HasValue
                ? (lexicalScore.Total * LexicalWeight) +
                    (affinity * AffinityWeight)
                : affinity;

            ranked[index] = new RankedSearchCandidate(
                documentId,
                candidate.CandidateId,
                document.TitleVariants[0].Value,
                candidate.ReleaseYear,
                evidence.Tier,
                lexicalScore,
                affinity,
                hybrid,
                candidate.QualityScore * UnitIntervalToPercentage,
                candidate.DiscoveryScore * UnitIntervalToPercentage,
                evidence.Evidence);
        }

        Array.Sort(ranked, StableSearchResultComparer.Instance);
        return [.. ranked];
    }

    public static ImmutableArray<RankedSearchCandidate> RankWindow(
        SearchIndexSnapshot snapshot,
        CandidateRetrievalResult retrieval,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        int requestedResultCount,
        CancellationToken cancellationToken = default)
    {
        if (requestedResultCount is < 1 or > SearchGrammarLimits.MaximumLocalResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedResultCount),
                requestedResultCount,
                $"Requested local result count must be between 1 and {SearchGrammarLimits.MaximumLocalResults}.");
        }

        ImmutableArray<RankedSearchCandidate> ranked = Rank(
            snapshot,
            retrieval,
            query,
            userContext,
            cancellationToken);
        return ranked.Length <= requestedResultCount
            ? ranked
            : ranked.Slice(0, requestedResultCount);
    }

    private static bool ContainsSorted(
        ImmutableArray<int> values,
        int target)
    {
        int lower = 0;
        int upper = values.Length - 1;
        while (lower <= upper)
        {
            int middle = lower + ((upper - lower) / 2);
            int value = values[middle];
            if (value == target)
            {
                return true;
            }

            if (value < target)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle - 1;
            }
        }

        return false;
    }
}
