using System.Text.Json.Serialization;

namespace HorrorHarbor.Tools.PosterAudit;

public sealed record AuditCandidate(
    string CandidateId,
    string Title,
    int? ReleaseYear,
    string ScopeTier,
    string? PrimaryLens,
    string? SecondaryLens,
    string? Franchise,
    int ScareIntensity,
    int GoreIntensity,
    int DreadIntensity,
    bool IsSlasher,
    bool IsExtreme,
    int? RuntimeMinutes,
    double DiscoveryScore,
    double QualityScore,
    double AvailabilityScore,
    string AvailabilityLabel,
    string PosterTone,
    IReadOnlyList<string> Tags,
    double UserFeedbackBoost,
    string? OriginalTitle = null,
    string? OriginalLanguage = null,
    int? TmdbId = null);

public sealed record PosterAuditEntry(
    string CandidateId,
    string Title,
    int? ReleaseYear,
    string? PrimaryLens,
    string? Franchise,
    string RelativePath,
    string Sha256,
    string PerceptualHash,
    long FileBytes,
    double RenderMilliseconds,
    bool IsEvaluationCorpus,
    IReadOnlyList<string> EvaluationReasons,
    string SourceKind,
    string Provider,
    string MatchMethod,
    double MatchConfidence,
    bool ProvenanceComplete,
    string CompositionFamily,
    string Motif,
    string EraStyle,
    string TitleStyle);

public sealed record PosterAuditFailure(
    string CandidateId,
    string Title,
    string ExceptionType,
    string Message);

public sealed record SimilarityPair(
    string CandidateIdA,
    string TitleA,
    string CandidateIdB,
    string TitleB,
    int HammingDistance,
    bool ExactDuplicate);

public sealed record CorpusEntry(
    string CandidateId,
    string Title,
    int? ReleaseYear,
    IReadOnlyList<string> Reasons);

public sealed record PosterAuditSummary(
    string Build,
    int RendererVersion,
    string CatalogSha256,
    int CatalogCount,
    int GeneratedCount,
    int OfficialArtworkCount,
    int GeneratedFallbackCount,
    int CompleteProvenanceCount,
    IReadOnlyDictionary<string, int> CompositionFamilyDistribution,
    int SemanticStyleCombinations,
    int FailedCount,
    int ExactDuplicateGroups,
    int NearDuplicatePairs,
    int UniquePerceptualHashes,
    int EvaluationCorpusCount,
    int ContactSheetCount,
    double TotalRenderMilliseconds,
    double MeanRenderMilliseconds,
    double P95RenderMilliseconds,
    long TotalPosterBytes,
    string Status,
    string GeneratedUtc,
    string ReproductionCommand);

public sealed record PosterCacheMetrics(
    int Requests,
    int Generated,
    int Reused,
    double HitRate,
    long CacheBytes);

public sealed record PosterAuditManifest(
    PosterAuditSummary Summary,
    PosterCacheMetrics Cache,
    IReadOnlyList<PosterAuditEntry> Posters,
    IReadOnlyList<PosterAuditFailure> Failures,
    IReadOnlyList<SimilarityPair> Similarities,
    IReadOnlyList<CorpusEntry> EvaluationCorpus);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AuditCandidate[]))]
[JsonSerializable(typeof(PosterAuditManifest))]
[JsonSerializable(typeof(PosterAuditSummary))]
[JsonSerializable(typeof(PosterCacheMetrics))]
[JsonSerializable(typeof(PosterAuditEntry[]))]
[JsonSerializable(typeof(PosterAuditFailure[]))]
[JsonSerializable(typeof(SimilarityPair[]))]
[JsonSerializable(typeof(CorpusEntry[]))]
internal sealed partial class AuditJsonContext : JsonSerializerContext;
