using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using HorrorHarbor.Search.Contracts;

namespace HorrorHarbor.Search.Normalization;

internal sealed class TitleVariant
{
    public TitleVariant(
        NormalizedSearchText normalized,
        SearchTitleVariantOrigin origin,
        SearchMetadataProvenance? provenance = null)
    {
        Value = normalized.Value;
        Tokens = normalized.Tokens;
        NormalizationChanges = normalized.Changes;
        Origin = origin;
        Provenance = provenance;
    }

    public string Value { get; }

    public ImmutableArray<string> Tokens { get; }

    public SearchNormalizationChanges NormalizationChanges { get; }

    public SearchTitleVariantOrigin Origin { get; }

    public SearchMetadataProvenance? Provenance { get; }
}

internal static class TitleVariantBuilder
{
    public static ImmutableArray<TitleVariant> Build(HorrorSearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var result = ImmutableArray.CreateBuilder<TitleVariant>();
        var observedValues = new HashSet<string>(StringComparer.Ordinal);

        NormalizedSearchText canonical =
            SearchTextNormalizerV1.Normalize(document.Candidate.Title);
        AddVariant(
            result,
            observedValues,
            canonical,
            SearchTitleVariantOrigin.CanonicalTitle);

        NormalizedSearchText? punctuationDeletion =
            BuildPunctuationDeletionVariant(
                document.Candidate.Title,
                canonical.Value);
        if (punctuationDeletion is not null)
        {
            AddVariant(
                result,
                observedValues,
                punctuationDeletion,
                SearchTitleVariantOrigin.CanonicalPunctuationDeletion);
        }

        string? romanNumeralAlias =
            RomanNumeralAliasBuilder.Build(canonical.Tokens);
        if (romanNumeralAlias is not null)
        {
            AddVariant(
                result,
                observedValues,
                SearchTextNormalizerV1.Normalize(romanNumeralAlias),
                SearchTitleVariantOrigin.RomanNumeralAlias);
        }

        if (document.Candidate.OriginalTitle is string originalTitle)
        {
            AddVariant(
                result,
                observedValues,
                SearchTextNormalizerV1.Normalize(originalTitle),
                SearchTitleVariantOrigin.OriginalTitle);
        }

        foreach (SearchAlias alias in document.Aliases)
        {
            AddVariant(
                result,
                observedValues,
                SearchTextNormalizerV1.Normalize(alias.Value),
                SearchTitleVariantOrigin.ExplicitAlias,
                alias.Provenance);
        }

        return result.ToImmutable();
    }

    private static void AddVariant(
        ImmutableArray<TitleVariant>.Builder result,
        HashSet<string> observedValues,
        NormalizedSearchText normalized,
        SearchTitleVariantOrigin origin,
        SearchMetadataProvenance? provenance = null)
    {
        if (normalized.Value.Length != 0 && observedValues.Add(normalized.Value))
        {
            result.Add(new TitleVariant(normalized, origin, provenance));
        }
    }

    private static NormalizedSearchText? BuildPunctuationDeletionVariant(
        string displayTitle,
        string canonicalValue)
    {
        var builder = new StringBuilder(displayTitle.Length);
        bool removed = false;
        foreach (Rune rune in displayTitle.EnumerateRunes())
        {
            if (IsPunctuationOrSymbol(Rune.GetUnicodeCategory(rune)))
            {
                removed = true;
                continue;
            }

            AppendRune(builder, rune);
        }

        if (!removed)
        {
            return null;
        }

        NormalizedSearchText normalized =
            SearchTextNormalizerV1.Normalize(builder.ToString());
        return normalized.Value.Length == 0 ||
            string.Equals(
                normalized.Value,
                canonicalValue,
                StringComparison.Ordinal)
            ? null
            : normalized;
    }

    private static bool IsPunctuationOrSymbol(UnicodeCategory category) => category is
        UnicodeCategory.ConnectorPunctuation or
        UnicodeCategory.DashPunctuation or
        UnicodeCategory.OpenPunctuation or
        UnicodeCategory.ClosePunctuation or
        UnicodeCategory.InitialQuotePunctuation or
        UnicodeCategory.FinalQuotePunctuation or
        UnicodeCategory.OtherPunctuation or
        UnicodeCategory.MathSymbol or
        UnicodeCategory.CurrencySymbol or
        UnicodeCategory.ModifierSymbol or
        UnicodeCategory.OtherSymbol;

    private static void AppendRune(StringBuilder builder, Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        int written = rune.EncodeToUtf16(buffer);
        builder.Append(buffer[..written]);
    }
}
