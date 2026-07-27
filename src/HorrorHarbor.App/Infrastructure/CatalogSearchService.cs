using System.IO;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Search;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.App.Infrastructure;

public sealed record CatalogSearchOutcome(
    IReadOnlyList<SearchResult> Results,
    int TotalMatchingCount,
    SearchDiagnostic? Diagnostic,
    string StatusCode);

public interface ICatalogSearchService
{
    void PublishCatalog(
        IReadOnlyList<RecommendationCandidate> candidates,
        CancellationToken cancellationToken = default);

    CatalogSearchOutcome Search(
        IReadOnlyDictionary<string, RecommendationFeedback> feedback,
        string queryText,
        int resultLimit,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogSearchService : ICatalogSearchService
{
    private readonly SearchIndexStore _store = new();
    private readonly HorrorSearchEngine _engine;

    public CatalogSearchService()
    {
        _engine = new HorrorSearchEngine(_store, new SearchQueryParser());
    }

    public void PublishCatalog(
        IReadOnlyList<RecommendationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        SearchIndexBuildResult build = new SearchIndexBuilder().Build(
            candidates.Select(static candidate => new HorrorSearchDocument(
                candidate,
                sourceRevision: "curated-catalog-v1",
                SearchAvailabilityState.Unknown)),
            cancellationToken);
        if (build is not SearchIndexBuildSucceeded succeeded)
        {
            throw new InvalidDataException("The curated catalog could not be published to the search index.");
        }

        _store.Publish(succeeded.Snapshot);
    }

    public CatalogSearchOutcome Search(
        IReadOnlyDictionary<string, RecommendationFeedback> feedback,
        string queryText,
        int resultLimit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(queryText);
        var statuses = new Dictionary<string, SearchLocalStatus>(StringComparer.Ordinal);
        foreach ((string candidateId, RecommendationFeedback value) in feedback)
        {
            statuses[candidateId] = value == RecommendationFeedback.Watched
                ? SearchLocalStatus.Watched
                : SearchLocalStatus.Unseen;
        }

        SearchResponse response = _engine.Execute(
            new SearchRequest(
                queryText,
                resultLimit,
                new SearchUserContext(statuses)),
            cancellationToken);

        return response switch
        {
            SearchSuccessResponse success => new CatalogSearchOutcome(
                success.Results,
                success.TotalMatchingCount,
                null,
                "search-success"),
            SearchRejectedResponse rejected => new CatalogSearchOutcome(
                Array.Empty<SearchResult>(),
                0,
                rejected.Diagnostic,
                "query-rejected"),
            SearchCanceledResponse => new CatalogSearchOutcome(
                Array.Empty<SearchResult>(),
                0,
                null,
                "search-canceled"),
            SearchIndexUnavailableResponse => new CatalogSearchOutcome(
                Array.Empty<SearchResult>(),
                0,
                null,
                "index-unavailable"),
            _ => throw new InvalidOperationException("Unsupported search response."),
        };
    }
}
