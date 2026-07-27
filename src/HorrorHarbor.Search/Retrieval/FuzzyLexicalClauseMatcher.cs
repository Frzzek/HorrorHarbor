using System.Collections.Immutable;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Retrieval;

internal sealed class FuzzyTokenPlan
{
    public FuzzyTokenPlan(
        string queryTerm,
        ImmutableArray<FuzzyTermMatch> fuzzyMatches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryTerm);
        if (fuzzyMatches.IsDefault)
        {
            throw new ArgumentException(
                "Fuzzy matches must be initialized.",
                nameof(fuzzyMatches));
        }

        QueryTerm = queryTerm;
        FuzzyMatches = fuzzyMatches;
    }

    public string QueryTerm { get; }

    public ImmutableArray<FuzzyTermMatch> FuzzyMatches { get; }

    public bool Matches(string indexedTerm)
    {
        if (indexedTerm.StartsWith(QueryTerm, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (FuzzyTermMatch fuzzyMatch in FuzzyMatches)
        {
            int comparison = StringComparer.Ordinal.Compare(
                fuzzyMatch.IndexedTerm,
                indexedTerm);
            if (comparison == 0)
            {
                return true;
            }

            if (comparison > 0)
            {
                break;
            }
        }

        return false;
    }
}

internal static class FuzzyLexicalClauseMatcher
{
    public static bool Matches(
        SearchIndexDocumentData document,
        SearchField field,
        ImmutableArray<FuzzyTokenPlan> tokenPlans)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (tokenPlans.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Fuzzy token plans must contain at least one token.",
                nameof(tokenPlans));
        }

        return field switch
        {
            SearchField.Any => MatchesAny(document, tokenPlans),
            SearchField.Title => MatchesTitles(document.TitleVariants, tokenPlans),
            SearchField.Tag => MatchesTags(document.Tags, tokenPlans),
            SearchField.Lens =>
                MatchesOptional(document.PrimaryLens, tokenPlans) ||
                MatchesOptional(document.SecondaryLens, tokenPlans),
            SearchField.Franchise =>
                MatchesOptional(document.Franchise, tokenPlans),
            SearchField.Language =>
                MatchesOptional(document.OriginalLanguage, tokenPlans),
            _ => throw new InvalidOperationException(
                "Fuzzy lexical matching received a non-lexical field."),
        };
    }

    private static bool MatchesAny(
        SearchIndexDocumentData document,
        ImmutableArray<FuzzyTokenPlan> tokenPlans)
    {
        foreach (FuzzyTokenPlan tokenPlan in tokenPlans)
        {
            if (!ContainsToken(document, tokenPlan))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesTitles(
        ImmutableArray<TitleVariant> variants,
        ImmutableArray<FuzzyTokenPlan> tokenPlans)
    {
        foreach (TitleVariant variant in variants)
        {
            if (MatchesValue(variant.Tokens, tokenPlans))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesTags(
        ImmutableArray<NormalizedSearchText> tags,
        ImmutableArray<FuzzyTokenPlan> tokenPlans)
    {
        foreach (NormalizedSearchText tag in tags)
        {
            if (MatchesValue(tag.Tokens, tokenPlans))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesOptional(
        NormalizedSearchText? value,
        ImmutableArray<FuzzyTokenPlan> tokenPlans) =>
        value is not null && MatchesValue(value.Tokens, tokenPlans);

    private static bool MatchesValue(
        ImmutableArray<string> indexedTerms,
        ImmutableArray<FuzzyTokenPlan> tokenPlans)
    {
        foreach (FuzzyTokenPlan tokenPlan in tokenPlans)
        {
            bool matched = false;
            foreach (string indexedTerm in indexedTerms)
            {
                if (tokenPlan.Matches(indexedTerm))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsToken(
        SearchIndexDocumentData document,
        FuzzyTokenPlan tokenPlan) =>
        ContainsToken(document.TitleVariants, tokenPlan) ||
        ContainsToken(document.Tags, tokenPlan) ||
        ContainsToken(document.PrimaryLens, tokenPlan) ||
        ContainsToken(document.SecondaryLens, tokenPlan) ||
        ContainsToken(document.Franchise, tokenPlan);

    private static bool ContainsToken(
        ImmutableArray<TitleVariant> values,
        FuzzyTokenPlan tokenPlan)
    {
        foreach (TitleVariant value in values)
        {
            if (ContainsToken(value.Tokens, tokenPlan))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsToken(
        ImmutableArray<NormalizedSearchText> values,
        FuzzyTokenPlan tokenPlan)
    {
        foreach (NormalizedSearchText value in values)
        {
            if (ContainsToken(value.Tokens, tokenPlan))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsToken(
        NormalizedSearchText? value,
        FuzzyTokenPlan tokenPlan) =>
        value is not null && ContainsToken(value.Tokens, tokenPlan);

    private static bool ContainsToken(
        ImmutableArray<string> indexedTerms,
        FuzzyTokenPlan tokenPlan)
    {
        foreach (string indexedTerm in indexedTerms)
        {
            if (tokenPlan.Matches(indexedTerm))
            {
                return true;
            }
        }

        return false;
    }
}
