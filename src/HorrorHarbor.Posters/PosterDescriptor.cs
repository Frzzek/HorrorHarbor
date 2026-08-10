namespace HorrorHarbor.Posters;

public sealed record PosterDescriptor(
    string CandidateId,
    string Title,
    int? ReleaseYear,
    string? PrimaryLens,
    string PosterTone,
    IReadOnlyList<string> Tags,
    string? OriginalTitle = null,
    string? OriginalLanguage = null,
    int? TmdbId = null,
    string? SecondaryLens = null,
    string? Franchise = null,
    int ScareIntensity = 0,
    int GoreIntensity = 0,
    int DreadIntensity = 0);

public enum PosterSourceKind
{
    GeneratedFallback,
    GeneratedCustom,
    TmdbOfficial,
}

public sealed record PosterProvenance(
    PosterSourceKind SourceKind,
    string Provider,
    string? ProviderAssetId,
    string? ProviderMediaId,
    string MatchMethod,
    double MatchConfidence,
    string? OriginalTitle,
    string? OriginalLanguage,
    string? Attribution,
    string? TermsUri,
    string RetrievedUtc,
    string ContentSha256);

public sealed record PosterAsset(
    string FilePath,
    int PixelWidth,
    int PixelHeight,
    bool WasGenerated,
    PosterProvenance? Provenance = null)
{
    public PosterSourceKind SourceKind => Provenance?.SourceKind ?? PosterSourceKind.GeneratedFallback;
}
