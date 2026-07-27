using System.Globalization;
using HorrorHarbor.Core.ValueObjects;

namespace HorrorHarbor.Core.Models;

public sealed record RecommendationCandidate(
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
    double UserFeedbackBoost = 0,
    string? OriginalTitle = null,
    string? OriginalLanguage = null,
    int? TmdbId = null)
{
    public int ClampedScareIntensity => Math.Clamp(ScareIntensity, 0, 5);
    public int ClampedGoreIntensity => Math.Clamp(GoreIntensity, 0, 5);
    public int ClampedDreadIntensity => Math.Clamp(DreadIntensity, 0, 5);
    public string NormalizedTitle => TitleNormalizer.NormalizeForKey(Title);
    public string WatchExclusionKey =>
        $"movie|{NormalizedTitle}|{ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
    public string TitleOnlyExclusionKey => $"movie|{NormalizedTitle}|*";
    public string YearLabel => ReleaseYear?.ToString(CultureInfo.CurrentCulture) ?? "Year unknown";
    public string RuntimeLabel => RuntimeMinutes is null ? "Runtime unknown" : $"{RuntimeMinutes} min";
    public string IntensityLabel =>
        $"Scare {ClampedScareIntensity}/5  •  Gore {ClampedGoreIntensity}/5  •  Dread {ClampedDreadIntensity}/5";
    public string TagLine => string.Join("  •  ", Tags.Take(4));
}
