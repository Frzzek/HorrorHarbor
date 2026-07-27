using System.Collections.Immutable;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Querying;

public enum SearchIntent
{
    LocalSearch,
    FilteredDiscovery,
}

public enum SearchField
{
    Any,
    Title,
    Tag,
    Lens,
    Franchise,
    Language,
    Year,
    Runtime,
    Scare,
    Gore,
    Dread,
    Slasher,
    Extreme,
    Availability,
    Status,
}

public enum SearchClauseOccurrence
{
    Positive,
    Excluded,
}

public enum SearchLexicalMode
{
    Token,
    Phrase,
}

public enum SearchNumericOperator
{
    Equal,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    InclusiveRange,
}

public enum SearchAvailabilityKind
{
    Unknown,
    Verified,
    ProviderDefined,
}

public enum SearchLocalStatus
{
    Unseen,
    Watched,
    Interested,
    NotNow,
    Disliked,
}

public abstract class SearchQueryClause
{
    private protected SearchQueryClause(
        SearchField field,
        SearchClauseOccurrence occurrence,
        SearchTextSpan span)
    {
        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown search field.");
        }

        if (!Enum.IsDefined(occurrence))
        {
            throw new ArgumentOutOfRangeException(nameof(occurrence), occurrence, "Unknown clause occurrence.");
        }

        Field = field;
        Occurrence = occurrence;
        Span = span;
    }

    public SearchField Field { get; }

    public SearchClauseOccurrence Occurrence { get; }

    public SearchTextSpan Span { get; }
}

public sealed class SearchLexicalClause : SearchQueryClause
{
    internal SearchLexicalClause(
        SearchField field,
        SearchClauseOccurrence occurrence,
        SearchLexicalMode mode,
        NormalizedSearchText normalizedValue,
        SearchTextSpan span)
        : base(field, occurrence, span)
    {
        if (field is not (SearchField.Any or
            SearchField.Title or
            SearchField.Tag or
            SearchField.Lens or
            SearchField.Franchise or
            SearchField.Language))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Field is not lexical.");
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown lexical mode.");
        }

        ArgumentNullException.ThrowIfNull(normalizedValue);
        if (normalizedValue.Value.Length == 0 || normalizedValue.Tokens.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Normalized lexical values must contain at least one token.", nameof(normalizedValue));
        }

        Mode = mode;
        NormalizedValue = normalizedValue.Value;
        Tokens = normalizedValue.Tokens;
        NormalizationChanges = normalizedValue.Changes;
    }

    public SearchLexicalMode Mode { get; }

    public string NormalizedValue { get; }

    public ImmutableArray<string> Tokens { get; }

    public SearchNormalizationChanges NormalizationChanges { get; }
}

public sealed class SearchNumericClause : SearchQueryClause
{
    internal SearchNumericClause(
        SearchField field,
        SearchClauseOccurrence occurrence,
        SearchNumericOperator numericOperator,
        int firstValue,
        int? secondValue,
        SearchTextSpan span)
        : base(field, occurrence, span)
    {
        if (field is not (SearchField.Year or
            SearchField.Runtime or
            SearchField.Scare or
            SearchField.Gore or
            SearchField.Dread))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Field is not numeric.");
        }

        if (!Enum.IsDefined(numericOperator))
        {
            throw new ArgumentOutOfRangeException(nameof(numericOperator), numericOperator, "Unknown numeric operator.");
        }

        bool isRange = numericOperator == SearchNumericOperator.InclusiveRange;
        if (isRange != secondValue.HasValue)
        {
            throw new ArgumentException(
                "Inclusive ranges require a second value, and non-range operators prohibit one.",
                nameof(secondValue));
        }

        if (secondValue < firstValue)
        {
            throw new ArgumentOutOfRangeException(nameof(secondValue), secondValue, "Range endpoints must be ascending.");
        }

        Operator = numericOperator;
        FirstValue = firstValue;
        SecondValue = secondValue;
    }

    public SearchNumericOperator Operator { get; }

    public int FirstValue { get; }

    public int? SecondValue { get; }
}

public sealed class SearchBooleanClause : SearchQueryClause
{
    internal SearchBooleanClause(
        SearchField field,
        SearchClauseOccurrence occurrence,
        bool value,
        SearchTextSpan span)
        : base(field, occurrence, span)
    {
        if (field is not (SearchField.Slasher or SearchField.Extreme))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Field is not Boolean.");
        }

        Value = value;
    }

    public bool Value { get; }
}

public sealed class SearchAvailabilityClause : SearchQueryClause
{
    internal SearchAvailabilityClause(
        SearchClauseOccurrence occurrence,
        SearchAvailabilityKind kind,
        string? providerValue,
        SearchTextSpan span)
        : base(SearchField.Availability, occurrence, span)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown availability kind.");
        }

        bool isProviderDefined = kind == SearchAvailabilityKind.ProviderDefined;
        if (isProviderDefined != !string.IsNullOrWhiteSpace(providerValue))
        {
            throw new ArgumentException(
                "Provider-defined availability requires a value, and built-in availability kinds prohibit one.",
                nameof(providerValue));
        }

        Kind = kind;
        ProviderValue = providerValue;
    }

    public SearchAvailabilityKind Kind { get; }

    public string? ProviderValue { get; }
}

public sealed class SearchStatusClause : SearchQueryClause
{
    internal SearchStatusClause(
        SearchClauseOccurrence occurrence,
        SearchLocalStatus value,
        SearchTextSpan span)
        : base(SearchField.Status, occurrence, span)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown local status.");
        }

        Value = value;
    }

    public SearchLocalStatus Value { get; }
}
