using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Recommendations.Engine;

public sealed class TasteProfileBuilder
{
    private static readonly string[] KnownFranchises =
    {
        "Saw", "Terrifier", "Friday the 13th", "The Hills Have Eyes", "Hostel",
        "Halloween", "Scream", "A Nightmare on Elm Street", "The Texas Chain Saw Massacre",
        "Final Destination", "The Conjuring", "Insidious", "The Purge", "Alien",
    };

    public static TasteProfile Build(
        IEnumerable<TitleCatalogItem> watchedTitles,
        UserTasteSeed seed,
        double desiredScare,
        double desiredGore,
        double desiredDread,
        bool? preferSlashers,
        bool preferExtreme,
        double novelty)
    {
        ArgumentNullException.ThrowIfNull(watchedTitles);
        ArgumentNullException.ThrowIfNull(seed);

        var lensScores = Sanitize(seed.LensWeights);
        var franchiseScores = Sanitize(seed.FranchiseWeights);

        foreach (TitleCatalogItem title in watchedTitles)
        {
            if (title is null || !double.IsFinite(title.WeightedTasteEventCount))
            {
                continue;
            }

            double signal = Math.Clamp(title.WeightedTasteEventCount, 0, 4);
            if (signal <= 0)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(title.PrimaryLens))
            {
                Add(lensScores, title.PrimaryLens, signal);
            }

            if (!string.IsNullOrWhiteSpace(title.SecondaryLens))
            {
                Add(lensScores, title.SecondaryLens, signal * 0.45);
            }

            string? franchise = DetectFranchise(title.DisplayTitle);
            if (franchise is not null)
            {
                Add(franchiseScores, franchise, signal);
            }
        }

        return new TasteProfile(
            Normalize(lensScores),
            Normalize(franchiseScores),
            ClampFinite(desiredScare, 0, 5, 3),
            ClampFinite(desiredGore, 0, 5, 3),
            ClampFinite(desiredDread, 0, 5, 3),
            preferSlashers,
            preferExtreme,
            ClampFinite(novelty, 0, 1, 0.75));
    }

    private static Dictionary<string, double> Sanitize(IReadOnlyDictionary<string, double> source)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, double value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key) && double.IsFinite(value) && value >= 0)
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static void Add(Dictionary<string, double> values, string key, double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0)
        {
            return;
        }

        values.TryGetValue(key, out double current);
        values[key] = current + amount;
    }

    private static Dictionary<string, double> Normalize(Dictionary<string, double> values)
    {
        if (values.Count == 0)
        {
            return values;
        }

        double maximum = values.Values.Where(double.IsFinite).DefaultIfEmpty(0).Max();
        if (maximum <= 0)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        return values.ToDictionary(
            static pair => pair.Key,
            pair => Math.Clamp(pair.Value / maximum, 0, 1),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? DetectFranchise(string title) => KnownFranchises.FirstOrDefault(
        franchise => title.Contains(franchise, StringComparison.OrdinalIgnoreCase));

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
