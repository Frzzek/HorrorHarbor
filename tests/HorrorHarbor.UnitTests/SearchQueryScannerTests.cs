using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchQueryScannerTests
{
    [TestMethod]
    public void Scan_ProducesExactSpansForExcludedQuotedField()
    {
        SearchScanSucceeded result = Assert.IsInstanceOfType<SearchScanSucceeded>(
            SearchQueryScanner.Scan("-title:\"you're next\"", CancellationToken.None));

        Assert.HasCount(1, result.Clauses);
        ScannedSearchClause clause = result.Clauses[0];
        Assert.AreEqual(new SearchTextSpan(1, 5), clause.FieldSpan);
        Assert.AreEqual(new SearchTextSpan(8, 11), clause.ValueSpan);
        Assert.AreEqual(new SearchTextSpan(0, 20), clause.Span);
        Assert.AreEqual(SearchClauseOccurrence.Excluded, clause.Occurrence);
        Assert.IsTrue(clause.IsQuoted);
    }

    [TestMethod]
    public void Scan_PreservesEmptyFieldShapedClauseForBinderResolution()
    {
        SearchScanSucceeded result = Assert.IsInstanceOfType<SearchScanSucceeded>(
            SearchQueryScanner.Scan("Noroi: The Curse", CancellationToken.None));

        Assert.HasCount(3, result.Clauses);
        Assert.AreEqual(new SearchTextSpan(0, 5), result.Clauses[0].FieldSpan);
        Assert.AreEqual(new SearchTextSpan(6, 0), result.Clauses[0].ValueSpan);
        Assert.AreEqual(new SearchTextSpan(0, 6), result.Clauses[0].Span);
    }

    [TestMethod]
    public void Scan_PreservesKnownEmptyFieldForMissingValueDiagnosticInBinder()
    {
        SearchScanSucceeded result = Assert.IsInstanceOfType<SearchScanSucceeded>(
            SearchQueryScanner.Scan("tag:", CancellationToken.None));

        Assert.HasCount(1, result.Clauses);
        Assert.AreEqual(new SearchTextSpan(0, 3), result.Clauses[0].FieldSpan);
        Assert.AreEqual(new SearchTextSpan(4, 0), result.Clauses[0].ValueSpan);
    }

    [TestMethod]
    public void Scan_ProducesFieldAndValueSpansWithoutCopyingRawText()
    {
        const string Query = "director:carpenter";
        SearchScanSucceeded result = Assert.IsInstanceOfType<SearchScanSucceeded>(
            SearchQueryScanner.Scan(Query, CancellationToken.None));

        ScannedSearchClause clause = result.Clauses[0];
        Assert.AreEqual("director", Query[clause.FieldSpan!.Value.Start..clause.FieldSpan.Value.End]);
        Assert.AreEqual("carpenter", Query[clause.ValueSpan.Start..clause.ValueSpan.End]);
    }

    [TestMethod]
    public void Scan_ValidBareQueryReturnsSuccessInsteadOfThrowing()
    {
        SearchScanSucceeded result = Assert.IsInstanceOfType<SearchScanSucceeded>(
            SearchQueryScanner.Scan("descent", CancellationToken.None));

        Assert.HasCount(1, result.Clauses);
        Assert.AreEqual(new SearchTextSpan(0, 7), result.Clauses[0].ValueSpan);
    }
}
