using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Contracts;

public enum SearchSort
{
    BestMatch,
}

public sealed class SearchRequest
{
    public SearchRequest(
        string queryText,
        int resultLimit,
        SearchUserContext userContext,
        SearchSort sort = SearchSort.BestMatch)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(userContext);
        if (resultLimit is < 1 or > SearchGrammarLimits.MaximumLocalResults)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resultLimit),
                resultLimit,
                $"Result limit must be between 1 and {SearchGrammarLimits.MaximumLocalResults}.");
        }

        if (!Enum.IsDefined(sort))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sort),
                sort,
                "Unknown search sort.");
        }

        QueryText = queryText;
        ResultLimit = resultLimit;
        UserContext = userContext;
        Sort = sort;
    }

    public string QueryText { get; }

    public int ResultLimit { get; }

    public SearchUserContext UserContext { get; }

    public SearchSort Sort { get; }
}
