using System.Collections.Immutable;
using System.Text;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.Search.Ranking;

internal enum LexicalMatchForm
{
    Phrase,
    ExactToken,
    PrefixToken,
    FuzzyTokenDistance1,
    FuzzyTokenDistance2,
}

internal readonly record struct LexicalMatchFact(
    SearchEvidenceField Field,
    string QueryValue,
    string IndexedValue,
    LexicalMatchForm Form,
    SearchNormalizationChanges IndexedNormalizationChanges,
    SearchTitleVariantOrigin? TitleOrigin);

internal sealed class LexicalClauseMatch
{
    public LexicalClauseMatch(
        ImmutableArray<LexicalMatchFact> facts,
        bool isCompleteValue)
    {
        if (facts.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A lexical clause match requires at least one fact.",
                nameof(facts));
        }

        Facts = facts;
        IsCompleteValue = isCompleteValue;
    }

    public ImmutableArray<LexicalMatchFact> Facts { get; }

    public bool IsCompleteValue { get; }

    public bool HasFuzzyMatch => Facts.Any(static fact => IsFuzzy(fact.Form));

    public bool HasNonTitleFuzzyMatch => Facts.Any(static fact =>
        IsFuzzy(fact.Form) && !IsTitleField(fact.Field));

    public bool IsCanonicalTitleOnly => Facts.All(static fact =>
        fact.Field == SearchEvidenceField.CanonicalTitle);

    public bool IsExactCanonicalTitleOnly => Facts.All(static fact =>
        fact.Field == SearchEvidenceField.CanonicalTitle &&
        fact.Form is LexicalMatchForm.Phrase or LexicalMatchForm.ExactToken);

    public bool IsExactCompleteNonTitleValue =>
        IsCompleteValue &&
        Facts.All(static fact =>
            (fact.Form is LexicalMatchForm.Phrase or LexicalMatchForm.ExactToken) &&
            (fact.Field is SearchEvidenceField.Tag or
                SearchEvidenceField.PrimaryLens or
                SearchEvidenceField.SecondaryLens or
                SearchEvidenceField.Franchise));

    private static bool IsFuzzy(LexicalMatchForm form) => form is
        LexicalMatchForm.FuzzyTokenDistance1 or
        LexicalMatchForm.FuzzyTokenDistance2;

    private static bool IsTitleField(SearchEvidenceField field) => field is
        SearchEvidenceField.CanonicalTitle or
        SearchEvidenceField.OriginalTitle or
        SearchEvidenceField.TitleAlias;
}

internal static class DocumentLexicalMatcher
{
    public static LexicalClauseMatch? Match(
        SearchIndexDocumentData document,
        SearchLexicalClause clause,
        bool allowFuzzy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clause);

        return clause.Field switch
        {
            SearchField.Any => MatchAny(
                document,
                clause,
                allowFuzzy,
                cancellationToken),
            SearchField.Title => MatchTitles(
                document.TitleVariants,
                clause,
                allowFuzzy,
                cancellationToken),
            SearchField.Tag => MatchTags(
                document.Tags,
                clause,
                allowFuzzy,
                cancellationToken),
            SearchField.Lens => Best(
                MatchOptional(
                    document.PrimaryLens,
                    SearchEvidenceField.PrimaryLens,
                    clause,
                    allowFuzzy,
                    cancellationToken),
                MatchOptional(
                    document.SecondaryLens,
                    SearchEvidenceField.SecondaryLens,
                    clause,
                    allowFuzzy,
                    cancellationToken)),
            SearchField.Franchise => MatchOptional(
                document.Franchise,
                SearchEvidenceField.Franchise,
                clause,
                allowFuzzy,
                cancellationToken),
            SearchField.Language => MatchOptional(
                document.OriginalLanguage,
                SearchEvidenceField.OriginalLanguage,
                clause,
                allowFuzzy,
                cancellationToken),
            _ => throw new InvalidOperationException(
                "Document lexical matching received a non-lexical field."),
        };
    }

    public static string? BuildPositiveTitleQuery(
        ParsedSearchQuery query,
        out SearchNormalizationChanges normalizationChanges)
    {
        ArgumentNullException.ThrowIfNull(query);

        normalizationChanges = SearchNormalizationChanges.None;
        var builder = new StringBuilder();
        foreach (SearchQueryClause queryClause in query.Clauses)
        {
            if (queryClause is not SearchLexicalClause lexical ||
                lexical.Occurrence != SearchClauseOccurrence.Positive)
            {
                continue;
            }

            if (lexical.Field is not (SearchField.Any or SearchField.Title))
            {
                return null;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(lexical.NormalizedValue);
            normalizationChanges |= lexical.NormalizationChanges;
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static LexicalClauseMatch? MatchAny(
        SearchIndexDocumentData document,
        SearchLexicalClause clause,
        bool allowFuzzy,
        CancellationToken cancellationToken)
    {
        if (clause.Mode == SearchLexicalMode.Phrase)
        {
            LexicalClauseMatch? best = MatchTitles(
                document.TitleVariants,
                clause,
                allowFuzzy: false,
                cancellationToken);
            best = Best(best, MatchTags(
                document.Tags,
                clause,
                allowFuzzy: false,
                cancellationToken,
                requireCompletePhrase: false));
            best = Best(best, MatchOptional(
                document.PrimaryLens,
                SearchEvidenceField.PrimaryLens,
                clause,
                allowFuzzy: false,
                cancellationToken));
            best = Best(best, MatchOptional(
                document.SecondaryLens,
                SearchEvidenceField.SecondaryLens,
                clause,
                allowFuzzy: false,
                cancellationToken));
            return Best(best, MatchOptional(
                document.Franchise,
                SearchEvidenceField.Franchise,
                clause,
                allowFuzzy: false,
                cancellationToken));
        }

        var facts = ImmutableArray.CreateBuilder<LexicalMatchFact>(
            clause.Tokens.Length);
        foreach (string queryToken in clause.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LexicalMatchFact? best = FindBestAnyToken(
                document,
                queryToken,
                allowFuzzy,
                cancellationToken);
            if (!best.HasValue)
            {
                return null;
            }

            facts.Add(best.Value);
        }

        return new LexicalClauseMatch(facts.MoveToImmutable(), isCompleteValue: false);
    }

    private static LexicalMatchFact? FindBestAnyToken(
        SearchIndexDocumentData document,
        string queryToken,
        bool allowFuzzy,
        CancellationToken cancellationToken)
    {
        LexicalMatchFact? best = null;
        foreach (TitleVariant variant in document.TitleVariants)
        {
            best = Best(best, FindBestToken(
                variant.Tokens,
                GetEvidenceField(variant.Origin),
                queryToken,
                variant.NormalizationChanges,
                variant.Origin,
                allowFuzzy,
                cancellationToken));
        }

        foreach (NormalizedSearchText tag in document.Tags)
        {
            best = Best(best, FindBestToken(
                tag.Tokens,
                SearchEvidenceField.Tag,
                queryToken,
                tag.Changes,
                titleOrigin: null,
                allowFuzzy,
                cancellationToken));
        }

        best = Best(best, FindBestToken(
            document.PrimaryLens,
            SearchEvidenceField.PrimaryLens,
            queryToken,
            allowFuzzy,
            cancellationToken));
        best = Best(best, FindBestToken(
            document.SecondaryLens,
            SearchEvidenceField.SecondaryLens,
            queryToken,
            allowFuzzy,
            cancellationToken));
        return Best(best, FindBestToken(
            document.Franchise,
            SearchEvidenceField.Franchise,
            queryToken,
            allowFuzzy,
            cancellationToken));
    }

    private static LexicalClauseMatch? MatchTitles(
        ImmutableArray<TitleVariant> variants,
        SearchLexicalClause clause,
        bool allowFuzzy,
        CancellationToken cancellationToken)
    {
        LexicalClauseMatch? best = null;
        foreach (TitleVariant variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            best = Best(best, MatchValue(
                variant.Value,
                variant.Tokens,
                variant.NormalizationChanges,
                GetEvidenceField(variant.Origin),
                variant.Origin,
                clause,
                allowFuzzy,
                requireCompletePhrase: false,
                cancellationToken));
        }

        return best;
    }

    private static LexicalClauseMatch? MatchTags(
        ImmutableArray<NormalizedSearchText> tags,
        SearchLexicalClause clause,
        bool allowFuzzy,
        CancellationToken cancellationToken,
        bool requireCompletePhrase = true)
    {
        LexicalClauseMatch? best = null;
        foreach (NormalizedSearchText tag in tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            best = Best(best, MatchValue(
                tag.Value,
                tag.Tokens,
                tag.Changes,
                SearchEvidenceField.Tag,
                titleOrigin: null,
                clause,
                allowFuzzy,
                requireCompletePhrase,
                cancellationToken));
        }

        return best;
    }

    private static LexicalClauseMatch? MatchOptional(
        NormalizedSearchText? value,
        SearchEvidenceField field,
        SearchLexicalClause clause,
        bool allowFuzzy,
        CancellationToken cancellationToken) => value is null
            ? null
            : MatchValue(
                value.Value,
                value.Tokens,
                value.Changes,
                field,
                titleOrigin: null,
                clause,
                allowFuzzy,
                requireCompletePhrase: false,
                cancellationToken);

    private static LexicalClauseMatch? MatchValue(
        string indexedValue,
        ImmutableArray<string> indexedTokens,
        SearchNormalizationChanges indexedChanges,
        SearchEvidenceField field,
        SearchTitleVariantOrigin? titleOrigin,
        SearchLexicalClause clause,
        bool allowFuzzy,
        bool requireCompletePhrase,
        CancellationToken cancellationToken)
    {
        bool isCompleteValue = string.Equals(
            indexedValue,
            clause.NormalizedValue,
            StringComparison.Ordinal);
        if (clause.Mode == SearchLexicalMode.Phrase)
        {
            if ((requireCompletePhrase && !isCompleteValue) ||
                !ContainsPhrase(indexedTokens, clause.Tokens))
            {
                return null;
            }

            return new LexicalClauseMatch(
                [new LexicalMatchFact(
                    field,
                    clause.NormalizedValue,
                    indexedValue,
                    LexicalMatchForm.Phrase,
                    indexedChanges,
                    titleOrigin)],
                isCompleteValue);
        }

        var facts = ImmutableArray.CreateBuilder<LexicalMatchFact>(
            clause.Tokens.Length);
        foreach (string queryToken in clause.Tokens)
        {
            LexicalMatchFact? match = FindBestToken(
                indexedTokens,
                field,
                queryToken,
                indexedChanges,
                titleOrigin,
                allowFuzzy,
                cancellationToken);
            if (!match.HasValue)
            {
                return null;
            }

            facts.Add(match.Value);
        }

        bool allExact = facts.All(static fact =>
            fact.Form == LexicalMatchForm.ExactToken);
        return new LexicalClauseMatch(
            facts.MoveToImmutable(),
            isCompleteValue && allExact);
    }

    private static LexicalMatchFact? FindBestToken(
        NormalizedSearchText? value,
        SearchEvidenceField field,
        string queryToken,
        bool allowFuzzy,
        CancellationToken cancellationToken) => value is null
            ? null
            : FindBestToken(
                value.Tokens,
                field,
                queryToken,
                value.Changes,
                titleOrigin: null,
                allowFuzzy,
                cancellationToken);

    private static LexicalMatchFact? FindBestToken(
        ImmutableArray<string> indexedTokens,
        SearchEvidenceField field,
        string queryToken,
        SearchNormalizationChanges indexedChanges,
        SearchTitleVariantOrigin? titleOrigin,
        bool allowFuzzy,
        CancellationToken cancellationToken)
    {
        LexicalMatchFact? best = null;
        foreach (string indexedToken in indexedTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LexicalMatchForm? form = ClassifyToken(
                queryToken,
                indexedToken,
                allowFuzzy);
            if (form.HasValue)
            {
                best = Best(best, new LexicalMatchFact(
                    field,
                    queryToken,
                    indexedToken,
                    form.Value,
                    indexedChanges,
                    titleOrigin));
            }
        }

        return best;
    }

    private static LexicalMatchForm? ClassifyToken(
        string queryToken,
        string indexedToken,
        bool allowFuzzy)
    {
        if (string.Equals(queryToken, indexedToken, StringComparison.Ordinal))
        {
            return LexicalMatchForm.ExactToken;
        }

        if (indexedToken.StartsWith(queryToken, StringComparison.Ordinal))
        {
            return LexicalMatchForm.PrefixToken;
        }

        if (!allowFuzzy)
        {
            return null;
        }

        Span<Rune> queryScalars =
            stackalloc Rune[SearchGrammarLimits.MaximumNormalizedTokenRunes];
        int scalarLength = BoundedEditDistance.CopyScalars(
            queryToken,
            queryScalars,
            nameof(queryToken));
        int maximumDistance = FuzzyTermMatcher.GetMaximumDistance(scalarLength);
        if (maximumDistance == 0 ||
            !BoundedEditDistance.TryCalculate(
                queryToken,
                indexedToken,
                maximumDistance,
                out int distance))
        {
            return null;
        }

        return distance switch
        {
            1 => LexicalMatchForm.FuzzyTokenDistance1,
            2 => LexicalMatchForm.FuzzyTokenDistance2,
            _ => null,
        };
    }

    private static bool ContainsPhrase(
        ImmutableArray<string> indexedTokens,
        ImmutableArray<string> phraseTokens)
    {
        if (phraseTokens.Length > indexedTokens.Length)
        {
            return false;
        }

        int lastStart = indexedTokens.Length - phraseTokens.Length;
        for (int start = 0; start <= lastStart; start++)
        {
            bool matches = true;
            for (int offset = 0; offset < phraseTokens.Length; offset++)
            {
                if (!string.Equals(
                    indexedTokens[start + offset],
                    phraseTokens[offset],
                    StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static LexicalClauseMatch? Best(
        LexicalClauseMatch? left,
        LexicalClauseMatch? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return Compare(left, right) <= 0 ? left : right;
    }

    private static int Compare(LexicalClauseMatch left, LexicalClauseMatch right)
    {
        int result = GetWorstFormRank(left.Facts).CompareTo(
            GetWorstFormRank(right.Facts));
        if (result != 0)
        {
            return result;
        }

        result = GetTotalFormRank(left.Facts).CompareTo(
            GetTotalFormRank(right.Facts));
        if (result != 0)
        {
            return result;
        }

        result = right.IsCompleteValue.CompareTo(left.IsCompleteValue);
        if (result != 0)
        {
            return result;
        }

        int length = Math.Min(left.Facts.Length, right.Facts.Length);
        for (int index = 0; index < length; index++)
        {
            result = Compare(left.Facts[index], right.Facts[index]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Facts.Length.CompareTo(right.Facts.Length);
    }

    private static LexicalMatchFact? Best(
        LexicalMatchFact? left,
        LexicalMatchFact? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return Compare(left.Value, right.Value) <= 0 ? left : right;
    }

    private static int Compare(LexicalMatchFact left, LexicalMatchFact right)
    {
        int result = GetFormRank(left.Form).CompareTo(GetFormRank(right.Form));
        if (result != 0)
        {
            return result;
        }

        result = GetFieldRank(left.Field).CompareTo(GetFieldRank(right.Field));
        if (result != 0)
        {
            return result;
        }

        result = GetTitleOriginRank(left.TitleOrigin).CompareTo(
            GetTitleOriginRank(right.TitleOrigin));
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.IndexedValue, right.IndexedValue);
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(left.QueryValue, right.QueryValue);
    }

    private static int GetWorstFormRank(ImmutableArray<LexicalMatchFact> facts) =>
        facts.Max(static fact => GetFormRank(fact.Form));

    private static int GetTotalFormRank(ImmutableArray<LexicalMatchFact> facts) =>
        facts.Sum(static fact => GetFormRank(fact.Form));

    private static int GetFormRank(LexicalMatchForm form) => form switch
    {
        LexicalMatchForm.Phrase or LexicalMatchForm.ExactToken => 0,
        LexicalMatchForm.PrefixToken => 1,
        LexicalMatchForm.FuzzyTokenDistance1 => 2,
        LexicalMatchForm.FuzzyTokenDistance2 => 3,
        _ => throw new InvalidOperationException("Lexical match form has no rank."),
    };

    internal static int GetFieldRank(SearchEvidenceField field) => field switch
    {
        SearchEvidenceField.CanonicalTitle => 0,
        SearchEvidenceField.OriginalTitle => 1,
        SearchEvidenceField.TitleAlias => 2,
        SearchEvidenceField.Tag => 3,
        SearchEvidenceField.PrimaryLens => 4,
        SearchEvidenceField.SecondaryLens => 5,
        SearchEvidenceField.Franchise => 6,
        SearchEvidenceField.OriginalLanguage => 7,
        SearchEvidenceField.Summary => 8,
        SearchEvidenceField.ReleaseYear => 9,
        SearchEvidenceField.Runtime => 10,
        SearchEvidenceField.Scare => 11,
        SearchEvidenceField.Gore => 12,
        SearchEvidenceField.Dread => 13,
        SearchEvidenceField.Slasher => 14,
        SearchEvidenceField.Extreme => 15,
        SearchEvidenceField.Availability => 16,
        SearchEvidenceField.Status => 17,
        SearchEvidenceField.Exclusion => 18,
        _ => throw new InvalidOperationException(
            "Search evidence field has no deterministic rank."),
    };

    private static int GetTitleOriginRank(
        SearchTitleVariantOrigin? origin) => origin switch
        {
            SearchTitleVariantOrigin.CanonicalTitle => 0,
            SearchTitleVariantOrigin.CanonicalPunctuationDeletion => 1,
            SearchTitleVariantOrigin.OriginalTitle => 2,
            SearchTitleVariantOrigin.RomanNumeralAlias => 3,
            SearchTitleVariantOrigin.ExplicitAlias => 4,
            null => 5,
            _ => throw new InvalidOperationException(
                "Title variant origin has no deterministic rank."),
        };

    private static SearchEvidenceField GetEvidenceField(
        SearchTitleVariantOrigin origin) => origin switch
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
}
