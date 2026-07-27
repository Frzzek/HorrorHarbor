using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Querying;

internal static class SearchQueryBinder
{
    public static SearchQueryParseResult Bind(
        string queryText,
        ImmutableArray<ScannedSearchClause> scannedClauses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        if (scannedClauses.IsDefault)
        {
            throw new ArgumentException("Scanned clauses must be initialized.", nameof(scannedClauses));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (scannedClauses.Length > SearchGrammarLimits.MaximumTotalClauses)
        {
            ScannedSearchClause offendingClause = scannedClauses[SearchGrammarLimits.MaximumTotalClauses];
            return Reject(SearchDiagnosticCode.ClauseLimitExceeded, offendingClause.Span);
        }

        var clauses = ImmutableArray.CreateBuilder<SearchQueryClause>(scannedClauses.Length);
        int positiveLexicalClauseCount = 0;
        int exclusionClauseCount = 0;
        int hardFacetClauseCount = 0;
        int quotedPhraseCount = 0;
        int fuzzyEligibleTermCount = 0;

        foreach (ScannedSearchClause scannedClause in scannedClauses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ClauseBinding binding = BindClause(queryText, scannedClause);
            if (binding.Diagnostic is not null)
            {
                return new SearchQueryRejected(binding.Diagnostic);
            }

            SearchQueryClause clause = binding.Clause!;
            bool isLexical = clause is SearchLexicalClause;
            int nextExclusionCount = exclusionClauseCount +
                (clause.Occurrence == SearchClauseOccurrence.Excluded ? 1 : 0);
            int nextPositiveLexicalCount = positiveLexicalClauseCount +
                (isLexical && clause.Occurrence == SearchClauseOccurrence.Positive ? 1 : 0);
            int nextHardFacetCount = hardFacetClauseCount + (isLexical ? 0 : 1);

            if (nextExclusionCount > SearchGrammarLimits.MaximumExclusionClauses ||
                nextPositiveLexicalCount > SearchGrammarLimits.MaximumPositiveLexicalClauses ||
                nextHardFacetCount > SearchGrammarLimits.MaximumHardFacetClauses)
            {
                return Reject(SearchDiagnosticCode.ClauseLimitExceeded, scannedClause.Span);
            }

            int nextQuotedPhraseCount = quotedPhraseCount;
            int nextFuzzyEligibleTermCount = fuzzyEligibleTermCount;
            if (clause is SearchLexicalClause lexicalClause)
            {
                if (lexicalClause.Mode == SearchLexicalMode.Phrase)
                {
                    nextQuotedPhraseCount++;
                    if (nextQuotedPhraseCount > SearchGrammarLimits.MaximumQuotedPhrases)
                    {
                        return Reject(SearchDiagnosticCode.PhraseLimitExceeded, scannedClause.ValueSpan);
                    }
                }
                else if (clause.Occurrence == SearchClauseOccurrence.Positive)
                {
                    nextFuzzyEligibleTermCount += CountFuzzyEligibleTerms(lexicalClause.Tokens);
                    if (nextFuzzyEligibleTermCount > SearchGrammarLimits.MaximumFuzzyEligibleTerms)
                    {
                        return Reject(SearchDiagnosticCode.FuzzyTermLimitExceeded, scannedClause.ValueSpan);
                    }
                }
            }

            clauses.Add(clause);
            exclusionClauseCount = nextExclusionCount;
            positiveLexicalClauseCount = nextPositiveLexicalCount;
            hardFacetClauseCount = nextHardFacetCount;
            quotedPhraseCount = nextQuotedPhraseCount;
            fuzzyEligibleTermCount = nextFuzzyEligibleTermCount;
        }

        SearchIntent intent = positiveLexicalClauseCount > 0
            ? SearchIntent.LocalSearch
            : SearchIntent.FilteredDiscovery;
        var query = new ParsedSearchQuery(
            intent,
            clauses.ToImmutable(),
            positiveLexicalClauseCount,
            exclusionClauseCount,
            hardFacetClauseCount,
            quotedPhraseCount,
            fuzzyEligibleTermCount);
        return new SearchQueryParsed(query);
    }

    private static ClauseBinding BindClause(
        string queryText,
        ScannedSearchClause scannedClause)
    {
        if (scannedClause.FieldSpan is not SearchTextSpan fieldSpan)
        {
            return BindLexicalClause(queryText, scannedClause, SearchField.Any, scannedClause.ValueSpan);
        }

        ReadOnlySpan<char> fieldName = Slice(queryText, fieldSpan);
        if (!TryResolveField(fieldName, out SearchField field))
        {
            if (scannedClause.ValueSpan.Length > 0)
            {
                return ClauseBinding.Rejected(SearchDiagnosticCode.UnknownField, fieldSpan);
            }

            var pastedTitleSpan = new SearchTextSpan(
                fieldSpan.Start,
                scannedClause.Span.End - fieldSpan.Start);
            return BindLexicalClause(queryText, scannedClause, SearchField.Any, pastedTitleSpan);
        }

        if (scannedClause.ValueSpan.Length == 0)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.MissingValue, scannedClause.ValueSpan);
        }

        return field switch
        {
            SearchField.Title or
            SearchField.Tag or
            SearchField.Lens or
            SearchField.Franchise or
            SearchField.Language => BindLexicalClause(
                queryText,
                scannedClause,
                field,
                scannedClause.ValueSpan),
            SearchField.Year or
            SearchField.Runtime or
            SearchField.Scare or
            SearchField.Gore or
            SearchField.Dread => BindNumericClause(queryText, scannedClause, field),
            SearchField.Slasher or
            SearchField.Extreme => BindBooleanClause(queryText, scannedClause, field),
            SearchField.Availability => BindAvailabilityClause(queryText, scannedClause),
            SearchField.Status => BindStatusClause(queryText, scannedClause),
            _ => throw new InvalidOperationException("Search field has no binding policy."),
        };
    }

    private static ClauseBinding BindLexicalClause(
        string queryText,
        ScannedSearchClause scannedClause,
        SearchField field,
        SearchTextSpan sourceSpan)
    {
        ReadOnlySpan<char> value = Slice(queryText, sourceSpan);
        if (field != SearchField.Any &&
            !scannedClause.IsQuoted &&
            TryGetLeadingOperatorDiagnostic(value, sourceSpan, out SearchDiagnostic? operatorDiagnostic))
        {
            return new ClauseBinding(null, operatorDiagnostic);
        }

        NormalizedSearchText normalized = SearchTextNormalizerV1.Normalize(value.ToString());
        if (normalized.Value.Length == 0 || normalized.Tokens.IsDefaultOrEmpty)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.MissingValue, sourceSpan);
        }

        SearchDiagnostic? tokenDiagnostic = ValidateNormalizedTokens(normalized.Tokens, sourceSpan);
        if (tokenDiagnostic is not null)
        {
            return new ClauseBinding(null, tokenDiagnostic);
        }

        SearchLexicalMode mode = scannedClause.IsQuoted
            ? SearchLexicalMode.Phrase
            : SearchLexicalMode.Token;
        if (mode == SearchLexicalMode.Phrase &&
            normalized.Tokens.Length > SearchGrammarLimits.MaximumTokensPerPhrase)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.PhraseTokenLimitExceeded, sourceSpan);
        }

        return ClauseBinding.Success(new SearchLexicalClause(
            field,
            scannedClause.Occurrence,
            mode,
            normalized,
            scannedClause.Span));
    }

    private static ClauseBinding BindNumericClause(
        string queryText,
        ScannedSearchClause scannedClause,
        SearchField field)
    {
        if (scannedClause.IsQuoted)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidOperator, scannedClause.ValueSpan);
        }

        ReadOnlySpan<char> value = Slice(queryText, scannedClause.ValueSpan);
        int numberStart = 0;
        SearchNumericOperator numericOperator = SearchNumericOperator.Equal;
        if (value.StartsWith(">=", StringComparison.Ordinal))
        {
            numericOperator = SearchNumericOperator.GreaterThanOrEqual;
            numberStart = 2;
        }
        else if (value.StartsWith("<=", StringComparison.Ordinal))
        {
            numericOperator = SearchNumericOperator.LessThanOrEqual;
            numberStart = 2;
        }
        else if (value[0] == '>')
        {
            numericOperator = SearchNumericOperator.GreaterThan;
            numberStart = 1;
        }
        else if (value[0] == '<')
        {
            numericOperator = SearchNumericOperator.LessThan;
            numberStart = 1;
        }
        else if (IsOperatorCharacter(value[0]))
        {
            return ClauseBinding.Rejected(
                SearchDiagnosticCode.InvalidOperator,
                new SearchTextSpan(scannedClause.ValueSpan.Start, 1));
        }

        if (numberStart == value.Length)
        {
            return ClauseBinding.Rejected(
                SearchDiagnosticCode.MissingValue,
                new SearchTextSpan(scannedClause.ValueSpan.End, 0));
        }

        ReadOnlySpan<char> numericValue = value[numberStart..];
        var numericValueSpan = new SearchTextSpan(
            scannedClause.ValueSpan.Start + numberStart,
            numericValue.Length);
        if (TryGetOperatorDiagnostic(
            numericValue,
            numericValueSpan,
            out SearchDiagnostic? embeddedOperatorDiagnostic))
        {
            return new ClauseBinding(null, embeddedOperatorDiagnostic);
        }

        int rangeSeparator = numericValue.IndexOf("..", StringComparison.Ordinal);
        if (numberStart > 0 && rangeSeparator >= 0)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidOperator, scannedClause.ValueSpan);
        }

        if (rangeSeparator >= 0)
        {
            ReadOnlySpan<char> firstText = numericValue[..rangeSeparator];
            ReadOnlySpan<char> secondText = numericValue[(rangeSeparator + 2)..];
            if (firstText.Length == 0 ||
                secondText.Length == 0 ||
                firstText.Contains('.') ||
                secondText.Contains('.'))
            {
                return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidRange, scannedClause.ValueSpan);
            }

            if (!TryParseInteger(firstText, out int firstValue))
            {
                return ClauseBinding.Rejected(
                    SearchDiagnosticCode.InvalidNumber,
                    new SearchTextSpan(scannedClause.ValueSpan.Start, firstText.Length));
            }

            if (!TryParseInteger(secondText, out int secondValue))
            {
                return ClauseBinding.Rejected(
                    SearchDiagnosticCode.InvalidNumber,
                    new SearchTextSpan(
                        scannedClause.ValueSpan.Start + rangeSeparator + 2,
                        secondText.Length));
            }

            if (secondValue < firstValue)
            {
                return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidRange, scannedClause.ValueSpan);
            }

            SearchDiagnostic? domainDiagnostic = ValidateNumericDomain(
                field,
                firstValue,
                new SearchTextSpan(scannedClause.ValueSpan.Start, firstText.Length));
            domainDiagnostic ??= ValidateNumericDomain(
                field,
                secondValue,
                new SearchTextSpan(
                    scannedClause.ValueSpan.Start + rangeSeparator + 2,
                    secondText.Length));
            if (domainDiagnostic is not null)
            {
                return new ClauseBinding(null, domainDiagnostic);
            }

            return ClauseBinding.Success(new SearchNumericClause(
                field,
                scannedClause.Occurrence,
                SearchNumericOperator.InclusiveRange,
                firstValue,
                secondValue,
                scannedClause.Span));
        }

        if (!TryParseInteger(numericValue, out int valueNumber))
        {
            return ClauseBinding.Rejected(
                SearchDiagnosticCode.InvalidNumber,
                new SearchTextSpan(
                    scannedClause.ValueSpan.Start + numberStart,
                    numericValue.Length));
        }

        SearchDiagnostic? singleDomainDiagnostic = ValidateNumericDomain(
            field,
            valueNumber,
            new SearchTextSpan(
                scannedClause.ValueSpan.Start + numberStart,
                numericValue.Length));
        if (singleDomainDiagnostic is not null)
        {
            return new ClauseBinding(null, singleDomainDiagnostic);
        }

        return ClauseBinding.Success(new SearchNumericClause(
            field,
            scannedClause.Occurrence,
            numericOperator,
            valueNumber,
            secondValue: null,
            scannedClause.Span));
    }

    private static ClauseBinding BindBooleanClause(
        string queryText,
        ScannedSearchClause scannedClause,
        SearchField field)
    {
        if (scannedClause.IsQuoted)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidOperator, scannedClause.ValueSpan);
        }

        ReadOnlySpan<char> value = Slice(queryText, scannedClause.ValueSpan);
        bool parsedValue;
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = true;
        }
        else if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = false;
        }
        else if (TryGetLeadingOperatorDiagnostic(
            value,
            scannedClause.ValueSpan,
            out SearchDiagnostic? operatorDiagnostic))
        {
            return new ClauseBinding(null, operatorDiagnostic);
        }
        else
        {
            return ClauseBinding.Rejected(
                SearchDiagnosticCode.InvalidBoolean,
                scannedClause.ValueSpan);
        }

        return ClauseBinding.Success(new SearchBooleanClause(
            field,
            scannedClause.Occurrence,
            parsedValue,
            scannedClause.Span));
    }

    private static ClauseBinding BindAvailabilityClause(
        string queryText,
        ScannedSearchClause scannedClause)
    {
        if (scannedClause.IsQuoted)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidOperator, scannedClause.ValueSpan);
        }

        ReadOnlySpan<char> value = Slice(queryText, scannedClause.ValueSpan);
        if (TryGetLeadingOperatorDiagnostic(
            value,
            scannedClause.ValueSpan,
            out SearchDiagnostic? operatorDiagnostic))
        {
            return new ClauseBinding(null, operatorDiagnostic);
        }

        if (value.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return ClauseBinding.Success(new SearchAvailabilityClause(
                scannedClause.Occurrence,
                SearchAvailabilityKind.Unknown,
                providerValue: null,
                scannedClause.Span));
        }

        if (value.Equals("verified", StringComparison.OrdinalIgnoreCase))
        {
            return ClauseBinding.Success(new SearchAvailabilityClause(
                scannedClause.Occurrence,
                SearchAvailabilityKind.Verified,
                providerValue: null,
                scannedClause.Span));
        }

        NormalizedSearchText normalized = SearchTextNormalizerV1.Normalize(value.ToString());
        if (normalized.Value.Length == 0 || normalized.Tokens.IsDefaultOrEmpty)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.UnsupportedValue, scannedClause.ValueSpan);
        }

        SearchDiagnostic? tokenDiagnostic = ValidateNormalizedTokens(
            normalized.Tokens,
            scannedClause.ValueSpan);
        if (tokenDiagnostic is not null)
        {
            return new ClauseBinding(null, tokenDiagnostic);
        }

        return ClauseBinding.Success(new SearchAvailabilityClause(
            scannedClause.Occurrence,
            SearchAvailabilityKind.ProviderDefined,
            normalized.Value,
            scannedClause.Span));
    }

    private static ClauseBinding BindStatusClause(
        string queryText,
        ScannedSearchClause scannedClause)
    {
        if (scannedClause.IsQuoted)
        {
            return ClauseBinding.Rejected(SearchDiagnosticCode.InvalidOperator, scannedClause.ValueSpan);
        }

        ReadOnlySpan<char> value = Slice(queryText, scannedClause.ValueSpan);
        SearchLocalStatus status;
        if (value.Equals("unseen", StringComparison.OrdinalIgnoreCase))
        {
            status = SearchLocalStatus.Unseen;
        }
        else if (value.Equals("watched", StringComparison.OrdinalIgnoreCase))
        {
            status = SearchLocalStatus.Watched;
        }
        else if (value.Equals("interested", StringComparison.OrdinalIgnoreCase))
        {
            status = SearchLocalStatus.Interested;
        }
        else if (value.Equals("not-now", StringComparison.OrdinalIgnoreCase))
        {
            status = SearchLocalStatus.NotNow;
        }
        else if (value.Equals("disliked", StringComparison.OrdinalIgnoreCase))
        {
            status = SearchLocalStatus.Disliked;
        }
        else if (TryGetLeadingOperatorDiagnostic(
            value,
            scannedClause.ValueSpan,
            out SearchDiagnostic? operatorDiagnostic))
        {
            return new ClauseBinding(null, operatorDiagnostic);
        }
        else
        {
            return ClauseBinding.Rejected(
                SearchDiagnosticCode.UnsupportedValue,
                scannedClause.ValueSpan);
        }

        return ClauseBinding.Success(new SearchStatusClause(
            scannedClause.Occurrence,
            status,
            scannedClause.Span));
    }

    private static SearchDiagnostic? ValidateNormalizedTokens(
        ImmutableArray<string> tokens,
        SearchTextSpan sourceSpan)
    {
        foreach (string token in tokens)
        {
            if (CountRunes(token) > SearchGrammarLimits.MaximumNormalizedTokenRunes)
            {
                return new SearchDiagnostic(SearchDiagnosticCode.TokenTooLong, sourceSpan);
            }
        }

        return null;
    }

    private static SearchDiagnostic? ValidateNumericDomain(
        SearchField field,
        int value,
        SearchTextSpan span)
    {
        (int minimum, int maximum) = field switch
        {
            SearchField.Year => (
                SearchGrammarLimits.EarliestReleaseYear,
                SearchGrammarLimits.LatestReleaseYear),
            SearchField.Runtime => (
                SearchGrammarLimits.MinimumRuntimeMinutes,
                SearchGrammarLimits.MaximumRuntimeMinutes),
            SearchField.Scare or
            SearchField.Gore or
            SearchField.Dread => (
                SearchGrammarLimits.MinimumIntensity,
                SearchGrammarLimits.MaximumIntensity),
            _ => throw new InvalidOperationException("Search field has no numeric domain."),
        };

        return value < minimum || value > maximum
            ? new SearchDiagnostic(SearchDiagnosticCode.ValueOutOfRange, span)
            : null;
    }

    private static int CountFuzzyEligibleTerms(ImmutableArray<string> tokens)
    {
        int count = 0;
        foreach (string token in tokens)
        {
            if (CountRunes(token) >= 4)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountRunes(string value)
    {
        int count = 0;
        foreach (Rune _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static bool TryParseInteger(ReadOnlySpan<char> value, out int result) =>
        int.TryParse(
            value,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);

    private static bool TryGetLeadingOperatorDiagnostic(
        ReadOnlySpan<char> value,
        SearchTextSpan sourceSpan,
        out SearchDiagnostic? diagnostic)
    {
        if (value.Length > 0 && IsOperatorCharacter(value[0]))
        {
            diagnostic = new SearchDiagnostic(
                SearchDiagnosticCode.InvalidOperator,
                new SearchTextSpan(sourceSpan.Start, 1));
            return true;
        }

        diagnostic = null;
        return false;
    }

    private static bool TryGetOperatorDiagnostic(
        ReadOnlySpan<char> value,
        SearchTextSpan sourceSpan,
        out SearchDiagnostic? diagnostic)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (IsOperatorCharacter(value[index]))
            {
                diagnostic = new SearchDiagnostic(
                    SearchDiagnosticCode.InvalidOperator,
                    new SearchTextSpan(sourceSpan.Start + index, 1));
                return true;
            }
        }

        diagnostic = null;
        return false;
    }

    private static bool IsOperatorCharacter(char value) => value is '<' or '>' or '=' or '!';

    private static bool TryResolveField(ReadOnlySpan<char> value, out SearchField field)
    {
        if (value.Equals("title", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Title;
        }
        else if (value.Equals("tag", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Tag;
        }
        else if (value.Equals("lens", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Lens;
        }
        else if (value.Equals("franchise", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Franchise;
        }
        else if (value.Equals("language", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Language;
        }
        else if (value.Equals("year", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Year;
        }
        else if (value.Equals("runtime", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Runtime;
        }
        else if (value.Equals("scare", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Scare;
        }
        else if (value.Equals("gore", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Gore;
        }
        else if (value.Equals("dread", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Dread;
        }
        else if (value.Equals("slasher", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Slasher;
        }
        else if (value.Equals("extreme", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Extreme;
        }
        else if (value.Equals("availability", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Availability;
        }
        else if (value.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            field = SearchField.Status;
        }
        else
        {
            field = default;
            return false;
        }

        return true;
    }

    private static ReadOnlySpan<char> Slice(string value, SearchTextSpan span) =>
        value.AsSpan(span.Start, span.Length);

    private static SearchQueryRejected Reject(SearchDiagnosticCode code, SearchTextSpan span) =>
        new(new SearchDiagnostic(code, span));

    private readonly record struct ClauseBinding(
        SearchQueryClause? Clause,
        SearchDiagnostic? Diagnostic)
    {
        public static ClauseBinding Success(SearchQueryClause clause) => new(clause, null);

        public static ClauseBinding Rejected(SearchDiagnosticCode code, SearchTextSpan span) =>
            new(null, new SearchDiagnostic(code, span));
    }
}
