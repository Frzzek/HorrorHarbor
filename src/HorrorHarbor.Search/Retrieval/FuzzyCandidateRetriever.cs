using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Filtering;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal static class FuzzyCandidateRetriever
{
    public static int[] Retrieve(
        SearchIndexSnapshot snapshot,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(userContext);

        if (query.PositiveLexicalClauseCount == 0 ||
            query.FuzzyEligibleTermCount == 0)
        {
            return [];
        }

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
        return ExclusionEvaluator.Apply(
            snapshot,
            candidates,
            query,
            userContext,
            cancellationToken);
    }

    private static int[] RetrievePositiveLexicalCandidates(
        SearchIndexSnapshot snapshot,
        ParsedSearchQuery query,
        CancellationToken cancellationToken)
    {
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

            int[] candidates = lexical.Mode == SearchLexicalMode.Phrase
                ? ExactLexicalRetriever.RetrieveClauseCandidates(
                    snapshot,
                    lexical,
                    cancellationToken)
                : RetrieveExpandedClauseCandidates(
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

    private static int[] RetrieveExpandedClauseCandidates(
        SearchIndexSnapshot snapshot,
        SearchLexicalClause clause,
        CancellationToken cancellationToken)
    {
        var tokenPlans = ImmutableArray.CreateBuilder<FuzzyTokenPlan>(
            clause.Tokens.Length);
        int[]? candidates = null;
        foreach (string token in clause.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImmutableArray<FuzzyTermMatch> fuzzyMatches = FindFieldMatches(
                snapshot,
                clause.Field,
                token,
                cancellationToken);
            tokenPlans.Add(new FuzzyTokenPlan(token, fuzzyMatches));

            int[] tokenCandidates = ExactLexicalRetriever.RetrieveTokenCandidates(
                snapshot,
                clause.Field,
                token);
            foreach (FuzzyTermMatch fuzzyMatch in fuzzyMatches)
            {
                tokenCandidates = PostingListOperations.Union(
                    tokenCandidates,
                    ExactLexicalRetriever.RetrieveIndexedTermCandidates(
                        snapshot,
                        clause.Field,
                        fuzzyMatch.IndexedTerm));
            }

            candidates = candidates is null
                ? tokenCandidates
                : PostingListOperations.Intersect(candidates, tokenCandidates);
            if (candidates.Length == 0)
            {
                return [];
            }
        }

        return VerifyExpandedClauseCandidates(
            snapshot,
            candidates ?? [],
            clause.Field,
            tokenPlans.MoveToImmutable(),
            cancellationToken);
    }

    private static ImmutableArray<FuzzyTermMatch> FindFieldMatches(
        SearchIndexSnapshot snapshot,
        SearchField field,
        string queryTerm,
        CancellationToken cancellationToken)
    {
        if (field != SearchField.Any)
        {
            return FuzzyTermMatcher.FindMatches(
                GetFuzzyTerms(snapshot, field),
                queryTerm,
                cancellationToken);
        }

        var matchesByTerm = new Dictionary<string, int>(StringComparer.Ordinal);
        AddMatches(snapshot.GetTitleFuzzyTerms());
        AddMatches(snapshot.GetTagFuzzyTerms());
        AddMatches(snapshot.GetLensFuzzyTerms());
        AddMatches(snapshot.GetFranchiseFuzzyTerms());

        var result = ImmutableArray.CreateBuilder<FuzzyTermMatch>(
            matchesByTerm.Count);
        foreach ((string indexedTerm, int editDistance) in matchesByTerm)
        {
            result.Add(new FuzzyTermMatch(indexedTerm, editDistance));
        }

        result.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.IndexedTerm, right.IndexedTerm));
        return result.MoveToImmutable();

        void AddMatches(FuzzyTermIndex index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (FuzzyTermMatch match in FuzzyTermMatcher.FindMatches(
                index,
                queryTerm,
                cancellationToken))
            {
                if (!matchesByTerm.TryGetValue(
                        match.IndexedTerm,
                        out int existingDistance) ||
                    match.EditDistance < existingDistance)
                {
                    matchesByTerm[match.IndexedTerm] = match.EditDistance;
                }
            }
        }
    }

    private static FuzzyTermIndex GetFuzzyTerms(
        SearchIndexSnapshot snapshot,
        SearchField field) => field switch
        {
            SearchField.Title => snapshot.GetTitleFuzzyTerms(),
            SearchField.Tag => snapshot.GetTagFuzzyTerms(),
            SearchField.Lens => snapshot.GetLensFuzzyTerms(),
            SearchField.Franchise => snapshot.GetFranchiseFuzzyTerms(),
            SearchField.Language => snapshot.GetOriginalLanguageFuzzyTerms(),
            _ => throw new InvalidOperationException(
                "Fuzzy term retrieval received a non-lexical field."),
        };

    private static int[] VerifyExpandedClauseCandidates(
        SearchIndexSnapshot snapshot,
        ReadOnlySpan<int> candidates,
        SearchField field,
        ImmutableArray<FuzzyTokenPlan> tokenPlans,
        CancellationToken cancellationToken)
    {
        var result = new List<int>(candidates.Length);
        for (int index = 0; index < candidates.Length; index++)
        {
            if ((index & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int documentId = candidates[index];
            if (FuzzyLexicalClauseMatcher.Matches(
                snapshot.GetDocumentData(documentId),
                field,
                tokenPlans))
            {
                result.Add(documentId);
            }
        }

        return [.. result];
    }

    private sealed record ClauseCandidateSet(
        int ClauseIndex,
        int[] DocumentIds);
}
