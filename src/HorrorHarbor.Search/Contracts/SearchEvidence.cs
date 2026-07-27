namespace HorrorHarbor.Search.Contracts;

public enum SearchTitleVariantOrigin
{
    CanonicalTitle,
    CanonicalPunctuationDeletion,
    RomanNumeralAlias,
    OriginalTitle,
    ExplicitAlias,
}

public enum SearchEvidenceField
{
    Summary,
    CanonicalTitle,
    OriginalTitle,
    TitleAlias,
    Tag,
    PrimaryLens,
    SecondaryLens,
    Franchise,
    OriginalLanguage,
    ReleaseYear,
    Runtime,
    Scare,
    Gore,
    Dread,
    Slasher,
    Extreme,
    Availability,
    Status,
    Exclusion,
}

public enum SearchEvidenceCode
{
    CanonicalTitleExact,
    CanonicalTitleNormalizedExact,
    CanonicalTitlePunctuationAlias,
    RomanNumeralTitleAlias,
    OriginalTitleExact,
    ExplicitTitleAlias,
    CanonicalTitlePhrase,
    CanonicalTitleAllQueryTokens,
    CanonicalTitleToken,
    CanonicalTitlePrefix,
    LexicalPhraseMatch,
    LexicalTokenMatch,
    LexicalTokenPrefix,
    FuzzyTitleEditDistance1,
    FuzzyTitleEditDistance2,
    TagExact,
    TagExactNormalized,
    TagTokenMatch,
    PrimaryLensMatch,
    SecondaryLensMatch,
    FranchiseMatch,
    OriginalLanguageMatch,
    FuzzyNonTitleEditDistance1,
    FuzzyNonTitleEditDistance2,
    AllPositiveClausesMatched,
    YearFilterSatisfied,
    RuntimeFilterSatisfied,
    ScareFilterSatisfied,
    GoreFilterSatisfied,
    DreadFilterSatisfied,
    SlasherFilterSatisfied,
    ExtremeFilterSatisfied,
    AvailabilityUnknown,
    AvailabilityVerified,
    AvailabilityProviderMatch,
    StatusUnseen,
    StatusWatched,
    StatusInterested,
    StatusNotNow,
    StatusDisliked,
    ExclusionSatisfied,
}

public static class SearchEvidenceCodeExtensions
{
    public static string ToStableCode(this SearchEvidenceCode code) => code switch
    {
        SearchEvidenceCode.CanonicalTitleExact => "canonical-title-exact",
        SearchEvidenceCode.CanonicalTitleNormalizedExact => "canonical-title-normalized-exact",
        SearchEvidenceCode.CanonicalTitlePunctuationAlias => "canonical-title-punctuation-alias",
        SearchEvidenceCode.RomanNumeralTitleAlias => "roman-numeral-title-alias",
        SearchEvidenceCode.OriginalTitleExact => "original-title-exact",
        SearchEvidenceCode.ExplicitTitleAlias => "explicit-title-alias",
        SearchEvidenceCode.CanonicalTitlePhrase => "canonical-title-phrase",
        SearchEvidenceCode.CanonicalTitleAllQueryTokens => "canonical-title-all-query-tokens",
        SearchEvidenceCode.CanonicalTitleToken => "canonical-title-token",
        SearchEvidenceCode.CanonicalTitlePrefix => "canonical-title-prefix",
        SearchEvidenceCode.LexicalPhraseMatch => "lexical-phrase-match",
        SearchEvidenceCode.LexicalTokenMatch => "lexical-token-match",
        SearchEvidenceCode.LexicalTokenPrefix => "lexical-token-prefix",
        SearchEvidenceCode.FuzzyTitleEditDistance1 => "fuzzy-title-edit-distance-1",
        SearchEvidenceCode.FuzzyTitleEditDistance2 => "fuzzy-title-edit-distance-2",
        SearchEvidenceCode.TagExact => "tag-exact",
        SearchEvidenceCode.TagExactNormalized => "tag-exact-normalized",
        SearchEvidenceCode.TagTokenMatch => "tag-token-match",
        SearchEvidenceCode.PrimaryLensMatch => "primary-lens-match",
        SearchEvidenceCode.SecondaryLensMatch => "secondary-lens-match",
        SearchEvidenceCode.FranchiseMatch => "franchise-match",
        SearchEvidenceCode.OriginalLanguageMatch => "original-language-match",
        SearchEvidenceCode.FuzzyNonTitleEditDistance1 => "fuzzy-non-title-edit-distance-1",
        SearchEvidenceCode.FuzzyNonTitleEditDistance2 => "fuzzy-non-title-edit-distance-2",
        SearchEvidenceCode.AllPositiveClausesMatched => "all-positive-clauses-matched",
        SearchEvidenceCode.YearFilterSatisfied => "year-filter-satisfied",
        SearchEvidenceCode.RuntimeFilterSatisfied => "runtime-filter-satisfied",
        SearchEvidenceCode.ScareFilterSatisfied => "scare-filter-satisfied",
        SearchEvidenceCode.GoreFilterSatisfied => "gore-filter-satisfied",
        SearchEvidenceCode.DreadFilterSatisfied => "dread-filter-satisfied",
        SearchEvidenceCode.SlasherFilterSatisfied => "slasher-filter-satisfied",
        SearchEvidenceCode.ExtremeFilterSatisfied => "extreme-filter-satisfied",
        SearchEvidenceCode.AvailabilityUnknown => "availability-unknown",
        SearchEvidenceCode.AvailabilityVerified => "availability-verified",
        SearchEvidenceCode.AvailabilityProviderMatch => "availability-provider-match",
        SearchEvidenceCode.StatusUnseen => "status-unseen",
        SearchEvidenceCode.StatusWatched => "status-watched",
        SearchEvidenceCode.StatusInterested => "status-interested",
        SearchEvidenceCode.StatusNotNow => "status-not-now",
        SearchEvidenceCode.StatusDisliked => "status-disliked",
        SearchEvidenceCode.ExclusionSatisfied => "exclusion-satisfied",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown search evidence code."),
    };
}

public sealed record SearchEvidence
{
    internal SearchEvidence(
        SearchEvidenceCode code,
        SearchEvidenceField field,
        string? normalizedQueryValue = null,
        string? normalizedIndexedValue = null,
        int? editDistance = null,
        SearchTitleVariantOrigin? titleOrigin = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown search evidence code.");
        }

        if (!Enum.IsDefined(field))
        {
            throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown search evidence field.");
        }

        if (titleOrigin.HasValue && !Enum.IsDefined(titleOrigin.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(titleOrigin),
                titleOrigin,
                "Unknown title variant origin.");
        }

        ValidateCodeField(code, field);
        ValidateEditDistance(code, editDistance);
        ValidateTitleOrigin(field, titleOrigin);

        Code = code;
        Field = field;
        NormalizedQueryValue = ValidateNormalizedValue(
            normalizedQueryValue,
            nameof(normalizedQueryValue));
        NormalizedIndexedValue = ValidateNormalizedValue(
            normalizedIndexedValue,
            nameof(normalizedIndexedValue));
        EditDistance = editDistance;
        TitleOrigin = titleOrigin;
    }

    public SearchEvidenceCode Code { get; }

    public string StableCode => Code.ToStableCode();

    public SearchEvidenceField Field { get; }

    public string? NormalizedQueryValue { get; }

    public string? NormalizedIndexedValue { get; }

    public int? EditDistance { get; }

    public SearchTitleVariantOrigin? TitleOrigin { get; }

    private static void ValidateCodeField(
        SearchEvidenceCode code,
        SearchEvidenceField field)
    {
        bool isValid = code switch
        {
            SearchEvidenceCode.CanonicalTitleExact or
            SearchEvidenceCode.CanonicalTitleNormalizedExact or
            SearchEvidenceCode.CanonicalTitlePunctuationAlias or
            SearchEvidenceCode.CanonicalTitlePhrase or
            SearchEvidenceCode.CanonicalTitleAllQueryTokens or
            SearchEvidenceCode.CanonicalTitleToken or
            SearchEvidenceCode.CanonicalTitlePrefix =>
                field == SearchEvidenceField.CanonicalTitle,
            SearchEvidenceCode.RomanNumeralTitleAlias or
            SearchEvidenceCode.ExplicitTitleAlias =>
                field == SearchEvidenceField.TitleAlias,
            SearchEvidenceCode.OriginalTitleExact =>
                field == SearchEvidenceField.OriginalTitle,
            SearchEvidenceCode.LexicalPhraseMatch or
            SearchEvidenceCode.LexicalTokenMatch or
            SearchEvidenceCode.LexicalTokenPrefix => IsLexicalField(field),
            SearchEvidenceCode.FuzzyTitleEditDistance1 or
            SearchEvidenceCode.FuzzyTitleEditDistance2 => IsTitleField(field),
            SearchEvidenceCode.TagExact or
            SearchEvidenceCode.TagExactNormalized or
            SearchEvidenceCode.TagTokenMatch => field == SearchEvidenceField.Tag,
            SearchEvidenceCode.PrimaryLensMatch =>
                field == SearchEvidenceField.PrimaryLens,
            SearchEvidenceCode.SecondaryLensMatch =>
                field == SearchEvidenceField.SecondaryLens,
            SearchEvidenceCode.FranchiseMatch =>
                field == SearchEvidenceField.Franchise,
            SearchEvidenceCode.OriginalLanguageMatch =>
                field == SearchEvidenceField.OriginalLanguage,
            SearchEvidenceCode.FuzzyNonTitleEditDistance1 or
            SearchEvidenceCode.FuzzyNonTitleEditDistance2 =>
                IsNonTitleLexicalField(field),
            SearchEvidenceCode.AllPositiveClausesMatched =>
                field == SearchEvidenceField.Summary,
            SearchEvidenceCode.YearFilterSatisfied =>
                field == SearchEvidenceField.ReleaseYear,
            SearchEvidenceCode.RuntimeFilterSatisfied =>
                field == SearchEvidenceField.Runtime,
            SearchEvidenceCode.ScareFilterSatisfied =>
                field == SearchEvidenceField.Scare,
            SearchEvidenceCode.GoreFilterSatisfied =>
                field == SearchEvidenceField.Gore,
            SearchEvidenceCode.DreadFilterSatisfied =>
                field == SearchEvidenceField.Dread,
            SearchEvidenceCode.SlasherFilterSatisfied =>
                field == SearchEvidenceField.Slasher,
            SearchEvidenceCode.ExtremeFilterSatisfied =>
                field == SearchEvidenceField.Extreme,
            SearchEvidenceCode.AvailabilityUnknown or
            SearchEvidenceCode.AvailabilityVerified or
            SearchEvidenceCode.AvailabilityProviderMatch =>
                field == SearchEvidenceField.Availability,
            SearchEvidenceCode.StatusUnseen or
            SearchEvidenceCode.StatusWatched or
            SearchEvidenceCode.StatusInterested or
            SearchEvidenceCode.StatusNotNow or
            SearchEvidenceCode.StatusDisliked => field == SearchEvidenceField.Status,
            SearchEvidenceCode.ExclusionSatisfied =>
                field == SearchEvidenceField.Exclusion,
            _ => false,
        };

        if (!isValid)
        {
            throw new ArgumentException(
                "Evidence code does not support the supplied evidence field.",
                nameof(field));
        }
    }

    private static bool IsLexicalField(SearchEvidenceField field) =>
        IsTitleField(field) || IsNonTitleLexicalField(field);

    private static bool IsTitleField(SearchEvidenceField field) => field is
        SearchEvidenceField.CanonicalTitle or
        SearchEvidenceField.OriginalTitle or
        SearchEvidenceField.TitleAlias;

    private static bool IsNonTitleLexicalField(SearchEvidenceField field) => field is
        SearchEvidenceField.Tag or
        SearchEvidenceField.PrimaryLens or
        SearchEvidenceField.SecondaryLens or
        SearchEvidenceField.Franchise or
        SearchEvidenceField.OriginalLanguage;

    private static string? ValidateNormalizedValue(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length == 0 || value.Length > SearchDocumentLimits.MaximumSearchableTextCodeUnits)
        {
            throw new ArgumentException(
                $"Normalized evidence values must contain 1 through {SearchDocumentLimits.MaximumSearchableTextCodeUnits} UTF-16 code units.",
                parameterName);
        }

        return value;
    }

    private static void ValidateEditDistance(
        SearchEvidenceCode code,
        int? editDistance)
    {
        int? requiredDistance = code switch
        {
            SearchEvidenceCode.FuzzyTitleEditDistance1 or
            SearchEvidenceCode.FuzzyNonTitleEditDistance1 => 1,
            SearchEvidenceCode.FuzzyTitleEditDistance2 or
            SearchEvidenceCode.FuzzyNonTitleEditDistance2 => 2,
            _ => null,
        };

        if (requiredDistance != editDistance)
        {
            throw new ArgumentException(
                requiredDistance.HasValue
                    ? $"Evidence code requires edit distance {requiredDistance.Value}."
                    : "Non-fuzzy evidence cannot contain an edit distance.",
                nameof(editDistance));
        }
    }

    private static void ValidateTitleOrigin(
        SearchEvidenceField field,
        SearchTitleVariantOrigin? titleOrigin)
    {
        if (!titleOrigin.HasValue)
        {
            return;
        }

        SearchEvidenceField requiredField = titleOrigin.Value switch
        {
            SearchTitleVariantOrigin.CanonicalTitle or
            SearchTitleVariantOrigin.CanonicalPunctuationDeletion =>
                SearchEvidenceField.CanonicalTitle,
            SearchTitleVariantOrigin.OriginalTitle => SearchEvidenceField.OriginalTitle,
            SearchTitleVariantOrigin.RomanNumeralAlias or
            SearchTitleVariantOrigin.ExplicitAlias => SearchEvidenceField.TitleAlias,
            _ => throw new InvalidOperationException(
                "Title variant origin has no evidence-field mapping."),
        };

        if (field != requiredField)
        {
            throw new ArgumentException(
                "Title variant origin does not match the evidence field.",
                nameof(titleOrigin));
        }
    }
}
