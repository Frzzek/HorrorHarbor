namespace HorrorHarbor.Core.Models;

public sealed record TasteProfile(
    IReadOnlyDictionary<string, double> LensWeights,
    IReadOnlyDictionary<string, double> FranchiseWeights,
    double DesiredScareIntensity,
    double DesiredGoreIntensity,
    double DesiredDreadIntensity,
    bool? PreferSlashers,
    bool PreferExtreme,
    double NoveltyPreference)
{
    public static TasteProfile Empty { get; } = new(
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
        3, 3, 3, null, false, 0.75);
}
