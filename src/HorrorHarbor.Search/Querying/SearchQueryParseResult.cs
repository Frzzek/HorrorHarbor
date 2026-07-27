using System.Collections.Immutable;

namespace HorrorHarbor.Search.Querying;

public abstract record SearchQueryParseResult
{
    private protected SearchQueryParseResult()
    {
    }
}

public sealed record SearchQueryParsed : SearchQueryParseResult
{
    internal SearchQueryParsed(ParsedSearchQuery query)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public ParsedSearchQuery Query { get; }
}

public sealed record SearchQueryRejected : SearchQueryParseResult
{
    internal SearchQueryRejected(SearchDiagnostic diagnostic)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public SearchDiagnostic Diagnostic { get; }
}

public sealed class ParsedSearchQuery
{
    internal ParsedSearchQuery(
        SearchIntent intent,
        ImmutableArray<SearchQueryClause> clauses,
        int positiveLexicalClauseCount,
        int exclusionClauseCount,
        int hardFacetClauseCount,
        int quotedPhraseCount,
        int fuzzyEligibleTermCount)
    {
        if (!Enum.IsDefined(intent))
        {
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown search intent.");
        }

        if (clauses.IsDefault)
        {
            throw new ArgumentException("Clauses must be initialized.", nameof(clauses));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(positiveLexicalClauseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(exclusionClauseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(hardFacetClauseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(quotedPhraseCount);
        ArgumentOutOfRangeException.ThrowIfNegative(fuzzyEligibleTermCount);

        Intent = intent;
        Clauses = clauses;
        PositiveLexicalClauseCount = positiveLexicalClauseCount;
        ExclusionClauseCount = exclusionClauseCount;
        HardFacetClauseCount = hardFacetClauseCount;
        QuotedPhraseCount = quotedPhraseCount;
        FuzzyEligibleTermCount = fuzzyEligibleTermCount;
    }

    public SearchIntent Intent { get; }

    public ImmutableArray<SearchQueryClause> Clauses { get; }

    public int PositiveLexicalClauseCount { get; }

    public int ExclusionClauseCount { get; }

    public int HardFacetClauseCount { get; }

    public int QuotedPhraseCount { get; }

    public int FuzzyEligibleTermCount { get; }
}

public interface ISearchQueryParser
{
    SearchQueryParseResult Parse(
        string queryText,
        CancellationToken cancellationToken = default);
}
