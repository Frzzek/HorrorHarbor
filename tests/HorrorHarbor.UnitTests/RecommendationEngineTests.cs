using Microsoft.VisualStudio.TestTools.UnitTesting;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Recommendations.Engine;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class RecommendationEngineTests
{
    private static readonly string[] TestTags = ["test"];

    private static readonly TasteProfile Profile = new(
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Slasher / Killer"] = 1 },
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Saw"] = 1 },
        4, 4, 4, true, true, 0.8);

    [TestMethod]
    public void RankIsDeterministicAndDiverse()
    {
        var engine = new DeterministicRecommendationEngine();
        RecommendationCandidate[] candidates = Enumerable.Range(1, 20)
            .Select(index => Candidate($"id-{index}", $"Movie {index}", 2000 + index, index % 2 == 0 ? "Saw" : null))
            .ToArray();

        IReadOnlyList<RecommendationResult> first = engine.Rank(candidates, Profile, new HashSet<string>(), 12);
        IReadOnlyList<RecommendationResult> second = engine.Rank(candidates, Profile, new HashSet<string>(), 12);

        CollectionAssert.AreEqual(
            first.Select(static result => result.Candidate.CandidateId).ToArray(),
            second.Select(static result => result.Candidate.CandidateId).ToArray());
        Assert.AreEqual(12, first.Count);
        Assert.AreEqual(12, first.Select(static result => result.Candidate.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.IsTrue(first.All(static result => Math.Abs(result.Score - result.Breakdown.Total) < 0.01));
    }

    [TestMethod]
    public void Rank_ExcludesWatchedByTitleOnlyIdentity()
    {
        var engine = new DeterministicRecommendationEngine();
        RecommendationCandidate watched = Candidate("watched", "Talk to Me", 2022, null);
        RecommendationCandidate unseen = Candidate("unseen", "Fresh Terror", 2026, null);
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { watched.TitleOnlyExclusionKey };

        IReadOnlyList<RecommendationResult> results = engine.Rank(new[] { watched, unseen }, Profile, exclusions, 2);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("unseen", results[0].Candidate.CandidateId);
    }

    [TestMethod]
    public void Constructor_RejectsNaNWeights()
    {
        var weights = new RecommendationWeights(PrimaryLens: double.NaN);
        Assert.Throws<ArgumentException>(() => new DeterministicRecommendationEngine(weights));
    }

    [TestMethod]
    public void Rank_RejectsDuplicateCandidateIds()
    {
        var engine = new DeterministicRecommendationEngine();
        RecommendationCandidate first = Candidate("duplicate", "A", 2020, null);
        RecommendationCandidate second = Candidate("duplicate", "B", 2021, null);
        Assert.Throws<ArgumentException>(() =>
            engine.Rank(new[] { first, second }, Profile, new HashSet<string>(), 1));
    }

    private static RecommendationCandidate Candidate(string id, string title, int year, string? franchise) =>
        new(id, title, year, "Core Horror", "Slasher / Killer", null, franchise, 4, 4, 4, true, true, 100, 0.8, 0.8, 0.25, "verification required", "#000000", TestTags);
}
