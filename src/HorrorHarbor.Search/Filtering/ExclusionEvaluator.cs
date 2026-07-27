using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.Search.Filtering;

internal static class ExclusionEvaluator
{
    public static int[] Apply(
        SearchIndexSnapshot snapshot,
        ReadOnlySpan<int> candidateIds,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (query.ExclusionClauseCount == 0)
        {
            return candidateIds.ToArray();
        }

        var result = new List<int>(candidateIds.Length);
        for (int index = 0; index < candidateIds.Length; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int documentId = candidateIds[index];
            if (!MatchesAnyExclusion(
                snapshot,
                documentId,
                query,
                userContext))
            {
                result.Add(documentId);
            }
        }

        return [.. result];
    }

    private static bool MatchesAnyExclusion(
        SearchIndexSnapshot snapshot,
        int documentId,
        ParsedSearchQuery query,
        SearchUserContext userContext)
    {
        foreach (SearchQueryClause clause in query.Clauses)
        {
            if (clause.Occurrence != SearchClauseOccurrence.Excluded)
            {
                continue;
            }

            bool matches = clause is SearchLexicalClause lexical
                ? LexicalClauseMatcher.Matches(
                    snapshot.GetDocumentData(documentId),
                    lexical)
                : HardFacetEvaluator.Matches(
                    snapshot.GetDocument(documentId),
                    clause,
                    userContext);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
