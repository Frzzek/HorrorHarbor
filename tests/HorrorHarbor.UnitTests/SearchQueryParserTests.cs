using System.Globalization;
using HorrorHarbor.Search.Querying;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchQueryParserTests
{
    private static readonly SearchQueryParser Parser = new();
    private static readonly string[] FoundFootageTokens = ["found", "footage"];
    private static readonly string[] PastedNoroiTitleTokens = ["noroi", "the", "curse"];
    private static readonly SearchField[] LexicalFields =
    [
        SearchField.Title,
        SearchField.Tag,
        SearchField.Lens,
        SearchField.Franchise,
        SearchField.Language,
    ];

    [TestMethod]
    public void Parse_BindsMixedHorrorQueryAndStatistics()
    {
        ParsedSearchQuery query = ParseSuccess(
            "tag:\"found footage\" year:>=2020 -extreme:true status:interested");

        Assert.AreEqual(SearchIntent.LocalSearch, query.Intent);
        Assert.HasCount(4, query.Clauses);
        Assert.AreEqual(1, query.PositiveLexicalClauseCount);
        Assert.AreEqual(1, query.ExclusionClauseCount);
        Assert.AreEqual(3, query.HardFacetClauseCount);
        Assert.AreEqual(1, query.QuotedPhraseCount);
        Assert.AreEqual(0, query.FuzzyEligibleTermCount);

        SearchLexicalClause tag = Assert.IsInstanceOfType<SearchLexicalClause>(query.Clauses[0]);
        Assert.AreEqual(SearchField.Tag, tag.Field);
        Assert.AreEqual(SearchLexicalMode.Phrase, tag.Mode);
        Assert.AreEqual("found footage", tag.NormalizedValue);
        CollectionAssert.AreEqual(FoundFootageTokens, tag.Tokens.ToArray());

        SearchNumericClause year = Assert.IsInstanceOfType<SearchNumericClause>(query.Clauses[1]);
        Assert.AreEqual(SearchField.Year, year.Field);
        Assert.AreEqual(SearchNumericOperator.GreaterThanOrEqual, year.Operator);
        Assert.AreEqual(2020, year.FirstValue);
        Assert.IsNull(year.SecondValue);

        SearchBooleanClause extreme = Assert.IsInstanceOfType<SearchBooleanClause>(query.Clauses[2]);
        Assert.AreEqual(SearchClauseOccurrence.Excluded, extreme.Occurrence);
        Assert.IsTrue(extreme.Value);

        SearchStatusClause status = Assert.IsInstanceOfType<SearchStatusClause>(query.Clauses[3]);
        Assert.AreEqual(SearchLocalStatus.Interested, status.Value);
    }

    [TestMethod]
    public void Parse_BindsEveryLexicalFieldOrdinally()
    {
        ParsedSearchQuery query = ParseSuccess(
            "TITLE:alien tag:cosmic lens:body franchise:saw language:ko");

        SearchField[] fields = query.Clauses.Select(clause => clause.Field).ToArray();
        CollectionAssert.AreEqual(LexicalFields, fields);
        Assert.AreEqual(SearchIntent.LocalSearch, query.Intent);
        Assert.AreEqual(5, query.PositiveLexicalClauseCount);
        Assert.AreEqual(3, query.FuzzyEligibleTermCount);
    }

    [TestMethod]
    [DataRow("year:2000", SearchField.Year, SearchNumericOperator.Equal, 2000, int.MinValue)]
    [DataRow("year:>=2020", SearchField.Year, SearchNumericOperator.GreaterThanOrEqual, 2020, int.MinValue)]
    [DataRow("runtime:<90", SearchField.Runtime, SearchNumericOperator.LessThan, 90, int.MinValue)]
    [DataRow("runtime:<=100", SearchField.Runtime, SearchNumericOperator.LessThanOrEqual, 100, int.MinValue)]
    [DataRow("scare:>3", SearchField.Scare, SearchNumericOperator.GreaterThan, 3, int.MinValue)]
    [DataRow("gore:4..5", SearchField.Gore, SearchNumericOperator.InclusiveRange, 4, 5)]
    public void Parse_BindsNumericForms(
        string queryText,
        SearchField expectedField,
        SearchNumericOperator expectedOperator,
        int expectedFirst,
        int expectedSecond)
    {
        ParsedSearchQuery query = ParseSuccess(queryText);

        SearchNumericClause clause = Assert.IsInstanceOfType<SearchNumericClause>(query.Clauses[0]);
        Assert.AreEqual(expectedField, clause.Field);
        Assert.AreEqual(expectedOperator, clause.Operator);
        Assert.AreEqual(expectedFirst, clause.FirstValue);
        if (expectedSecond == int.MinValue)
        {
            Assert.IsNull(clause.SecondValue);
        }
        else
        {
            Assert.AreEqual(expectedSecond, clause.SecondValue);
        }
    }

    [TestMethod]
    public void Parse_BindsBooleanAvailabilityAndStatusFacets()
    {
        ParsedSearchQuery query = ParseSuccess(
            "slasher:true extreme:false availability:unknown availability:verified " +
            "availability:shudder status:not-now");

        Assert.AreEqual(SearchIntent.FilteredDiscovery, query.Intent);
        Assert.AreEqual(6, query.HardFacetClauseCount);

        SearchBooleanClause slasher = Assert.IsInstanceOfType<SearchBooleanClause>(query.Clauses[0]);
        SearchBooleanClause extreme = Assert.IsInstanceOfType<SearchBooleanClause>(query.Clauses[1]);
        Assert.IsTrue(slasher.Value);
        Assert.IsFalse(extreme.Value);

        SearchAvailabilityClause unknown = Assert.IsInstanceOfType<SearchAvailabilityClause>(query.Clauses[2]);
        SearchAvailabilityClause verified = Assert.IsInstanceOfType<SearchAvailabilityClause>(query.Clauses[3]);
        SearchAvailabilityClause provider = Assert.IsInstanceOfType<SearchAvailabilityClause>(query.Clauses[4]);
        Assert.AreEqual(SearchAvailabilityKind.Unknown, unknown.Kind);
        Assert.AreEqual(SearchAvailabilityKind.Verified, verified.Kind);
        Assert.AreEqual(SearchAvailabilityKind.ProviderDefined, provider.Kind);
        Assert.AreEqual("shudder", provider.ProviderValue);

        SearchStatusClause status = Assert.IsInstanceOfType<SearchStatusClause>(query.Clauses[5]);
        Assert.AreEqual(SearchLocalStatus.NotNow, status.Value);
    }

    [TestMethod]
    public void Parse_TreatsPastedColonTitleAsOrdinaryLexicalText()
    {
        ParsedSearchQuery query = ParseSuccess("Noroi: The Curse");

        Assert.AreEqual(SearchIntent.LocalSearch, query.Intent);
        Assert.HasCount(3, query.Clauses);
        CollectionAssert.AreEqual(
            PastedNoroiTitleTokens,
            query.Clauses
                .Cast<SearchLexicalClause>()
                .Select(clause => clause.NormalizedValue)
                .ToArray());
        Assert.AreEqual(new SearchTextSpan(0, 6), query.Clauses[0].Span);
        Assert.IsTrue(query.Clauses.All(clause => clause.Field == SearchField.Any));
    }

    [TestMethod]
    public void Parse_ExclusionOnlyQueryIsFilteredDiscovery()
    {
        ParsedSearchQuery query = ParseSuccess("-tag:\"horror comedy\"");

        Assert.AreEqual(SearchIntent.FilteredDiscovery, query.Intent);
        Assert.AreEqual(0, query.PositiveLexicalClauseCount);
        Assert.AreEqual(1, query.ExclusionClauseCount);
        Assert.AreEqual(1, query.QuotedPhraseCount);
    }

    [TestMethod]
    public void Parse_EmptyQueryProducesEmptyFilteredQueryForApplicationRouting()
    {
        ParsedSearchQuery query = ParseSuccess(string.Empty);

        Assert.AreEqual(SearchIntent.FilteredDiscovery, query.Intent);
        Assert.IsEmpty(query.Clauses);
    }

    [TestMethod]
    [DataRow("year:2024..2000", SearchDiagnosticCode.InvalidRange)]
    [DataRow("director:carpenter", SearchDiagnosticCode.UnknownField)]
    [DataRow("tag:", SearchDiagnosticCode.MissingValue)]
    [DataRow("title:\"the descent", SearchDiagnosticCode.UnterminatedQuote)]
    [DataRow("scare:6", SearchDiagnosticCode.ValueOutOfRange)]
    [DataRow("slasher:maybe", SearchDiagnosticCode.InvalidBoolean)]
    [DataRow("runtime:fast", SearchDiagnosticCode.InvalidNumber)]
    [DataRow("year:>=", SearchDiagnosticCode.MissingValue)]
    [DataRow("year:=2020", SearchDiagnosticCode.InvalidOperator)]
    [DataRow("year:2000..>2009", SearchDiagnosticCode.InvalidOperator)]
    [DataRow("year:2000...2009", SearchDiagnosticCode.InvalidRange)]
    [DataRow("year:\"2020\"", SearchDiagnosticCode.InvalidOperator)]
    [DataRow("status:later", SearchDiagnosticCode.UnsupportedValue)]
    public void Parse_ReturnsTypedDiagnosticForInvalidQuery(
        string queryText,
        SearchDiagnosticCode expectedCode)
    {
        SearchQueryRejected result = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse(queryText));

        Assert.AreEqual(expectedCode, result.Diagnostic.Code);
    }

    [TestMethod]
    [DataRow("tag:>=cosmic", 4)]
    [DataRow("availability:>=shudder", 13)]
    [DataRow("slasher:>=true", 8)]
    [DataRow("status:>=watched", 7)]
    [DataRow("year:=2020", 5)]
    [DataRow("year:20>20", 7)]
    [DataRow("year:2000..>2009", 11)]
    public void Parse_ReportsPreciseLeadingOperatorSpanAcrossValueTypes(
        string queryText,
        int expectedStart)
    {
        SearchQueryRejected result = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse(queryText));

        Assert.AreEqual(SearchDiagnosticCode.InvalidOperator, result.Diagnostic.Code);
        Assert.AreEqual(new SearchTextSpan(expectedStart, 1), result.Diagnostic.Span);
    }

    [TestMethod]
    public void Parse_ReportsPreciseFieldAndMissingValueSpans()
    {
        SearchQueryRejected unknown = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse("director:carpenter"));
        SearchQueryRejected missing = Assert.IsInstanceOfType<SearchQueryRejected>(
            Parser.Parse("tag:"));

        Assert.AreEqual(new SearchTextSpan(0, 8), unknown.Diagnostic.Span);
        Assert.AreEqual(new SearchTextSpan(4, 0), missing.Diagnostic.Span);
    }

    [TestMethod]
    public void Parse_AppliesPhraseTokenAndTokenLengthLimits()
    {
        string ninePhrases = string.Join(' ', Enumerable.Repeat("\"a\"", 9));
        string thirteenTokenPhrase = $"\"{string.Join(' ', Enumerable.Repeat("a", 13))}\"";
        string longToken = new('a', SearchGrammarLimits.MaximumNormalizedTokenRunes + 1);

        AssertDiagnostic(ninePhrases, SearchDiagnosticCode.PhraseLimitExceeded);
        AssertDiagnostic(thirteenTokenPhrase, SearchDiagnosticCode.PhraseTokenLimitExceeded);
        AssertDiagnostic(longToken, SearchDiagnosticCode.TokenTooLong);
    }

    [TestMethod]
    public void Parse_AppliesClauseCategoryAndFuzzyTermLimits()
    {
        string positiveClauses = string.Join(' ', Enumerable.Repeat("a", 17));
        string exclusionClauses = string.Join(' ', Enumerable.Repeat("-a", 17));
        string hardFacets = string.Join(' ', Enumerable.Repeat("year:2000", 17));

        AssertDiagnostic(positiveClauses, SearchDiagnosticCode.ClauseLimitExceeded);
        AssertDiagnostic(exclusionClauses, SearchDiagnosticCode.ClauseLimitExceeded);
        AssertDiagnostic(hardFacets, SearchDiagnosticCode.ClauseLimitExceeded);
        AssertDiagnostic("alpha bravo charlie delta echo", SearchDiagnosticCode.FuzzyTermLimitExceeded);
    }

    [TestMethod]
    public void Parse_IsInvariantUnderTurkishCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo turkish = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;

            ParsedSearchQuery query = ParseSuccess("TITLE:ALIEN YEAR:>=2020");

            SearchLexicalClause title = Assert.IsInstanceOfType<SearchLexicalClause>(query.Clauses[0]);
            SearchNumericClause year = Assert.IsInstanceOfType<SearchNumericClause>(query.Clauses[1]);
            Assert.AreEqual("alien", title.NormalizedValue);
            Assert.AreEqual(2020, year.FirstValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [TestMethod]
    public void Parse_QuotedSqlLookingTextRemainsLexicalData()
    {
        ParsedSearchQuery query = ParseSuccess("title:\"x' OR 1=1 --\"");

        SearchLexicalClause clause = Assert.IsInstanceOfType<SearchLexicalClause>(query.Clauses[0]);
        Assert.AreEqual(SearchField.Title, clause.Field);
        Assert.AreEqual(SearchLexicalMode.Phrase, clause.Mode);
        Assert.AreEqual("x or 1 1", clause.NormalizedValue);
    }

    [TestMethod]
    public void Parse_PreCanceledTokenThrowsOperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => Parser.Parse("descent", source.Token));
    }

    private static ParsedSearchQuery ParseSuccess(string queryText)
    {
        SearchQueryParsed parsed = Assert.IsInstanceOfType<SearchQueryParsed>(Parser.Parse(queryText));
        return parsed.Query;
    }

    private static void AssertDiagnostic(string queryText, SearchDiagnosticCode expectedCode)
    {
        SearchQueryRejected rejected = Assert.IsInstanceOfType<SearchQueryRejected>(Parser.Parse(queryText));
        Assert.AreEqual(expectedCode, rejected.Diagnostic.Code);
    }
}
