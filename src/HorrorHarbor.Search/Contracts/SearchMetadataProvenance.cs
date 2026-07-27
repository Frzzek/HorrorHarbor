using HorrorHarbor.Search.Validation;

namespace HorrorHarbor.Search.Contracts;

public enum SearchMetadataSourceKind
{
    CuratedCatalog,
    OnlineProvider,
}

public sealed class SearchMetadataProvenance
{
    public SearchMetadataProvenance(
        SearchMetadataSourceKind sourceKind,
        string sourceIdentifier,
        string sourceRevision)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                sourceKind,
                "Unknown search metadata source kind.");
        }

        SourceKind = sourceKind;
        SourceIdentifier = SearchDocumentValueValidator.ValidateStableKey(
            sourceIdentifier,
            nameof(sourceIdentifier),
            SearchDocumentLimits.MaximumSourceIdentifierCodeUnits);
        SourceRevision = SearchDocumentValueValidator.ValidateStableKey(
            sourceRevision,
            nameof(sourceRevision),
            SearchDocumentLimits.MaximumSourceRevisionCodeUnits);
    }

    public SearchMetadataSourceKind SourceKind { get; }

    public string SourceIdentifier { get; }

    public string SourceRevision { get; }

    internal SearchMetadataProvenance Copy() => new(
        SourceKind,
        SourceIdentifier,
        SourceRevision);
}
