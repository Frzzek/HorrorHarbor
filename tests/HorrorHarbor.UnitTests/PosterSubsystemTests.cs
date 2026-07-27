using System.Security.Cryptography;
using HorrorHarbor.Posters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class PosterSubsystemTests
{
    private static readonly PosterDescriptor Descriptor = new(
        "the-collector-2009",
        "The Collector",
        2009,
        "Slasher / Killer",
        "#7F1D1D",
        ["traps", "home invasion", "killer"]);

    [TestMethod]
    public void Render_IsDeterministicAndProducesValidatedPng()
    {
        byte[] first = ProceduralPosterRenderer.Render(Descriptor, 240, 360);
        byte[] second = ProceduralPosterRenderer.Render(Descriptor, 240, 360);

        CollectionAssert.AreEqual(SHA256.HashData(first), SHA256.HashData(second));
        Assert.IsTrue(PngImageValidator.IsValid(first, 240, 360, 2_000_000));
    }

    [TestMethod]
    public void Render_MetadataChangeChangesArtwork()
    {
        byte[] first = ProceduralPosterRenderer.Render(Descriptor, 240, 360);
        byte[] second = ProceduralPosterRenderer.Render(
            Descriptor with { CandidateId = "the-collection-2012", Title = "The Collection" },
            240,
            360);

        CollectionAssert.AreNotEqual(SHA256.HashData(first), SHA256.HashData(second));
    }

    [TestMethod]
    public void Validator_RejectsTruncatedAndOversizedInput()
    {
        byte[] valid = ProceduralPosterRenderer.Render(Descriptor, 240, 360);
        byte[] truncated = valid[..^12];

        Assert.IsFalse(PngImageValidator.IsValid(truncated, 240, 360, 2_000_000));
        Assert.IsFalse(PngImageValidator.IsValid(valid, 240, 360, valid.Length - 1));
        Assert.IsFalse(PngImageValidator.IsValid(valid, 241, 360, 2_000_000));
    }

    [TestMethod]
    public async Task Cache_RegeneratesCorruptAssetAndThenReusesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "NocturneHarborTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new LocalPosterService(new PosterCacheOptions(root, 240, 360, 2_000_000));
            PosterAsset generated = await service.GetPosterAsync(Descriptor);
            Assert.IsTrue(generated.WasGenerated);
            await File.WriteAllTextAsync(generated.FilePath, "not an image");

            PosterAsset repaired = await service.GetPosterAsync(Descriptor);
            Assert.IsTrue(repaired.WasGenerated);
            PosterAsset reused = await service.GetPosterAsync(Descriptor);
            Assert.IsFalse(reused.WasGenerated);
            Assert.IsTrue(PngImageValidator.IsValidFile(reused.FilePath, 240, 360, 2_000_000));
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
    public async Task Cache_DeduplicatesConcurrentGeneration()
    {
        string root = Path.Combine(Path.GetTempPath(), "NocturneHarborTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new LocalPosterService(new PosterCacheOptions(root, 240, 360, 2_000_000));
            PosterAsset[] results = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => service.GetPosterAsync(Descriptor)));

            Assert.AreEqual(1, results.Count(static result => result.WasGenerated));
            Assert.AreEqual(1, results.Select(static result => result.FilePath).Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
