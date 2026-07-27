namespace HorrorHarbor.Core.Models;

public sealed record UserTasteSeed(
    IReadOnlyDictionary<string, double> LensWeights,
    IReadOnlyDictionary<string, double> FranchiseWeights)
{
    public static UserTasteSeed HorrorHarborDefault { get; } = new(
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Slasher / Killer"] = 1.00,
            ["Survival / Isolation Horror"] = 0.92,
            ["Home Invasion / Stalker"] = 0.88,
            ["Psychological Horror"] = 0.82,
            ["Body Horror"] = 0.80,
            ["General Horror"] = 0.76,
            ["Creature / Animal"] = 0.68,
            ["Supernatural / Occult"] = 0.62,
            ["Sci-Fi / Cosmic Horror"] = 0.60,
            ["Horror Comedy"] = 0.44,
        },
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Saw"] = 1.00,
            ["Terrifier"] = 1.00,
            ["Friday the 13th"] = 0.98,
            ["The Hills Have Eyes"] = 0.96,
            ["Hostel"] = 0.94,
            ["Halloween"] = 0.82,
            ["Scream"] = 0.78,
        });
}
