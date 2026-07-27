using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.Catalog;
using HorrorHarbor.Recommendations.Engine;

return await SelfTest.RunAsync(args).ConfigureAwait(false);

internal static class SelfTest
{
    private static readonly string[] TestTags = ["test"];

    public static async Task<int> RunAsync(string[] args)
    {
        var catalogPath = args.FirstOrDefault() ?? Path.GetFullPath("data/catalog/recommendation_catalog.json");
        try
        {
            var catalog = await new JsonCandidateCatalog(catalogPath).LoadAsync().ConfigureAwait(false);
            Assert(catalog.Count == 116, "Canonical Alpha 2 catalog must contain 116 validated candidates.");
            Assert(catalog.Select(static candidate => candidate.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Count, "Candidate IDs must be unique.");
            Assert(catalog.Select(static candidate => candidate.WatchExclusionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Count, "Candidate watch identities must be unique.");

            var engine = new DeterministicRecommendationEngine();
            var profile = new TasteProfile(
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Slasher / Killer"] = 1.0,
                    ["Body Horror"] = 0.8,
                    ["Psychological Horror"] = 0.6,
                },
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Saw"] = 1.0,
                    ["Friday the 13th"] = 0.9,
                },
                DesiredScareIntensity: 4,
                DesiredGoreIntensity: 4,
                DesiredDreadIntensity: 4,
                PreferSlashers: true,
                PreferExtreme: true,
                NoveltyPreference: 0.9);

            var candidates = new[]
            {
                Candidate("C1", "Saw VI", 2009, "Slasher / Killer", "Body Horror", "Saw", 4, 5, 3, true, true, 0.30, 0.80),
                Candidate("C2", "Candidate Alpha", 2026, "Slasher / Killer", "Body Horror", "Saw", 5, 5, 4, true, true, 0.90, 0.90),
                Candidate("C3", "Candidate Beta", 2024, "Body Horror", null, null, 4, 5, 4, false, true, 0.95, 0.70),
                Candidate("C4", "Candidate Gamma", 2023, "Psychological Horror", null, null, 3, 2, 5, false, false, 0.60, 1.00),
            };

            var watchedExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "movie|saw vi|*",
                "movie|talk to me|*",
            };
            var ranked = engine.Rank(candidates, profile, watchedExclusions, 3);
            var catalogRanked = engine.Rank(catalog, profile, watchedExclusions, 20);
            Assert(catalogRanked.Count == 20, "The full catalog must produce 20 unseen recommendations.");
            Assert(catalogRanked.All(result => result.Candidate.Title != "Talk to Me"), "User-confirmed watched corrections must be excluded.");
            Assert(ranked.Count == 3, "Three unseen candidates should remain after watched exclusion.");
            Assert(ranked.All(result => result.Candidate.CandidateId != "C1"), "Watched candidates must be excluded by title identity.");
            Assert(ranked.Zip(ranked.Skip(1), (left, right) => left.Score >= right.Score).All(static value => value), "Recommendation scores must be descending.");
            Assert(ranked.All(result => !string.IsNullOrWhiteSpace(result.Explanation)), "Every recommendation must be explainable.");
            Assert(ranked.All(result => Math.Abs(result.Score - result.Breakdown.Total) < 0.01), "Score breakdown must reconcile.");

            Console.WriteLine("SELF-TEST PASS");
            Console.WriteLine($"Catalog: {Path.GetFullPath(catalogPath)}");
            Console.WriteLine($"Candidates: {catalog.Count:N0}; bundled watch-history events: 0");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SELF-TEST FAIL");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static RecommendationCandidate Candidate(
        string id,
        string title,
        int year,
        string primary,
        string? secondary,
        string? franchise,
        int scare,
        int gore,
        int dread,
        bool slasher,
        bool extreme,
        double discovery,
        double quality) =>
        new(
            id,
            title,
            year,
            "Core Horror",
            primary,
            secondary,
            franchise,
            scare,
            gore,
            dread,
            slasher,
            extreme,
            100,
            discovery,
            quality,
            0.25,
            "US streaming availability requires live verification",
            "#000000",
            TestTags);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
