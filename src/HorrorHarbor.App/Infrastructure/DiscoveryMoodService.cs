using System.Globalization;

namespace HorrorHarbor.App.Infrastructure;

public sealed record DiscoveryMood(
    double DesiredScare,
    double DesiredGore,
    double DesiredDread,
    double Novelty,
    bool PreferSlashers,
    bool PreferExtreme,
    int RecommendationCount);

public sealed class DiscoveryMoodService(Random random)
{
    private readonly Lock _randomLock = new();

    public DiscoveryMood CreateSurprise()
    {
        lock (_randomLock)
        {
            return new DiscoveryMood(
                random.Next(2, 6),
                random.Next(1, 6),
                random.Next(2, 6),
                Novelty: 0.92,
                PreferSlashers: random.Next(0, 2) == 1,
                PreferExtreme: random.Next(0, 3) == 2,
                RecommendationCount: 12);
        }
    }

    public static DiscoveryMood CreateReset() => new(
        DesiredScare: 4,
        DesiredGore: 3,
        DesiredDread: 4,
        Novelty: 0.75,
        PreferSlashers: false,
        PreferExtreme: false,
        RecommendationCount: 12);

    public static bool TryDeserialize(string? value, out DiscoveryMood mood)
    {
        mood = CreateReset();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('|');
        if (parts.Length != 7 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double scare) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double gore) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double dread) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double novelty) ||
            !bool.TryParse(parts[4], out bool slashers) ||
            !bool.TryParse(parts[5], out bool extreme) ||
            !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            return false;
        }

        mood = new DiscoveryMood(
            Math.Clamp(scare, 0, 5),
            Math.Clamp(gore, 0, 5),
            Math.Clamp(dread, 0, 5),
            Math.Clamp(novelty, 0, 1),
            slashers,
            extreme,
            Math.Clamp(count, 6, 30));
        return true;
    }

    public static string Serialize(DiscoveryMood mood)
    {
        ArgumentNullException.ThrowIfNull(mood);
        return string.Join(
            '|',
            mood.DesiredScare.ToString("R", CultureInfo.InvariantCulture),
            mood.DesiredGore.ToString("R", CultureInfo.InvariantCulture),
            mood.DesiredDread.ToString("R", CultureInfo.InvariantCulture),
            mood.Novelty.ToString("R", CultureInfo.InvariantCulture),
            mood.PreferSlashers.ToString(CultureInfo.InvariantCulture),
            mood.PreferExtreme.ToString(CultureInfo.InvariantCulture),
            mood.RecommendationCount.ToString(CultureInfo.InvariantCulture));
    }
}
