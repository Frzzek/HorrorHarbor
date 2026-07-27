using System.Reflection;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.SecurityTests;

[TestClass]
public sealed class SearchQueryParserSecurityTests
{
    private static readonly SearchQueryParser Parser = new();

    [TestMethod]
    [DataRow("host\u202Efound", SearchDiagnosticCode.UnsafeFormatCharacter, 4, 1)]
    [DataRow("𐐀\u202Ehost", SearchDiagnosticCode.UnsafeFormatCharacter, 2, 1)]
    [DataRow("host\u0001found", SearchDiagnosticCode.ControlCharacter, 4, 1)]
    public void Parse_RejectsUnsafeUnicodeWithUtf16Spans(
        string queryText,
        SearchDiagnosticCode expectedCode,
        int expectedStart,
        int expectedLength)
    {
        SearchQueryRejected result = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse(queryText));

        Assert.AreEqual(expectedCode, result.Diagnostic.Code);
        Assert.AreEqual(
            new SearchTextSpan(expectedStart, expectedLength),
            result.Diagnostic.Span);
    }

    [TestMethod]
    public void Parse_RejectsUnpairedSurrogateWithUtf16Span()
    {
        string queryText = "host\uD800found";

        SearchQueryRejected result = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse(queryText));

        Assert.AreEqual(SearchDiagnosticCode.InvalidUtf16, result.Diagnostic.Code);
        Assert.AreEqual(new SearchTextSpan(4, 1), result.Diagnostic.Span);
    }

    [TestMethod]
    public void Parse_AllowsJoinControlsRequiredBySomeWritingSystems()
    {
        SearchQueryParsed result = Assert.IsInstanceOfType<SearchQueryParsed>(
            Parser.Parse("क्‍ष"));

        SearchLexicalClause clause = Assert.IsInstanceOfType<SearchLexicalClause>(
            result.Query.Clauses[0]);
        Assert.AreEqual("क्‍ष", clause.NormalizedValue);
    }

    [TestMethod]
    public void Parse_EnforcesRawQueryLimitBeforeFurtherProcessing()
    {
        string maximumLengthQuery = new(' ', SearchGrammarLimits.MaximumRawQueryCodeUnits);
        string oversizedQuery = maximumLengthQuery + " ";

        SearchQueryParsed accepted = Assert.IsInstanceOfType<SearchQueryParsed>(
            Parser.Parse(maximumLengthQuery));
        SearchQueryRejected rejected = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse(oversizedQuery));

        Assert.IsEmpty(accepted.Query.Clauses);
        Assert.AreEqual(SearchDiagnosticCode.QueryTooLong, rejected.Diagnostic.Code);
        Assert.AreEqual(
            new SearchTextSpan(SearchGrammarLimits.MaximumRawQueryCodeUnits, 1),
            rejected.Diagnostic.Span);
    }

    [TestMethod]
    public void Parse_EnforcesTotalClauseLimitAtMaximumLegalCategoryShape()
    {
        string positiveClauses = string.Join(
            ' ',
            Enumerable.Repeat("a", SearchGrammarLimits.MaximumPositiveLexicalClauses));
        string hardFacets = string.Join(
            ' ',
            Enumerable.Repeat("year:2000", SearchGrammarLimits.MaximumHardFacetClauses));
        string maximumClauseQuery = $"{positiveClauses} {hardFacets}";
        string oversizedClauseQuery = $"{maximumClauseQuery} b";

        SearchQueryParsed accepted = Assert.IsInstanceOfType<SearchQueryParsed>(
            Parser.Parse(maximumClauseQuery));
        SearchQueryRejected rejected = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse(oversizedClauseQuery));

        Assert.HasCount(SearchGrammarLimits.MaximumTotalClauses, accepted.Query.Clauses);
        Assert.AreEqual(SearchDiagnosticCode.ClauseLimitExceeded, rejected.Diagnostic.Code);
        Assert.AreEqual(
            new SearchTextSpan(maximumClauseQuery.Length + 1, 1),
            rejected.Diagnostic.Span);
    }

    [TestMethod]
    public void Parse_ReturnsNoPartialQueryWhenALaterClauseIsInvalid()
    {
        SearchQueryParseResult result = Parser.Parse("descent year:not-a-number");

        SearchQueryRejected rejected = Assert.IsInstanceOfType<SearchQueryRejected>(result);
        Assert.AreEqual(SearchDiagnosticCode.InvalidNumber, rejected.Diagnostic.Code);
    }

    [TestMethod]
    public void Parse_DoesNotRetainRawQueryTextInResultObjects()
    {
        const string queryText = "title:SéCrÉt!!! availability:ShUdDeR";
        SearchQueryParsed result = Assert.IsInstanceOfType<SearchQueryParsed>(
            Parser.Parse(queryText));

        AssertObjectDoesNotRetainString(result, queryText);
        AssertObjectDoesNotRetainString(result.Query, queryText);
        foreach (SearchQueryClause clause in result.Query.Clauses)
        {
            AssertObjectDoesNotRetainString(clause, queryText);
        }

        SearchLexicalClause title = Assert.IsInstanceOfType<SearchLexicalClause>(
            result.Query.Clauses[0]);
        SearchAvailabilityClause availability = Assert.IsInstanceOfType<SearchAvailabilityClause>(
            result.Query.Clauses[1]);
        Assert.AreEqual("secret", title.NormalizedValue);
        Assert.AreEqual("shudder", availability.ProviderValue);
    }

    [TestMethod]
    public void Parse_PreCanceledRequestPerformsNoParsing()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => Parser.Parse("title:descent", source.Token));
    }

    private static void AssertObjectDoesNotRetainString(object value, string forbiddenValue)
    {
        FieldInfo? retainedField = value
            .GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(field =>
                field.GetValue(value) is string retained &&
                retained.Equals(forbiddenValue, StringComparison.Ordinal));

        Assert.IsNull(
            retainedField,
            $"{value.GetType().Name} retained the complete raw query in {retainedField?.Name}.");
    }
}
