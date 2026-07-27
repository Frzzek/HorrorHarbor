using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal static class ExactLexicalRetriever
{
    public static int[] RetrieveClauseCandidates(
        SearchIndexSnapshot snapshot,
        SearchLexicalClause clause,
        CancellationToken cancellationToken)
    {
        int[] candidates = GenerateClauseCandidates(snapshot, clause);
        return VerifyClauseCandidates(
            snapshot,
            candidates,
            clause,
            cancellationToken);
    }

    public static int[] RetrieveTokenCandidates(
        SearchIndexSnapshot snapshot,
        SearchField field,
        string token) => field switch
        {
            SearchField.Any => UnionFields(
                RetrievePrefixCandidates(
                    token,
                    snapshot.GetTitleVocabulary(),
                    snapshot.GetTitleTokenPostings),
                RetrievePrefixCandidates(
                    token,
                    snapshot.GetTagVocabulary(),
                    snapshot.GetTagTokenPostings),
                RetrievePrefixCandidates(
                    token,
                    snapshot.GetLensVocabulary(),
                    snapshot.GetLensTokenPostings),
                RetrievePrefixCandidates(
                    token,
                    snapshot.GetFranchiseVocabulary(),
                    snapshot.GetFranchiseTokenPostings)),
            SearchField.Title => RetrievePrefixCandidates(
                token,
                snapshot.GetTitleVocabulary(),
                snapshot.GetTitleTokenPostings),
            SearchField.Tag => RetrievePrefixCandidates(
                token,
                snapshot.GetTagVocabulary(),
                snapshot.GetTagTokenPostings),
            SearchField.Lens => RetrievePrefixCandidates(
                token,
                snapshot.GetLensVocabulary(),
                snapshot.GetLensTokenPostings),
            SearchField.Franchise => RetrievePrefixCandidates(
                token,
                snapshot.GetFranchiseVocabulary(),
                snapshot.GetFranchiseTokenPostings),
            SearchField.Language => RetrievePrefixCandidates(
                token,
                snapshot.GetOriginalLanguageVocabulary(),
                snapshot.GetOriginalLanguageTokenPostings),
            _ => throw new InvalidOperationException(
                "Token retrieval received a non-lexical field."),
        };

    public static int[] RetrieveIndexedTermCandidates(
        SearchIndexSnapshot snapshot,
        SearchField field,
        string indexedTerm) => field switch
        {
            SearchField.Any => UnionFields(
                snapshot.GetTitleTokenPostings(indexedTerm).ToArray(),
                snapshot.GetTagTokenPostings(indexedTerm).ToArray(),
                snapshot.GetLensTokenPostings(indexedTerm).ToArray(),
                snapshot.GetFranchiseTokenPostings(indexedTerm).ToArray()),
            SearchField.Title =>
                snapshot.GetTitleTokenPostings(indexedTerm).ToArray(),
            SearchField.Tag =>
                snapshot.GetTagTokenPostings(indexedTerm).ToArray(),
            SearchField.Lens =>
                snapshot.GetLensTokenPostings(indexedTerm).ToArray(),
            SearchField.Franchise =>
                snapshot.GetFranchiseTokenPostings(indexedTerm).ToArray(),
            SearchField.Language => snapshot
                .GetOriginalLanguageTokenPostings(indexedTerm)
                .ToArray(),
            _ => throw new InvalidOperationException(
                "Indexed-term retrieval received a non-lexical field."),
        };

    private static int[] GenerateClauseCandidates(
        SearchIndexSnapshot snapshot,
        SearchLexicalClause clause)
    {
        if (clause.Field == SearchField.Tag &&
            clause.Mode == SearchLexicalMode.Phrase)
        {
            return snapshot
                .GetExactTagPostings(clause.NormalizedValue)
                .ToArray();
        }

        int[]? candidates = null;
        foreach (string token in clause.Tokens)
        {
            int[] tokenCandidates = RetrieveTokenCandidates(
                snapshot,
                clause.Field,
                token);
            candidates = candidates is null
                ? tokenCandidates
                : PostingListOperations.Intersect(candidates, tokenCandidates);
            if (candidates.Length == 0)
            {
                break;
            }
        }

        return candidates ?? [];
    }

    private static int[] RetrievePrefixCandidates(
        string prefix,
        ReadOnlySpan<string> vocabulary,
        PostingLookup postingLookup)
    {
        int[] result = [];
        int start = PostingListOperations.LowerBound(vocabulary, prefix);
        for (int index = start; index < vocabulary.Length; index++)
        {
            string term = vocabulary[index];
            if (!term.StartsWith(prefix, StringComparison.Ordinal))
            {
                break;
            }

            result = PostingListOperations.Union(result, postingLookup(term));
        }

        return result;
    }

    private static int[] UnionFields(params int[][] fields)
    {
        int[] result = [];
        foreach (int[] field in fields)
        {
            result = PostingListOperations.Union(result, field);
        }

        return result;
    }

    private static int[] VerifyClauseCandidates(
        SearchIndexSnapshot snapshot,
        ReadOnlySpan<int> candidates,
        SearchLexicalClause clause,
        CancellationToken cancellationToken)
    {
        var result = new List<int>(candidates.Length);
        for (int index = 0; index < candidates.Length; index++)
        {
            if ((index & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int documentId = candidates[index];
            if (LexicalClauseMatcher.Matches(
                snapshot.GetDocumentData(documentId),
                clause))
            {
                result.Add(documentId);
            }
        }

        return [.. result];
    }

    private delegate ReadOnlySpan<int> PostingLookup(string value);
}
