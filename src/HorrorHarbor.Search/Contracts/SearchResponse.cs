using System.Collections.Immutable;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Contracts;

public abstract record SearchResponse;

public sealed record SearchSuccessResponse : SearchResponse
{
    internal SearchSuccessResponse(
        SearchIntent intent,
        string snapshotVersion,
        string catalogFingerprint,
        int totalMatchingCount,
        ImmutableArray<SearchResult> results)
    {
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intent),
                intent,
                "Unknown search intent.");
        }

        ArgumentException.ThrowIfNullOrEmpty(snapshotVersion);
        ArgumentException.ThrowIfNullOrEmpty(catalogFingerprint);
        ArgumentOutOfRangeException.ThrowIfNegative(totalMatchingCount);
        if (results.IsDefault || results.Length > totalMatchingCount)
        {
            throw new ArgumentException(
                "Search results must be initialized and cannot exceed the total matching count.",
                nameof(results));
        }

        Intent = intent;
        SnapshotVersion = snapshotVersion;
        CatalogFingerprint = catalogFingerprint;
        TotalMatchingCount = totalMatchingCount;
        Results = results;
    }

    public SearchIntent Intent { get; }

    public string SnapshotVersion { get; }

    public string CatalogFingerprint { get; }

    public int TotalMatchingCount { get; }

    public ImmutableArray<SearchResult> Results { get; }
}

public sealed record SearchRejectedResponse : SearchResponse
{
    internal SearchRejectedResponse(SearchDiagnostic diagnostic)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public SearchDiagnostic Diagnostic { get; }
}

public sealed record SearchCanceledResponse : SearchResponse;

public enum SearchIndexUnavailableReason
{
    NoPublishedSnapshot,
}

public sealed record SearchIndexUnavailableResponse : SearchResponse
{
    internal SearchIndexUnavailableResponse(SearchIndexUnavailableReason reason)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown index-unavailable reason.");
        }

        Reason = reason;
    }

    public SearchIndexUnavailableReason Reason { get; }

    public string StableReasonCode => Reason switch
    {
        SearchIndexUnavailableReason.NoPublishedSnapshot =>
            "no-published-snapshot",
        _ => throw new InvalidOperationException(
            "Index-unavailable reason has no stable-code mapping."),
    };
}
