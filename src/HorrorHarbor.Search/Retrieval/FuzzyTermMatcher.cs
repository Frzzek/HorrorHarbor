using System.Collections.Immutable;
using System.Text;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal readonly record struct FuzzyTermMatch(
    string IndexedTerm,
    int EditDistance);

internal static class FuzzyTermMatcher
{
    private const int BigramWidth = 2;
    private const int TrigramWidth = 3;

    public static ImmutableArray<FuzzyTermMatch> FindMatches(
        FuzzyTermIndex index,
        string queryTerm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(queryTerm);

        Span<Rune> queryScalars =
            stackalloc Rune[SearchGrammarLimits.MaximumNormalizedTokenRunes];
        int queryLength = BoundedEditDistance.CopyScalars(
            queryTerm,
            queryScalars,
            nameof(queryTerm));
        int maximumDistance = GetMaximumDistance(queryLength);
        if (maximumDistance == 0 || index.TermCount == 0)
        {
            return [];
        }

        int[] candidateTermIds = CanRequireSharedTrigram(
            queryLength,
            maximumDistance)
            ? RetrieveTrigramCandidates(
                index,
                queryScalars[..queryLength],
                cancellationToken)
            : RetrieveBigramCandidates(
                index,
                queryScalars[..queryLength],
                cancellationToken);

        var matches = ImmutableArray.CreateBuilder<FuzzyTermMatch>();
        Span<Rune> indexedScalars =
            stackalloc Rune[SearchGrammarLimits.MaximumNormalizedTokenRunes];
        for (int indexPosition = 0;
             indexPosition < candidateTermIds.Length;
             indexPosition++)
        {
            if ((indexPosition & 127) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int termId = candidateTermIds[indexPosition];
            int indexedLength = index.GetScalarLength(termId);
            if (Math.Abs(queryLength - indexedLength) > maximumDistance)
            {
                continue;
            }

            string indexedTerm = index.GetTerm(termId);
            if (indexedTerm.StartsWith(queryTerm, StringComparison.Ordinal))
            {
                continue;
            }

            int copiedLength = BoundedEditDistance.CopyScalars(
                indexedTerm,
                indexedScalars,
                nameof(index));
            if (BoundedEditDistance.TryCalculate(
                queryScalars[..queryLength],
                indexedScalars[..copiedLength],
                maximumDistance,
                out int distance) &&
                distance > 0)
            {
                matches.Add(new FuzzyTermMatch(indexedTerm, distance));
            }
        }

        return matches.ToImmutable();
    }

    public static int GetMaximumDistance(int scalarLength) => scalarLength switch
    {
        <= 3 => 0,
        <= 7 => 1,
        _ => BoundedEditDistance.MaximumSupportedDistance,
    };

    private static bool CanRequireSharedTrigram(
        int queryLength,
        int maximumDistance)
    {
        int queryTrigramCount = queryLength - TrigramWidth + 1;
        return queryTrigramCount > TrigramWidth * maximumDistance;
    }

    private static int[] RetrieveTrigramCandidates(
        FuzzyTermIndex index,
        ReadOnlySpan<Rune> queryScalars,
        CancellationToken cancellationToken)
    {
        int[] result = [];
        Span<ScalarTrigram> observed = stackalloc ScalarTrigram[
            SearchGrammarLimits.MaximumNormalizedTokenRunes -
            TrigramWidth +
            1];
        int observedCount = 0;

        for (int scalarIndex = 0;
             scalarIndex + TrigramWidth - 1 < queryScalars.Length;
             scalarIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trigram = new ScalarTrigram(
                queryScalars[scalarIndex].Value,
                queryScalars[scalarIndex + 1].Value,
                queryScalars[scalarIndex + 2].Value);
            if (Contains(observed[..observedCount], trigram))
            {
                continue;
            }

            observed[observedCount] = trigram;
            observedCount++;
            result = PostingListOperations.Union(
                result,
                index.GetTrigramPostings(trigram));
        }

        return result;
    }

    private static int[] RetrieveBigramCandidates(
        FuzzyTermIndex index,
        ReadOnlySpan<Rune> queryScalars,
        CancellationToken cancellationToken)
    {
        int[] result = [];
        Span<ScalarBigram> observed = stackalloc ScalarBigram[
            SearchGrammarLimits.MaximumNormalizedTokenRunes -
            BigramWidth +
            1];
        int observedCount = 0;

        for (int scalarIndex = 0;
             scalarIndex + BigramWidth - 1 < queryScalars.Length;
             scalarIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bigram = new ScalarBigram(
                queryScalars[scalarIndex].Value,
                queryScalars[scalarIndex + 1].Value);
            if (Contains(observed[..observedCount], bigram))
            {
                continue;
            }

            observed[observedCount] = bigram;
            observedCount++;
            result = PostingListOperations.Union(
                result,
                index.GetBigramPostings(bigram));
        }

        return result;
    }

    private static bool Contains(
        ReadOnlySpan<ScalarTrigram> values,
        ScalarTrigram expected)
    {
        foreach (ScalarTrigram value in values)
        {
            if (value == expected)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(
        ReadOnlySpan<ScalarBigram> values,
        ScalarBigram expected)
    {
        foreach (ScalarBigram value in values)
        {
            if (value == expected)
            {
                return true;
            }
        }

        return false;
    }
}
