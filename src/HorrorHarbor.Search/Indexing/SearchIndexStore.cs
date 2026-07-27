namespace HorrorHarbor.Search.Indexing;

public interface ISearchIndexStore
{
    SearchIndexSnapshot? Current { get; }

    void Publish(SearchIndexSnapshot snapshot);
}

public sealed class SearchIndexStore : ISearchIndexStore
{
    private SearchIndexSnapshot? _current;

    public SearchIndexSnapshot? Current => Volatile.Read(ref _current);

    public void Publish(SearchIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = Interlocked.Exchange(ref _current, snapshot);
    }
}
