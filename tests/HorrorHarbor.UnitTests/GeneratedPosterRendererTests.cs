using System.Security.Cryptography;
using HorrorHarbor.Posters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class GeneratedPosterRendererTests
{
    public static IEnumerable<object[]> FamilyCases
    {
        get
        {
            yield return Case("slasher", "Slasher / Killer", ["killer"], PosterCompositionFamily.Slasher);
            yield return Case("occult", "Supernatural / Occult", ["ritual"], PosterCompositionFamily.Occult);
            yield return Case("psychological", "Psychological Horror", ["paranoia"], PosterCompositionFamily.Psychological);
            yield return Case("body", "Body Horror", ["transformation"], PosterCompositionFamily.BodyHorror);
            yield return Case("cosmic", "Sci-Fi / Cosmic Horror", ["alien"], PosterCompositionFamily.Cosmic);
            yield return Case("creature", "Creature / Animal", ["monster"], PosterCompositionFamily.Creature);
            yield return Case("isolation", "Survival / Isolation Horror", ["snow"], PosterCompositionFamily.Isolation);
            yield return Case("footage", "General Horror", ["found footage"], PosterCompositionFamily.FoundFootage);
            yield return Case("gothic", "Folk / Gothic Horror", ["period horror"], PosterCompositionFamily.Gothic);
            yield return Case("invasion", "Home Invasion / Stalker", ["home invasion"], PosterCompositionFamily.HomeInvasion);
            yield return Case("comedy", "Horror Comedy", ["satire"], PosterCompositionFamily.HorrorComedy);
            yield return Case("general", "General Horror", ["dread"], PosterCompositionFamily.General);
        }
    }

    [TestMethod]
    [DynamicData(nameof(FamilyCases))]
    public void Analyze_SelectsExpectedCompositionFamily(
        PosterDescriptor descriptor,
        PosterCompositionFamily expected)
    {
        GeneratedPosterArtDirection direction = GeneratedPosterArtDirector.Analyze(descriptor);

        Assert.AreEqual(expected, direction.Family);
        Assert.IsFalse(string.IsNullOrWhiteSpace(direction.Motif));
        Assert.IsFalse(string.IsNullOrWhiteSpace(direction.Eyebrow));
        Assert.IsTrue(direction.Variation is >= 0 and < 12);
    }

    [TestMethod]
    [DynamicData(nameof(FamilyCases))]
    public void Render_AllCompositionFamiliesProduceDeterministicValidatedPng(
        PosterDescriptor descriptor,
        PosterCompositionFamily _)
    {
        byte[] first = ProceduralPosterRenderer.Render(descriptor, 240, 360);
        byte[] second = ProceduralPosterRenderer.Render(descriptor, 240, 360);

        CollectionAssert.AreEqual(SHA256.HashData(first), SHA256.HashData(second));
        Assert.IsTrue(PngImageValidator.IsValid(first, 240, 360, 2_000_000));
    }

    [TestMethod]
    public void Analyze_UsesEraAndFamilyForTypography()
    {
        PosterDescriptor grindhouse = Descriptor(
            "analog-slasher",
            "Analog Slasher",
            "Slasher / Killer",
            ["killer"]) with
        {
            ReleaseYear = 1978,
        };
        PosterDescriptor contemporaryFootage = Descriptor(
            "screen-horror",
            "Screen Horror",
            "General Horror",
            ["screenlife"]) with
        {
            ReleaseYear = 2024,
        };

        GeneratedPosterArtDirection first = GeneratedPosterArtDirector.Analyze(grindhouse);
        GeneratedPosterArtDirection second = GeneratedPosterArtDirector.Analyze(contemporaryFootage);

        Assert.AreEqual(PosterEraStyle.Grindhouse, first.EraStyle);
        Assert.AreEqual(PosterTitleStyle.Distressed, first.TitleStyle);
        Assert.AreEqual(PosterEraStyle.Contemporary, second.EraStyle);
        Assert.AreEqual(PosterTitleStyle.Technical, second.TitleStyle);
    }

    [TestMethod]
    public void CacheKey_ChangesForArtDirectionMetadata()
    {
        PosterDescriptor baseline = Descriptor(
            "identity",
            "Identity",
            "Psychological Horror",
            ["paranoia"]);
        string original = ProceduralPosterRenderer.GetCacheKey(baseline, 240, 360);
        var variants = new[]
        {
            baseline with { SecondaryLens = "Surreal Mystery" },
            baseline with { Franchise = "Identity Cycle" },
            baseline with { ScareIntensity = 1 },
            baseline with { GoreIntensity = 1 },
            baseline with { DreadIntensity = 1 },
        };

        Assert.IsTrue(variants.All(value =>
            ProceduralPosterRenderer.GetCacheKey(value, 240, 360) != original));
    }

    [TestMethod]
    public void Render_SemanticallyDistinctFamiliesProduceDistinctArtwork()
    {
        string[] hashes = FamilyCases
            .Select(static values => (PosterDescriptor)values[0])
            .Select(static descriptor => Convert.ToHexString(
                SHA256.HashData(ProceduralPosterRenderer.Render(descriptor, 240, 360))))
            .ToArray();

        Assert.HasCount(12, hashes.Distinct(StringComparer.Ordinal));
    }

    [TestMethod]
    public void Render_RejectsDimensionsOutsideSafetyBounds()
    {
        PosterDescriptor descriptor = Descriptor(
            "bounds",
            "Bounds",
            "General Horror",
            ["dread"]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ProceduralPosterRenderer.Render(descriptor, 239, 360));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            ProceduralPosterRenderer.Render(descriptor, 240, 1801));
    }

    private static object[] Case(
        string id,
        string lens,
        IReadOnlyList<string> tags,
        PosterCompositionFamily family) =>
        [Descriptor(id, id.Replace('-', ' '), lens, tags), family];

    private static PosterDescriptor Descriptor(
        string id,
        string title,
        string lens,
        IReadOnlyList<string> tags) => new(
            id,
            title,
            2018,
            lens,
            "#7F1D1D",
            tags,
            SecondaryLens: "Mystery",
            Franchise: null,
            ScareIntensity: 4,
            GoreIntensity: 3,
            DreadIntensity: 5);
}
