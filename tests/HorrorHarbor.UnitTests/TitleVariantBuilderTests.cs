using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class TitleVariantBuilderTests
{
    private static readonly SearchMetadataProvenance CuratedProvenance = new(
        SearchMetadataSourceKind.CuratedCatalog,
        "horrorharbor.catalog",
        "catalog-v1");

    [TestMethod]
    public void Build_AddsSinglePunctuationDeletionVariant()
    {
        TitleVariant[] variants = Build("Frontier(s)");

        Assert.AreEqual(2, variants.Length);
        Assert.AreEqual("frontier s", variants[0].Value);
        Assert.AreEqual(SearchTitleVariantOrigin.CanonicalTitle, variants[0].Origin);
        Assert.AreEqual("frontiers", variants[1].Value);
        Assert.AreEqual(
            SearchTitleVariantOrigin.CanonicalPunctuationDeletion,
            variants[1].Origin);
    }

    [TestMethod]
    [DataRow("Saw VI", "saw vi", "saw 6")]
    [DataRow("The Exorcist III", "the exorcist iii", "the exorcist 3")]
    [DataRow("Cold Prey II", "cold prey ii", "cold prey 2")]
    [DataRow("Film 6", "film 6", "film vi")]
    public void Build_AddsStandaloneRomanOrArabicNumeralVariant(
        string title,
        string expectedCanonical,
        string expectedVariant)
    {
        TitleVariant[] variants = Build(title);

        Assert.AreEqual(expectedCanonical, variants[0].Value);
        TitleVariant numeralVariant = variants.Single(
            static variant => variant.Origin == SearchTitleVariantOrigin.RomanNumeralAlias);
        Assert.AreEqual(expectedVariant, numeralVariant.Value);
    }

    [TestMethod]
    public void Build_RestrictsNumeralVariantsToOneThroughTwenty()
    {
        TitleVariant[] twenty = Build("Film XX");
        TitleVariant[] twentyOneRoman = Build("Film XXI");
        TitleVariant[] twentyOneArabic = Build("Film 21");

        Assert.IsTrue(twenty.Any(static variant => variant.Value == "film 20"));
        Assert.IsFalse(twentyOneRoman.Any(
            static variant => variant.Origin == SearchTitleVariantOrigin.RomanNumeralAlias));
        Assert.IsFalse(twentyOneArabic.Any(
            static variant => variant.Origin == SearchTitleVariantOrigin.RomanNumeralAlias));
    }

    [TestMethod]
    [DataRow("You're Next", "youre next")]
    [DataRow("The Blackcoat's Daughter", "the blackcoats daughter")]
    public void Build_DoesNotGenerateArbitraryDeletionOrPermutationAliases(
        string title,
        string expectedCanonical)
    {
        TitleVariant[] variants = Build(title);

        Assert.AreEqual(1, variants.Length);
        Assert.AreEqual(expectedCanonical, variants[0].Value);
        Assert.AreEqual(SearchTitleVariantOrigin.CanonicalTitle, variants[0].Origin);
    }

    [TestMethod]
    public void Build_PreservesTypedOriginalTitleAndAliasOrigins()
    {
        var alias = new SearchAlias("The Grudge House", CuratedProvenance);
        var document = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(
                title: "The Grudge",
                originalTitle: "Ju-On: The Grudge"),
            "catalog-v1",
            SearchAvailabilityState.Unknown,
            aliases: [alias]);

        TitleVariant[] variants = TitleVariantBuilder.Build(document).ToArray();

        TitleVariant original = variants.Single(
            static variant => variant.Origin == SearchTitleVariantOrigin.OriginalTitle);
        TitleVariant explicitAlias = variants.Single(
            static variant => variant.Origin == SearchTitleVariantOrigin.ExplicitAlias);
        Assert.AreEqual("ju on the grudge", original.Value);
        Assert.AreEqual("the grudge house", explicitAlias.Value);
        Assert.AreEqual(
            "horrorharbor.catalog",
            explicitAlias.Provenance?.SourceIdentifier);
    }

    [TestMethod]
    public void Build_PreservesNormalizationChangesForEvidence()
    {
        TitleVariant[] variants = Build("Noroi: The Curse");

        TitleVariant canonical = variants[0];
        Assert.IsTrue(canonical.NormalizationChanges.HasFlag(
            SearchNormalizationChanges.PunctuationOrSymbolNormalized));
        Assert.IsTrue(canonical.NormalizationChanges.HasFlag(
            SearchNormalizationChanges.InvariantLowercased));
    }

    private static TitleVariant[] Build(string title)
    {
        var document = new HorrorSearchDocument(
            SearchTestData.CreateCandidate(title: title),
            "catalog-v1",
            SearchAvailabilityState.Unknown);

        return TitleVariantBuilder.Build(document).ToArray();
    }
}
