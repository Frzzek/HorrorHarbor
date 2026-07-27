using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Validation;

namespace HorrorHarbor.Search.Contracts;

public sealed class SearchAvailabilityFact
{
    public SearchAvailabilityFact(
        string providerValue,
        SearchMetadataProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        if (provenance.SourceKind != SearchMetadataSourceKind.OnlineProvider)
        {
            throw new ArgumentException(
                "Verified availability facts require online-provider provenance.",
                nameof(provenance));
        }

        NormalizedSearchText normalized =
            SearchDocumentValueValidator.NormalizeRequiredSearchableText(
                providerValue,
                nameof(providerValue));

        ProviderValue = providerValue;
        NormalizedProviderValue = normalized.Value;
        Provenance = provenance;
    }

    public string ProviderValue { get; }

    public string NormalizedProviderValue { get; }

    public SearchMetadataProvenance Provenance { get; }

    internal SearchAvailabilityFact Copy() => new(
        ProviderValue,
        Provenance.Copy());
}
