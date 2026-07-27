using System.Collections.Frozen;

namespace HorrorHarbor.Search.Indexing;

internal sealed class PostingListBuilder
{
    private readonly Dictionary<string, List<int>> _postings =
        new(StringComparer.Ordinal);

    public void Add(string normalizedValue, int documentId)
    {
        if (!_postings.TryGetValue(normalizedValue, out List<int>? values))
        {
            values = [];
            _postings.Add(normalizedValue, values);
        }

        if (values.Count == 0 || values[^1] != documentId)
        {
            values.Add(documentId);
        }
    }

    public PostingListIndex Build()
    {
        var frozenSource = new Dictionary<string, int[]>(
            _postings.Count,
            StringComparer.Ordinal);
        foreach ((string key, List<int> values) in _postings)
        {
            frozenSource.Add(key, values.ToArray());
        }

        string[] vocabulary = [.. _postings.Keys];
        Array.Sort(vocabulary, StringComparer.Ordinal);

        return new PostingListIndex(
            frozenSource.ToFrozenDictionary(StringComparer.Ordinal),
            vocabulary);
    }
}

internal sealed class PostingListIndex
{
    private readonly FrozenDictionary<string, int[]> _postings;
    private readonly string[] _vocabulary;

    public PostingListIndex(
        FrozenDictionary<string, int[]> postings,
        string[] vocabulary)
    {
        _postings = postings;
        _vocabulary = vocabulary;
    }

    public ReadOnlySpan<int> Get(string normalizedValue) =>
        _postings.TryGetValue(normalizedValue, out int[]? values)
            ? values
            : [];

    public ReadOnlySpan<string> Vocabulary => _vocabulary;
}
