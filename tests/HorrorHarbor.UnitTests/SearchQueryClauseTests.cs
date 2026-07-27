using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchQueryClauseTests
{
    private static readonly SearchTextSpan Span = new(0, 1);

    [TestMethod]
    public void NumericClause_RequiresSecondValueOnlyForInclusiveRange()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SearchNumericClause(
            SearchField.Year,
            SearchClauseOccurrence.Positive,
            SearchNumericOperator.InclusiveRange,
            2000,
            null,
            Span));

        Assert.ThrowsExactly<ArgumentException>(() => new SearchNumericClause(
            SearchField.Year,
            SearchClauseOccurrence.Positive,
            SearchNumericOperator.Equal,
            2000,
            2001,
            Span));
    }

    [TestMethod]
    public void NumericClause_RejectsDescendingRange()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchNumericClause(
            SearchField.Year,
            SearchClauseOccurrence.Positive,
            SearchNumericOperator.InclusiveRange,
            2024,
            2000,
            Span));
    }

    [TestMethod]
    public void NumericClause_RejectsNonNumericField()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchNumericClause(
            SearchField.Title,
            SearchClauseOccurrence.Positive,
            SearchNumericOperator.Equal,
            2000,
            null,
            Span));
    }

    [TestMethod]
    public void AvailabilityClause_RequiresProviderValueOnlyForProviderDefinedKind()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SearchAvailabilityClause(
            SearchClauseOccurrence.Positive,
            SearchAvailabilityKind.ProviderDefined,
            null,
            Span));

        Assert.ThrowsExactly<ArgumentException>(() => new SearchAvailabilityClause(
            SearchClauseOccurrence.Positive,
            SearchAvailabilityKind.Unknown,
            "provider-value",
            Span));
    }

    [TestMethod]
    public void AvailabilityClause_PreservesValidProviderDefinedValue()
    {
        var clause = new SearchAvailabilityClause(
            SearchClauseOccurrence.Positive,
            SearchAvailabilityKind.ProviderDefined,
            "provider-value",
            Span);

        Assert.AreEqual(SearchAvailabilityKind.ProviderDefined, clause.Kind);
        Assert.AreEqual("provider-value", clause.ProviderValue);
    }

    [TestMethod]
    public void LexicalClause_RejectsEmptyNormalizedValue()
    {
        NormalizedSearchText normalized = SearchTextNormalizerV1.Normalize("---");

        Assert.ThrowsExactly<ArgumentException>(() => new SearchLexicalClause(
            SearchField.Any,
            SearchClauseOccurrence.Positive,
            SearchLexicalMode.Token,
            normalized,
            Span));
    }

    [TestMethod]
    public void BooleanAndLexicalClauses_RejectWrongFieldKinds()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchBooleanClause(
            SearchField.Tag,
            SearchClauseOccurrence.Positive,
            true,
            Span));

        NormalizedSearchText normalized = SearchTextNormalizerV1.Normalize("cosmic");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SearchLexicalClause(
            SearchField.Year,
            SearchClauseOccurrence.Positive,
            SearchLexicalMode.Token,
            normalized,
            Span));
    }
}
