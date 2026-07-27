using System.Collections.Immutable;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal static class LexicalClauseMatcher
{
    public static bool Matches(
        SearchIndexDocumentData document,
        SearchLexicalClause clause)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clause);

        return clause.Field switch
        {
            SearchField.Any => MatchesAny(document, clause),
            SearchField.Title => MatchesTitles(document.TitleVariants, clause),
            SearchField.Tag => MatchesTags(document.Tags, clause),
            SearchField.Lens =>
                MatchesOptional(document.PrimaryLens, clause) ||
                MatchesOptional(document.SecondaryLens, clause),
            SearchField.Franchise => MatchesOptional(document.Franchise, clause),
            SearchField.Language =>
                MatchesOptional(document.OriginalLanguage, clause),
            _ => throw new InvalidOperationException(
                "Lexical matching received a non-lexical field."),
        };
    }

    private static bool MatchesAny(
        SearchIndexDocumentData document,
        SearchLexicalClause clause)
    {
        if (clause.Mode == SearchLexicalMode.Phrase)
        {
            return MatchesTitles(document.TitleVariants, clause) ||
                ContainsPhrase(document.Tags, clause.Tokens) ||
                ContainsPhrase(document.PrimaryLens, clause.Tokens) ||
                ContainsPhrase(document.SecondaryLens, clause.Tokens) ||
                ContainsPhrase(document.Franchise, clause.Tokens);
        }

        foreach (string queryToken in clause.Tokens)
        {
            if (!ContainsTokenPrefix(document, queryToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTitles(
        ImmutableArray<TitleVariant> variants,
        SearchLexicalClause clause)
    {
        foreach (TitleVariant variant in variants)
        {
            if (MatchesNormalizedValue(variant.Tokens, clause))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesTags(
        ImmutableArray<NormalizedSearchText> tags,
        SearchLexicalClause clause)
    {
        foreach (NormalizedSearchText tag in tags)
        {
            bool matches = clause.Mode == SearchLexicalMode.Phrase
                ? string.Equals(
                    tag.Value,
                    clause.NormalizedValue,
                    StringComparison.Ordinal)
                : MatchesNormalizedValue(tag.Tokens, clause);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesOptional(
        NormalizedSearchText? value,
        SearchLexicalClause clause) =>
        value is not null && MatchesNormalizedValue(value.Tokens, clause);

    private static bool MatchesNormalizedValue(
        ImmutableArray<string> valueTokens,
        SearchLexicalClause clause)
    {
        if (clause.Mode == SearchLexicalMode.Phrase)
        {
            return ContainsPhrase(valueTokens, clause.Tokens);
        }

        foreach (string queryToken in clause.Tokens)
        {
            if (!ContainsTokenPrefix(valueTokens, queryToken))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsTokenPrefix(
        SearchIndexDocumentData document,
        string queryToken) =>
        ContainsTokenPrefix(document.TitleVariants, queryToken) ||
        ContainsTokenPrefix(document.Tags, queryToken) ||
        ContainsTokenPrefix(document.PrimaryLens, queryToken) ||
        ContainsTokenPrefix(document.SecondaryLens, queryToken) ||
        ContainsTokenPrefix(document.Franchise, queryToken);

    private static bool ContainsTokenPrefix(
        ImmutableArray<TitleVariant> values,
        string queryToken)
    {
        foreach (TitleVariant value in values)
        {
            if (ContainsTokenPrefix(value.Tokens, queryToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTokenPrefix(
        ImmutableArray<NormalizedSearchText> values,
        string queryToken)
    {
        foreach (NormalizedSearchText value in values)
        {
            if (ContainsTokenPrefix(value.Tokens, queryToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsTokenPrefix(
        NormalizedSearchText? value,
        string queryToken) =>
        value is not null && ContainsTokenPrefix(value.Tokens, queryToken);

    private static bool ContainsTokenPrefix(
        ImmutableArray<string> valueTokens,
        string queryToken)
    {
        foreach (string valueToken in valueTokens)
        {
            if (valueToken.StartsWith(queryToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPhrase(
        ImmutableArray<NormalizedSearchText> values,
        ImmutableArray<string> phraseTokens)
    {
        foreach (NormalizedSearchText value in values)
        {
            if (ContainsPhrase(value.Tokens, phraseTokens))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPhrase(
        NormalizedSearchText? value,
        ImmutableArray<string> phraseTokens) =>
        value is not null && ContainsPhrase(value.Tokens, phraseTokens);

    private static bool ContainsPhrase(
        ImmutableArray<string> valueTokens,
        ImmutableArray<string> phraseTokens)
    {
        if (phraseTokens.Length > valueTokens.Length)
        {
            return false;
        }

        int lastStart = valueTokens.Length - phraseTokens.Length;
        for (int start = 0; start <= lastStart; start++)
        {
            bool matches = true;
            for (int offset = 0; offset < phraseTokens.Length; offset++)
            {
                if (!string.Equals(
                    valueTokens[start + offset],
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
}
