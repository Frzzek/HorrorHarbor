using Microsoft.VisualStudio.TestTools.UnitTesting;
using HorrorHarbor.Core.ValueObjects;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class TitleNormalizerTests
{
    [TestMethod]
    [DataRow("Talk to Me", "talk to me")]
    [DataRow("  The  Hills—Have Eyes! ", "the hills have eyes")]
    [DataRow("Léon: The Professional", "leon the professional")]
    [DataRow("Friday the 13th: Part VI", "friday the 13th part vi")]
    public void NormalizeForKey_ProducesStableIdentity(string source, string expected) =>
        Assert.AreEqual(expected, TitleNormalizer.NormalizeForKey(source));

    [TestMethod]
    public void NormalizeForKey_RejectsWhitespace() =>
        Assert.Throws<ArgumentException>(() => TitleNormalizer.NormalizeForKey("   "));
}
