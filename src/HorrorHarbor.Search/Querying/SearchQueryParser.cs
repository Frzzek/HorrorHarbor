namespace HorrorHarbor.Search.Querying;

public sealed class SearchQueryParser : ISearchQueryParser
{
    public SearchQueryParseResult Parse(
        string queryText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryText);

        return SearchQueryScanner.Scan(queryText, cancellationToken) switch
        {
            SearchScanRejected rejected => new SearchQueryRejected(rejected.Diagnostic),
            SearchScanSucceeded succeeded => SearchQueryBinder.Bind(
                queryText,
                succeeded.Clauses,
                cancellationToken),
            _ => throw new InvalidOperationException("Unknown search scanner result."),
        };
    }
}
