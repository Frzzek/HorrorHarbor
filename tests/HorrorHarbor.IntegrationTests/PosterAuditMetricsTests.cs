using HorrorHarbor.Posters;
using HorrorHarbor.Tools.PosterAudit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class PosterAuditMetricsTests
{
    [TestMethod]
    public void PerceptualHash_IsStableAndDistanceUsesAllBits()
    {
        var descriptor = new PosterDescriptor(
            "audit-hash-2026", "Audit Hash", 2026, "Psychological", "#7F1D1D", ["dread", "isolation"]);
        byte[] first = ProceduralPosterRenderer.Render(descriptor, 240, 360);
        byte[] second = ProceduralPosterRenderer.Render(descriptor, 240, 360);

        ulong firstHash = PerceptualHash.Calculate(first, 240, 360);
        ulong secondHash = PerceptualHash.Calculate(second, 240, 360);

        Assert.AreEqual(firstHash, secondHash);
        Assert.AreEqual(0, PerceptualHash.Distance(firstHash, secondHash));
        Assert.AreEqual(64, PerceptualHash.Distance(0, ulong.MaxValue));
    }

    [TestMethod]
    public void PerceptualHash_AcceptsAdaptivePngRowFilters()
    {
        var descriptor = new PosterDescriptor(
            "filtered-png-2026",
            "Filtered PNG",
            2026,
            "Sci-Fi / Cosmic Horror",
            "#243B7A",
            ["cosmic", "alien"],
            ScareIntensity: 4,
            DreadIntensity: 5);
        byte[] encoded = ProceduralPosterRenderer.Render(descriptor, 240, 360);

        ulong hash = PerceptualHash.Calculate(encoded, 240, 360);

        Assert.AreNotEqual(0UL, hash);
    }

    [TestMethod]
    public void EvaluationCorpus_IsDeterministicAndBounded()
    {
        AuditCandidate[] candidates =
        [
            Candidate("alpha-1980", "Shared", 1980, "Slasher", "Series", isSlasher: true, tags: ["Japanese horror"]),
            Candidate("beta-2000", "Shared", 2000, "Psychological", "Series", isExtreme: true),
            Candidate("gamma-2020", "Gamma", 2020, "Creature", null),
            Candidate("delta-1990", "Delta", 1990, "Creature", null),
        ];

        IReadOnlyDictionary<string, IReadOnlyList<string>> first = EvaluationCorpusSelector.Select(candidates, 3);
        IReadOnlyDictionary<string, IReadOnlyList<string>> second = EvaluationCorpusSelector.Select(candidates.Reverse().ToArray(), 3);

        CollectionAssert.AreEqual(first.Keys.ToArray(), second.Keys.ToArray());
        Assert.AreEqual(3, first.Count);
        Assert.IsTrue(first.Values.SelectMany(static reasons => reasons).Any(static reason => reason == "duplicate-title"));
        Assert.IsTrue(first.Values.SelectMany(static reasons => reasons).Any(static reason => reason == "language-market:Japanese horror"));
    }

    [TestMethod]
    public void AuditOptions_RejectUnsafeBoundsAndMissingValues()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditOptions.Parse(["--width"]));
        var options = new AuditOptions("missing.json", "output", 239, 360, 2_000_000, 20, 100, false, false);
        Assert.ThrowsExactly<ArgumentException>(options.Validate);
    }

    [TestMethod]
    public void AuditOptions_RequireConsistentPersonalTmdbAcknowledgement()
    {
        var missingAcknowledgement = new AuditOptions(
            "catalog.json", "output", 240, 360, 2_000_000, 20, 100, true, false);
        var acknowledgementWithoutTmdb = missingAcknowledgement with
        {
            UseTmdb = false,
            PersonalUseAcknowledged = true,
        };

        Assert.ThrowsExactly<ArgumentException>(missingAcknowledgement.Validate);
        Assert.ThrowsExactly<ArgumentException>(acknowledgementWithoutTmdb.Validate);
    }

    [TestMethod]
    public void AuditOptions_ParsePersonalHybridModeWithExplicitAcknowledgement()
    {
        AuditOptions options = AuditOptions.Parse([
            "--use-tmdb",
            "--acknowledge-personal-noncommercial-use",
        ]);

        Assert.IsTrue(options.UseTmdb);
        Assert.IsTrue(options.PersonalUseAcknowledged);
        StringAssert.Contains(options.ReproductionCommand, "--use-tmdb");
        StringAssert.Contains(options.ReproductionCommand, "--acknowledge-personal-noncommercial-use");
    }

    [TestMethod]
    public async Task GenerateAsync_RefusesToDeleteExistingOutputContent()
    {
        string root = Path.Combine(Path.GetTempPath(), "HorrorHarborPosterAuditSafety", Guid.NewGuid().ToString("N"));
        string catalogPath = Path.Combine(root, "catalog.json");
        string outputPath = Path.Combine(root, "output");
        string sentinelPath = Path.Combine(outputPath, "keep-me.txt");
        Directory.CreateDirectory(outputPath);
        await File.WriteAllTextAsync(catalogPath, "[]");
        await File.WriteAllTextAsync(sentinelPath, "sentinel");
        try
        {
            var options = new AuditOptions(
                catalogPath,
                outputPath,
                240,
                360,
                2_000_000,
                20,
                100,
                false,
                false);

            await Assert.ThrowsAsync<IOException>(() => PosterAuditCommand.GenerateAsync(options));
            Assert.AreEqual("sentinel", await File.ReadAllTextAsync(sentinelPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void SimilarityDetection_ReportsExactAndNearDuplicates()
    {
        var entries = new List<PosterAuditEntry>
        {
            Entry("a", "A", "SAME"),
            Entry("b", "B", "SAME"),
            Entry("c", "C", "OTHER"),
        };
        var hashes = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 0,
            ["b"] = 0,
            ["c"] = 0b1111,
        };

        SimilarityPair[] results = PosterAuditCommand.FindSimilarities(entries, hashes);

        Assert.AreEqual(3, results.Length);
        Assert.IsTrue(results.Any(static pair => pair.CandidateIdA == "a" && pair.CandidateIdB == "b" && pair.ExactDuplicate));
        Assert.IsTrue(results.Any(static pair => pair.CandidateIdB == "c" && pair.HammingDistance == 4 && !pair.ExactDuplicate));
    }

    private static AuditCandidate Candidate(
        string id,
        string title,
        int year,
        string lens,
        string? franchise,
        bool isSlasher = false,
        bool isExtreme = false,
        IReadOnlyList<string>? tags = null) => new(
            id, title, year, "Core Horror", lens, null, franchise, 3, 3, 3, isSlasher, isExtreme, 90,
            0.5, 0.5, 0.5, "Verification required", "#7F1D1D", tags ?? ["audit"], 0);

    private static PosterAuditEntry Entry(string id, string title, string sha256) => new(
        id,
        title,
        2000,
        "Audit",
        null,
        $"posters/{id}.png",
        sha256,
        "0000000000000000",
        100,
        1,
        false,
        [],
        nameof(PosterSourceKind.GeneratedFallback),
        "Nocturne Harbor renderer",
        "deterministic-local-fallback",
        1,
        true,
        nameof(PosterCompositionFamily.General),
        "labyrinth",
        nameof(PosterEraStyle.Millennium),
        nameof(PosterTitleStyle.Condensed));
}
