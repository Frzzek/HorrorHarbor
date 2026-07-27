using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Filtering;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal sealed class CandidateRetrievalResult
{
    public CandidateRetrievalResult(
        ImmutableArray<int> documentIds,
        ImmutableArray<int> exactDocumentIds,
        bool fuzzyFallbackAttempted)
    {
        if (documentIds.IsDefault)
        {
            throw new ArgumentException(
                "Retrieved document IDs must be initialized.",
                nameof(documentIds));
        }

        if (exactDocumentIds.IsDefault)
        {
            throw new ArgumentException(
                "Exact document IDs must be initialized.",
                nameof(exactDocumentIds));
        }

        DocumentIds = documentIds;
        ExactDocumentIds = exactDocumentIds;
        FuzzyFallbackAttempted = fuzzyFallbackAttempted;
    }

    public ImmutableArray<int> DocumentIds { get; }

    public ImmutableArray<int> ExactDocumentIds { get; }

    public bool FuzzyFallbackAttempted { get; }
}

internal static class CandidateRetriever
{
    public static CandidateRetrievalResult Retrieve(
        SearchIndexSnapshot snapshot,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        int requestedResultCount,
        CancellationToken cancellationToken = default)
    {
        ValidateRequestedResultCount(requestedResultCount);

        CandidateRetrievalResult exact = RetrieveExact(
            snapshot,
            query,
            userContext,
            cancellationToken);
        if (exact.DocumentIds.Length >= requestedResultCount ||
            query.FuzzyEligibleTermCount == 0)
        {
            return exact;
        }

        cancellationToken.ThrowIfCancellationRequested();
        int[] fuzzyDocumentIds = FuzzyCandidateRetriever.Retrieve(
            snapshot,
            query,
            userContext,
            cancellationToken);
        int[] combinedDocumentIds = PostingListOperations.Union(
            exact.DocumentIds.AsSpan(),
            fuzzyDocumentIds);

        return new CandidateRetrievalResult(
            [.. combinedDocumentIds],
            exact.ExactDocumentIds,
            fuzzyFallbackAttempted: true);
    }

    public static CandidateRetrievalResult RetrieveExact(
        SearchIndexSnapshot snapshot,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(userContext);

        int[] candidates = RetrievePositiveLexicalCandidates(
            snapshot,
            query,
            cancellationToken);
        candidates = HardFacetEvaluator.Apply(
            snapshot,
            candidates,
            query,
            userContext,
            cancellationToken);
        candidates = ExclusionEvaluator.Apply(
            snapshot,
            candidates,
            query,
            userContext,
            cancellationToken);

        ImmutableArray<int> documentIds = [.. candidates];
        return new CandidateRetrievalResult(
            documentIds,
            documentIds,
            fuzzyFallbackAttempted: false);
    }

    private static int[] RetrievePositiveLexicalCandidates(
        SearchIndexSnapshot snapshot,
        ParsedSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (query.PositiveLexicalClauseCount == 0)
        {
            return snapshot.GetAllDocumentIds().ToArray();
        }

        var clauseCandidates = new List<ClauseCandidateSet>(
            query.PositiveLexicalClauseCount);
        int clauseIndex = 0;
        foreach (SearchQueryClause queryClause in query.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queryClause is not SearchLexicalClause lexical ||
                lexical.Occurrence != SearchClauseOccurrence.Positive)
            {
                clauseIndex++;
                continue;
            }

            int[] candidates = ExactLexicalRetriever.RetrieveClauseCandidates(
                snapshot,
                lexical,
                cancellationToken);
            clauseCandidates.Add(new ClauseCandidateSet(clauseIndex, candidates));
            clauseIndex++;
        }

        clauseCandidates.Sort(static (left, right) =>
        {
            int result = left.DocumentIds.Length.CompareTo(right.DocumentIds.Length);
            return result != 0
                ? result
                : left.ClauseIndex.CompareTo(right.ClauseIndex);
        });

        int[] resultCandidates = clauseCandidates[0].DocumentIds;
        for (int index = 1; index < clauseCandidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            resultCandidates = PostingListOperations.Intersect(
                resultCandidates,
                clauseCandidates[index].DocumentIds);
            if (resultCandidates.Length == 0)
            {
                break;
            }
        }

        return resultCandidates;
    }

    private sealed record ClauseCandidateSet(
        int ClauseIndex,
        int[] DocumentIds);

    private static void ValidateRequestedResultCount(int requestedResultCount)
    {
        if (requestedResultCount is < 1 or > SearchGrammarLimits.MaximumLocalResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedResultCount),
                requestedResultCount,
                $"Requested result count must be from 1 through {SearchGrammarLimits.MaximumLocalResults}.");
        }
    }
}
