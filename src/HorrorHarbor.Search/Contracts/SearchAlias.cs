using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Validation;

namespace HorrorHarbor.Search.Contracts;

public sealed class SearchAlias
{
    public SearchAlias(
        string value,
        SearchMetadataProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);

        NormalizedSearchText normalized =
            SearchDocumentValueValidator.NormalizeRequiredSearchableText(
                value,
                nameof(value));

        Value = value;
        NormalizedValue = normalized.Value;
        Provenance = provenance;
    }

    public string Value { get; }

    public string NormalizedValue { get; }

    public SearchMetadataProvenance Provenance { get; }

    internal SearchAlias Copy() => new(Value, Provenance.Copy());
}
