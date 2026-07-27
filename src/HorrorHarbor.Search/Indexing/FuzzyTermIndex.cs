using System.Collections.Frozen;
using System.Text;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Indexing;

internal readonly record struct ScalarBigram(
    int First,
    int Second);

internal readonly record struct ScalarTrigram(
    int First,
    int Second,
    int Third);

internal sealed class FuzzyTermIndex
{
    private readonly string[] _terms;
    private readonly byte[] _scalarLengths;
    private readonly int[][] _termIdsByScalarLength;
    private readonly FrozenDictionary<ScalarBigram, int[]> _bigramPostings;
    private readonly FrozenDictionary<ScalarTrigram, int[]> _trigramPostings;

    private FuzzyTermIndex(
        string[] terms,
        byte[] scalarLengths,
        int[][] termIdsByScalarLength,
        FrozenDictionary<ScalarBigram, int[]> bigramPostings,
        FrozenDictionary<ScalarTrigram, int[]> trigramPostings)
    {
        _terms = terms;
        _scalarLengths = scalarLengths;
        _termIdsByScalarLength = termIdsByScalarLength;
        _bigramPostings = bigramPostings;
        _trigramPostings = trigramPostings;
    }

    public int TermCount => _terms.Length;

    public string GetTerm(int termId) => _terms[termId];

    public int GetScalarLength(int termId) => _scalarLengths[termId];

    public ReadOnlySpan<int> GetTermIdsByScalarLength(int scalarLength) =>
        scalarLength >= 0 && scalarLength < _termIdsByScalarLength.Length
            ? _termIdsByScalarLength[scalarLength]
            : [];

    public ReadOnlySpan<int> GetBigramPostings(ScalarBigram bigram) =>
        _bigramPostings.TryGetValue(bigram, out int[]? termIds)
            ? termIds
            : [];

    public ReadOnlySpan<int> GetTrigramPostings(ScalarTrigram trigram) =>
        _trigramPostings.TryGetValue(trigram, out int[]? termIds)
            ? termIds
            : [];

    public static FuzzyTermIndex Build(
        ReadOnlySpan<string> vocabulary,
        CancellationToken cancellationToken)
    {
        string[] terms = vocabulary.ToArray();
        var lengthBuckets = new List<int>[
            SearchGrammarLimits.MaximumNormalizedTokenRunes + 1];
        var mutableBigrams = new Dictionary<ScalarBigram, List<int>>();
        var mutableTrigrams = new Dictionary<ScalarTrigram, List<int>>();
        byte[] scalarLengths = new byte[terms.Length];
        Span<Rune> scalars =
            stackalloc Rune[SearchGrammarLimits.MaximumNormalizedTokenRunes];

        for (int termId = 0; termId < terms.Length; termId++)
        {
            if ((termId & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int scalarLength = CopyScalars(terms[termId], scalars);
            scalarLengths[termId] = checked((byte)scalarLength);
            (lengthBuckets[scalarLength] ??= []).Add(termId);
            AddBigrams(
                scalars[..scalarLength],
                termId,
                mutableBigrams);
            AddTrigrams(
                scalars[..scalarLength],
                termId,
                mutableTrigrams);
        }

        int[][] termIdsByScalarLength = new int[lengthBuckets.Length][];
        for (int index = 0; index < lengthBuckets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            termIdsByScalarLength[index] = lengthBuckets[index]?.ToArray() ?? [];
        }

        FrozenDictionary<ScalarBigram, int[]> frozenBigrams = FreezePostings(
            mutableBigrams,
            cancellationToken);
        FrozenDictionary<ScalarTrigram, int[]> frozenTrigrams = FreezePostings(
            mutableTrigrams,
            cancellationToken);

        return new FuzzyTermIndex(
            terms,
            scalarLengths,
            termIdsByScalarLength,
            frozenBigrams,
            frozenTrigrams);
    }

    private static FrozenDictionary<TKey, int[]> FreezePostings<TKey>(
        Dictionary<TKey, List<int>> mutablePostings,
        CancellationToken cancellationToken)
        where TKey : notnull
    {
        var frozenSource = new Dictionary<TKey, int[]>(mutablePostings.Count);
        foreach ((TKey key, List<int> termIds) in mutablePostings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            frozenSource.Add(key, termIds.ToArray());
        }

        return frozenSource.ToFrozenDictionary();
    }

    private static int CopyScalars(string value, Span<Rune> destination)
    {
        int count = 0;
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (count == destination.Length)
            {
                throw new InvalidOperationException(
                    "Indexed term exceeds the validated Unicode scalar limit.");
            }

            destination[count] = rune;
            count++;
        }

        return count;
    }

    private static void AddTrigrams(
        ReadOnlySpan<Rune> scalars,
        int termId,
        Dictionary<ScalarTrigram, List<int>> postings)
    {
        for (int index = 0; index + 2 < scalars.Length; index++)
        {
            var trigram = new ScalarTrigram(
                scalars[index].Value,
                scalars[index + 1].Value,
                scalars[index + 2].Value);
            if (!postings.TryGetValue(trigram, out List<int>? termIds))
            {
                termIds = [];
                postings.Add(trigram, termIds);
            }

            if (termIds.Count == 0 || termIds[^1] != termId)
            {
                termIds.Add(termId);
            }
        }
    }

    private static void AddBigrams(
        ReadOnlySpan<Rune> scalars,
        int termId,
        Dictionary<ScalarBigram, List<int>> postings)
    {
        for (int index = 0; index + 1 < scalars.Length; index++)
        {
            var bigram = new ScalarBigram(
                scalars[index].Value,
                scalars[index + 1].Value);
            if (!postings.TryGetValue(bigram, out List<int>? termIds))
            {
                termIds = [];
                postings.Add(bigram, termIds);
            }

            if (termIds.Count == 0 || termIds[^1] != termId)
            {
                termIds.Add(termId);
            }
        }
    }
}
