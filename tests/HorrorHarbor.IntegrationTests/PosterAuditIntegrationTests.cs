using System.Text.Json;
using HorrorHarbor.Tools.PosterAudit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class PosterAuditIntegrationTests
{
    [TestMethod]
    public async Task SmallCatalogAudit_WritesEvidenceAndReproducesPosterHashes()
    {
        string root = Path.Combine(Path.GetTempPath(), "NocturneHarborAuditTests", Guid.NewGuid().ToString("N"));
        string catalogPath = Path.Combine(root, "catalog.json");
        string firstOutput = Path.Combine(root, "first");
        string secondOutput = Path.Combine(root, "second");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(catalogPath, CatalogJson);
            var firstOptions = new AuditOptions(catalogPath, firstOutput, 240, 360, 2_000_000, 5, 2, false, false);
            var secondOptions = firstOptions with { OutputPath = secondOutput };

            PosterAuditManifest first = await PosterAuditCommand.GenerateAsync(firstOptions);
            PosterAuditManifest second = await PosterAuditCommand.GenerateAsync(secondOptions);

            Assert.AreEqual("PASS", first.Summary.Status);
            Assert.AreEqual(2, first.Summary.GeneratedCount);
            Assert.AreEqual(0, first.Summary.OfficialArtworkCount);
            Assert.AreEqual(2, first.Summary.GeneratedFallbackCount);
            Assert.AreEqual(2, first.Summary.CompleteProvenanceCount);
            Assert.AreEqual(0, first.Summary.FailedCount);
            Assert.AreEqual(1, first.Summary.ContactSheetCount);
            Assert.AreEqual(2, first.Cache.Generated);
            Assert.AreEqual(0, first.Cache.Reused);
            CollectionAssert.AreEqual(
                first.Posters.Select(static poster => poster.Sha256).ToArray(),
                second.Posters.Select(static poster => poster.Sha256).ToArray());
            AssertEvidenceExists(firstOutput);

            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(firstOutput, "manifest.json")));
            Assert.AreEqual(2, manifest.RootElement.GetProperty("posters").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task InvalidPosterMetadata_IsCapturedWithoutDroppingSuccessfulEvidence()
    {
        string root = Path.Combine(Path.GetTempPath(), "NocturneHarborAuditTests", Guid.NewGuid().ToString("N"));
        string catalogPath = Path.Combine(root, "catalog.json");
        string outputPath = Path.Combine(root, "output");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(catalogPath, CatalogJson.Replace("\"title\": \"Beta\"", "\"title\": \"\"", StringComparison.Ordinal));
            var options = new AuditOptions(catalogPath, outputPath, 240, 360, 2_000_000, 5, 2, false, false);

            PosterAuditManifest manifest = await PosterAuditCommand.GenerateAsync(options);

            Assert.AreEqual("FAIL", manifest.Summary.Status);
            Assert.AreEqual(1, manifest.Posters.Count);
            Assert.AreEqual(1, manifest.Failures.Count);
            Assert.AreEqual("beta-2002", manifest.Failures[0].CandidateId);
            Assert.IsTrue(File.Exists(Path.Combine(outputPath, "failures.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void AssertEvidenceExists(string outputPath)
    {
        string[] files =
        [
            "manifest.json", "summary.json", "cache.json", "posters.json", "failures.json", "similarities.json",
            "evaluation-corpus.json", "index.html", "README.md", "contact-sheets/page-001.svg",
        ];
        foreach (string file in files)
        {
            Assert.IsTrue(File.Exists(Path.Combine(outputPath, file)), file);
        }
    }

    private const string CatalogJson = """
        [
          {
            "candidateId": "alpha-1999", "title": "Alpha", "releaseYear": 1999,
            "scopeTier": "Core Horror", "primaryLens": "Psychological", "secondaryLens": "General Horror",
            "franchise": null, "scareIntensity": 4, "goreIntensity": 2, "dreadIntensity": 5,
            "isSlasher": false, "isExtreme": false, "runtimeMinutes": 95,
            "discoveryScore": 0.8, "qualityScore": 0.9, "availabilityScore": 0.5,
            "availabilityLabel": "Verification required", "posterTone": "#7F1D1D",
            "tags": ["dread", "isolation"], "userFeedbackBoost": 0
          },
          {
            "candidateId": "beta-2002", "title": "Beta", "releaseYear": 2002,
            "scopeTier": "Core Horror", "primaryLens": "Creature", "secondaryLens": null,
            "franchise": "Beta", "scareIntensity": 3, "goreIntensity": 3, "dreadIntensity": 3,
            "isSlasher": false, "isExtreme": false, "runtimeMinutes": 88,
            "discoveryScore": 0.7, "qualityScore": 0.7, "availabilityScore": 0.5,
            "availabilityLabel": "Verification required", "posterTone": "#1F3A5F",
            "tags": ["creature"], "userFeedbackBoost": 0
          }
        ]
        """;
}
