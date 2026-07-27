using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Indexing;

internal sealed class SearchIndexDocumentData
{
    public SearchIndexDocumentData(
        HorrorSearchDocument document,
        ImmutableArray<TitleVariant> titleVariants,
        ImmutableArray<NormalizedSearchText> tags,
        NormalizedSearchText? primaryLens,
        NormalizedSearchText? secondaryLens,
        NormalizedSearchText? franchise,
        NormalizedSearchText? originalLanguage)
    {
        Document = document;
        TitleVariants = titleVariants;
        Tags = tags;
        PrimaryLens = primaryLens;
        SecondaryLens = secondaryLens;
        Franchise = franchise;
        OriginalLanguage = originalLanguage;
    }

    public HorrorSearchDocument Document { get; }

    public ImmutableArray<TitleVariant> TitleVariants { get; }

    public ImmutableArray<NormalizedSearchText> Tags { get; }

    public NormalizedSearchText? PrimaryLens { get; }

    public NormalizedSearchText? SecondaryLens { get; }

    public NormalizedSearchText? Franchise { get; }

    public NormalizedSearchText? OriginalLanguage { get; }
}

internal readonly record struct NumericFacetValue(
    int Value,
    int DocumentId);
