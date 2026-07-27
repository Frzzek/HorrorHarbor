using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Querying;

internal readonly record struct ScannedSearchClause(
    SearchTextSpan? FieldSpan,
    SearchTextSpan ValueSpan,
    SearchTextSpan Span,
    SearchClauseOccurrence Occurrence,
    bool IsQuoted);

internal abstract record SearchScanResult
{
    private protected SearchScanResult()
    {
    }
}

internal sealed record SearchScanSucceeded(
    ImmutableArray<ScannedSearchClause> Clauses) : SearchScanResult;

internal sealed record SearchScanRejected(
    SearchDiagnostic Diagnostic) : SearchScanResult;

internal static class SearchQueryScanner
{
    public static SearchScanResult Scan(
        string queryText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        cancellationToken.ThrowIfCancellationRequested();

        if (queryText.Length > SearchGrammarLimits.MaximumRawQueryCodeUnits)
        {
            return Reject(
                SearchDiagnosticCode.QueryTooLong,
                SearchGrammarLimits.MaximumRawQueryCodeUnits,
                queryText.Length - SearchGrammarLimits.MaximumRawQueryCodeUnits);
        }

        SearchScanRejected? validationFailure = ValidateCharacters(queryText, cancellationToken);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var clauses = ImmutableArray.CreateBuilder<ScannedSearchClause>();
        int index = 0;

        while (index < queryText.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SkipWhitespace(queryText, ref index);
            if (index >= queryText.Length)
            {
                break;
            }

            int clauseStart = index;
            SearchClauseOccurrence occurrence = SearchClauseOccurrence.Positive;
            if (queryText[index] == '-')
            {
                occurrence = SearchClauseOccurrence.Excluded;
                index++;

                if (index >= queryText.Length || char.IsWhiteSpace(queryText[index]))
                {
                    return Reject(SearchDiagnosticCode.MissingValue, index, 0);
                }
            }

            ClauseScanResult clauseResult = ScanClause(
                queryText,
                clauseStart,
                index,
                occurrence);
            if (clauseResult.Diagnostic is not null)
            {
                return new SearchScanRejected(clauseResult.Diagnostic);
            }

            ScannedSearchClause clause = clauseResult.Clause;
            clauses.Add(clause);
            index = clause.Span.End;
        }

        return new SearchScanSucceeded(clauses.ToImmutable());
    }

    private static ClauseScanResult ScanClause(
        string queryText,
        int clauseStart,
        int contentStart,
        SearchClauseOccurrence occurrence)
    {
        if (queryText[contentStart] == '"')
        {
            return ScanQuotedClause(
                queryText,
                clauseStart,
                contentStart,
                fieldSpan: null,
                occurrence);
        }

        int cursor = contentStart;
        int colonIndex = -1;
        while (cursor < queryText.Length && !char.IsWhiteSpace(queryText[cursor]))
        {
            if (queryText[cursor] == ':')
            {
                colonIndex = cursor;
                break;
            }

            if (queryText[cursor] == '"')
            {
                return ClauseScanResult.Rejected(
                    SearchDiagnosticCode.InvalidOperator,
                    cursor,
                    1);
            }

            cursor++;
        }

        if (colonIndex < 0)
        {
            int valueLength = cursor - contentStart;
            return ClauseScanResult.Success(new ScannedSearchClause(
                FieldSpan: null,
                new SearchTextSpan(contentStart, valueLength),
                new SearchTextSpan(clauseStart, cursor - clauseStart),
                occurrence,
                IsQuoted: false));
        }

        if (colonIndex == contentStart)
        {
            return ClauseScanResult.Rejected(
                SearchDiagnosticCode.UnknownField,
                colonIndex,
                1);
        }

        var fieldSpan = new SearchTextSpan(contentStart, colonIndex - contentStart);
        int valueStart = colonIndex + 1;
        if (valueStart >= queryText.Length || char.IsWhiteSpace(queryText[valueStart]))
        {
            return ClauseScanResult.Success(new ScannedSearchClause(
                fieldSpan,
                new SearchTextSpan(valueStart, 0),
                new SearchTextSpan(clauseStart, valueStart - clauseStart),
                occurrence,
                IsQuoted: false));
        }

        if (queryText[valueStart] == '"')
        {
            return ScanQuotedClause(
                queryText,
                clauseStart,
                valueStart,
                fieldSpan,
                occurrence);
        }

        cursor = valueStart;
        while (cursor < queryText.Length && !char.IsWhiteSpace(queryText[cursor]))
        {
            if (queryText[cursor] == '"')
            {
                return ClauseScanResult.Rejected(
                    SearchDiagnosticCode.InvalidOperator,
                    cursor,
                    1);
            }

            cursor++;
        }

        int unquotedLength = cursor - valueStart;
        return ClauseScanResult.Success(new ScannedSearchClause(
            fieldSpan,
            new SearchTextSpan(valueStart, unquotedLength),
            new SearchTextSpan(clauseStart, cursor - clauseStart),
            occurrence,
            IsQuoted: false));
    }

    private static ClauseScanResult ScanQuotedClause(
        string queryText,
        int clauseStart,
        int openingQuoteIndex,
        SearchTextSpan? fieldSpan,
        SearchClauseOccurrence occurrence)
    {
        int valueStart = openingQuoteIndex + 1;
        int closingQuoteIndex = queryText.IndexOf('"', valueStart);
        if (closingQuoteIndex < 0)
        {
            return ClauseScanResult.Rejected(
                SearchDiagnosticCode.UnterminatedQuote,
                openingQuoteIndex,
                queryText.Length - openingQuoteIndex);
        }

        int clauseEnd = closingQuoteIndex + 1;
        if (clauseEnd < queryText.Length && !char.IsWhiteSpace(queryText[clauseEnd]))
        {
            return ClauseScanResult.Rejected(
                SearchDiagnosticCode.InvalidOperator,
                clauseEnd,
                1);
        }

        int valueLength = closingQuoteIndex - valueStart;
        if (valueLength == 0)
        {
            return ClauseScanResult.Rejected(
                SearchDiagnosticCode.MissingValue,
                valueStart,
                0);
        }

        return ClauseScanResult.Success(new ScannedSearchClause(
            fieldSpan,
            new SearchTextSpan(valueStart, valueLength),
            new SearchTextSpan(clauseStart, clauseEnd - clauseStart),
            occurrence,
            IsQuoted: true));
    }

    private static SearchScanRejected? ValidateCharacters(
        string queryText,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < queryText.Length;)
        {
            if ((index & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            char character = queryText[index];
            Rune rune;
            int runeLength;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= queryText.Length || !char.IsLowSurrogate(queryText[index + 1]))
                {
                    return Reject(SearchDiagnosticCode.InvalidUtf16, index, 1);
                }

                rune = new Rune(character, queryText[index + 1]);
                runeLength = 2;
            }
            else if (char.IsLowSurrogate(character))
            {
                return Reject(SearchDiagnosticCode.InvalidUtf16, index, 1);
            }
            else
            {
                rune = new Rune(character);
                runeLength = 1;
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                return Reject(SearchDiagnosticCode.ControlCharacter, index, runeLength);
            }

            if (SearchCharacterPolicy.IsUnsafeFormatCharacter(rune))
            {
                return Reject(SearchDiagnosticCode.UnsafeFormatCharacter, index, runeLength);
            }

            index += runeLength;
        }

        return null;
    }

    private static void SkipWhitespace(string queryText, ref int index)
    {
        while (index < queryText.Length && char.IsWhiteSpace(queryText[index]))
        {
            index++;
        }
    }

    private static SearchScanRejected Reject(
        SearchDiagnosticCode code,
        int start,
        int length) => new(new SearchDiagnostic(code, new SearchTextSpan(start, length)));

    private readonly record struct ClauseScanResult(
        ScannedSearchClause Clause,
        SearchDiagnostic? Diagnostic)
    {
        public static ClauseScanResult Success(ScannedSearchClause clause) => new(clause, null);

        public static ClauseScanResult Rejected(
            SearchDiagnosticCode code,
            int start,
            int length) => new(default, new SearchDiagnostic(code, new SearchTextSpan(start, length)));
    }
}
