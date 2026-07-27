namespace HorrorHarbor.Search.Querying;

public enum SearchDiagnosticCategory
{
    InvalidSyntax,
    UnsupportedFieldOrValue,
    LimitExceeded,
}

public enum SearchDiagnosticCode
{
    InvalidUtf16,
    ControlCharacter,
    UnsafeFormatCharacter,
    QueryTooLong,
    ClauseLimitExceeded,
    PhraseLimitExceeded,
    PhraseTokenLimitExceeded,
    TokenTooLong,
    FuzzyTermLimitExceeded,
    UnknownField,
    MissingValue,
    UnterminatedQuote,
    InvalidOperator,
    InvalidNumber,
    InvalidBoolean,
    InvalidRange,
    ValueOutOfRange,
    UnsupportedValue,
}

public sealed record SearchDiagnostic
{
    public SearchDiagnostic(SearchDiagnosticCode code, SearchTextSpan span)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown search diagnostic code.");
        }

        Code = code;
        Span = span;
    }

    public SearchDiagnosticCode Code { get; }

    public SearchTextSpan Span { get; }

    public SearchDiagnosticCategory Category => Code switch
    {
        SearchDiagnosticCode.InvalidUtf16 or
        SearchDiagnosticCode.ControlCharacter or
        SearchDiagnosticCode.UnsafeFormatCharacter or
        SearchDiagnosticCode.MissingValue or
        SearchDiagnosticCode.UnterminatedQuote or
        SearchDiagnosticCode.InvalidOperator or
        SearchDiagnosticCode.InvalidNumber or
        SearchDiagnosticCode.InvalidRange => SearchDiagnosticCategory.InvalidSyntax,

        SearchDiagnosticCode.UnknownField or
        SearchDiagnosticCode.InvalidBoolean or
        SearchDiagnosticCode.ValueOutOfRange or
        SearchDiagnosticCode.UnsupportedValue => SearchDiagnosticCategory.UnsupportedFieldOrValue,

        SearchDiagnosticCode.QueryTooLong or
        SearchDiagnosticCode.ClauseLimitExceeded or
        SearchDiagnosticCode.PhraseLimitExceeded or
        SearchDiagnosticCode.PhraseTokenLimitExceeded or
        SearchDiagnosticCode.TokenTooLong or
        SearchDiagnosticCode.FuzzyTermLimitExceeded => SearchDiagnosticCategory.LimitExceeded,

        _ => throw new InvalidOperationException("Search diagnostic code has no category mapping."),
    };

    public string StableCode => Code switch
    {
        SearchDiagnosticCode.InvalidUtf16 => "invalid-utf16",
        SearchDiagnosticCode.ControlCharacter => "control-character",
        SearchDiagnosticCode.UnsafeFormatCharacter => "unsafe-format-character",
        SearchDiagnosticCode.QueryTooLong => "query-too-long",
        SearchDiagnosticCode.ClauseLimitExceeded => "clause-limit-exceeded",
        SearchDiagnosticCode.PhraseLimitExceeded => "phrase-limit-exceeded",
        SearchDiagnosticCode.PhraseTokenLimitExceeded => "phrase-token-limit-exceeded",
        SearchDiagnosticCode.TokenTooLong => "token-too-long",
        SearchDiagnosticCode.FuzzyTermLimitExceeded => "fuzzy-term-limit-exceeded",
        SearchDiagnosticCode.UnknownField => "unknown-field",
        SearchDiagnosticCode.MissingValue => "missing-value",
        SearchDiagnosticCode.UnterminatedQuote => "unterminated-quote",
        SearchDiagnosticCode.InvalidOperator => "invalid-operator",
        SearchDiagnosticCode.InvalidNumber => "invalid-number",
        SearchDiagnosticCode.InvalidBoolean => "invalid-boolean",
        SearchDiagnosticCode.InvalidRange => "invalid-range",
        SearchDiagnosticCode.ValueOutOfRange => "value-out-of-range",
        SearchDiagnosticCode.UnsupportedValue => "unsupported-value",
        _ => throw new InvalidOperationException("Search diagnostic code has no stable-code mapping."),
    };
}
