using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Ranking;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.Search;

public interface IHorrorSearchEngine
{
    SearchResponse Execute(
        SearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HorrorSearchEngine(
    ISearchIndexStore indexStore,
    ISearchQueryParser queryParser) : IHorrorSearchEngine
{
    private readonly ISearchIndexStore _indexStore =
        indexStore ?? throw new ArgumentNullException(nameof(indexStore));
    private readonly ISearchQueryParser _queryParser =
        queryParser ?? throw new ArgumentNullException(nameof(queryParser));

    public SearchResponse Execute(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchIndexSnapshot? snapshot = _indexStore.Current;
            if (snapshot is null)
            {
                return new SearchIndexUnavailableResponse(
                    SearchIndexUnavailableReason.NoPublishedSnapshot);
            }

            SearchQueryParseResult parseResult = _queryParser.Parse(
                request.QueryText,
                cancellationToken);
            if (parseResult is SearchQueryRejected rejected)
            {
                return new SearchRejectedResponse(rejected.Diagnostic);
            }

            if (parseResult is not SearchQueryParsed parsed)
            {
                throw new InvalidOperationException(
                    "Search query parser returned an unsupported result type.");
            }

            ParsedSearchQuery query = parsed.Query;
            CandidateRetrievalResult retrieval = CandidateRetriever.Retrieve(
                snapshot,
                query,
                request.UserContext,
                request.ResultLimit,
                cancellationToken);
            ImmutableArray<RankedSearchCandidate> ranked = HybridRanker.RankWindow(
                snapshot,
                retrieval,
                query,
                request.UserContext,
                request.ResultLimit,
                cancellationToken);

            var results = ImmutableArray.CreateBuilder<SearchResult>(ranked.Length);
            foreach (RankedSearchCandidate item in ranked)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HorrorSearchDocument document = snapshot.GetDocument(item.DocumentId);
                results.Add(new SearchResult(
                    query.Intent,
                    document.Candidate,
                    request.UserContext.GetStatusUnchecked(item.CandidateId),
                    item.LexicalTier,
                    item.LexicalScore,
                    item.AffinityScore,
                    item.HybridScore,
                    item.QualityScore,
                    item.DiscoveryScore,
                    item.Evidence));
            }

            return new SearchSuccessResponse(
                query.Intent,
                snapshot.SnapshotVersion,
                snapshot.CatalogFingerprint,
                retrieval.DocumentIds.Length,
                results.MoveToImmutable());
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new SearchCanceledResponse();
        }
    }
}
