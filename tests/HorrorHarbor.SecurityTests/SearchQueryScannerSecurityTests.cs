using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.SecurityTests;

[TestClass]
public sealed class SearchQueryScannerSecurityTests
{
    [TestMethod]
    [DataRow("host\u202Efound", 4)]
    [DataRow("host\u2066found", 4)]
    [DataRow("host\u200Bfound", 4)]
    [DataRow("𐐀\u202Ehost", 2)]
    public void Scan_RejectsUnsafeFormatCharactersAtUtf16Span(
        string query,
        int expectedStart)
    {
        SearchScanRejected result = Assert.IsInstanceOfType<SearchScanRejected>(
            SearchQueryScanner.Scan(query, CancellationToken.None));

        Assert.AreEqual(SearchDiagnosticCode.UnsafeFormatCharacter, result.Diagnostic.Code);
        Assert.AreEqual("unsafe-format-character", result.Diagnostic.StableCode);
        Assert.AreEqual(expectedStart, result.Diagnostic.Span.Start);
        Assert.AreEqual(1, result.Diagnostic.Span.Length);
    }

    [TestMethod]
    public void Scan_AllowsJoinControlsNeededBySomeScripts()
    {
        SearchScanResult result = SearchQueryScanner.Scan("क्‍ष", CancellationToken.None);

        Assert.IsInstanceOfType<SearchScanSucceeded>(result);
    }

    [TestMethod]
    public void Scan_RejectsControlCharactersSeparately()
    {
        SearchScanRejected result = Assert.IsInstanceOfType<SearchScanRejected>(
            SearchQueryScanner.Scan("host\u0001found", CancellationToken.None));

        Assert.AreEqual(SearchDiagnosticCode.ControlCharacter, result.Diagnostic.Code);
        Assert.AreEqual(new SearchTextSpan(4, 1), result.Diagnostic.Span);
    }

    [TestMethod]
    public void Scan_RejectsUnpairedSurrogateSeparately()
    {
        SearchScanRejected result = Assert.IsInstanceOfType<SearchScanRejected>(
            SearchQueryScanner.Scan("host\uD800found", CancellationToken.None));

        Assert.AreEqual(SearchDiagnosticCode.InvalidUtf16, result.Diagnostic.Code);
        Assert.AreEqual(new SearchTextSpan(4, 1), result.Diagnostic.Span);
    }
}
