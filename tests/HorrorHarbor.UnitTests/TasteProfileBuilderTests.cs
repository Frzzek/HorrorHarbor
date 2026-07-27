using Microsoft.VisualStudio.TestTools.UnitTesting;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Recommendations.Engine;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class TasteProfileBuilderTests
{
    [TestMethod]
    public void Build_IgnoresNonFiniteSignalsAndClampsControls()
    {
        var titles = new[]
        {
            Title("Saw VI", 2.5, "Slasher / Killer"),
            Title("Broken", double.NaN, "Body Horror"),
        };

        TasteProfile profile = TasteProfileBuilder.Build(
            titles,
            UserTasteSeed.HorrorHarborDefault,
            double.PositiveInfinity,
            -100,
            99,
            true,
            true,
            double.NaN);

        Assert.AreEqual(3d, profile.DesiredScareIntensity);
        Assert.AreEqual(0d, profile.DesiredGoreIntensity);
        Assert.AreEqual(5d, profile.DesiredDreadIntensity);
        Assert.AreEqual(0.75, profile.NoveltyPreference);
        Assert.IsTrue(profile.FranchiseWeights["Saw"] > 0);
    }

    private static TitleCatalogItem Title(string title, double weight, string lens) =>
        new("id", "Movie", title, 2020, "plex://id", "movie|x|2020", 1, weight, DateTime.UnixEpoch, DateTime.UnixEpoch, "Core Horror", lens, null, "High", "Current", false);
}
