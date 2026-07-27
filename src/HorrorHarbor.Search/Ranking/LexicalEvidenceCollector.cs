using System.Collections.Immutable;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Filtering;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;
using HorrorHarbor.Search.Retrieval;

namespace HorrorHarbor.Search.Ranking;

internal static class LexicalEvidenceCollector
{
    private const SearchNormalizationChanges BenignIdentityChanges =
        SearchNormalizationChanges.InvariantLowercased |
        SearchNormalizationChanges.WhitespaceNormalized;

    private const SearchNormalizationChanges NormalizedTagChanges =
        SearchNormalizationChanges.CompatibilityOrCanonicalForm |
        SearchNormalizationChanges.LatinDiacriticRemoved;

    public static LexicalEvidenceCollection Collect(
        SearchIndexSnapshot snapshot,
        int documentId,
        ParsedSearchQuery query,
        SearchUserContext userContext,
        bool allowFuzzy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(userContext);
        if ((uint)documentId >= (uint)snapshot.DocumentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(documentId),
                documentId,
                "Document ID is outside the snapshot.");
        }

        SearchIndexDocumentData document = snapshot.GetDocumentData(documentId);
        var clauseMatches = ImmutableArray.CreateBuilder<LexicalClauseMatch>(
            query.PositiveLexicalClauseCount);
        var evidence = new HashSet<SearchEvidence>();

        SearchLexicalTier? identityTier = TryCollectTitleIdentity(
            document,
            query,
            evidence);
        int positiveTokenCount = 0;
        int positiveHardFacetCount = 0;
        bool hasPositivePhrase = false;

        foreach (SearchQueryClause queryClause in query.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queryClause is SearchLexicalClause lexical &&
                lexical.Occurrence == SearchClauseOccurrence.Positive)
            {
                LexicalClauseMatch match = DocumentLexicalMatcher.Match(
                    document,
                    lexical,
                    allowFuzzy,
                    cancellationToken) ?? throw new InvalidOperationException(
                        "Evidence collection received a document that does not satisfy every positive lexical clause.");
                clauseMatches.Add(match);
                positiveTokenCount += lexical.Tokens.Length;
                hasPositivePhrase |= lexical.Mode == SearchLexicalMode.Phrase;
                AddLexicalEvidence(evidence, lexical, match);
            }
            else if (queryClause.Occurrence == SearchClauseOccurrence.Positive)
            {
                positiveHardFacetCount++;
                AddFacetEvidence(
                    evidence,
                    document.Document,
                    queryClause,
                    userContext);
            }
        }

        if (query.PositiveLexicalClauseCount > 0)
        {
            evidence.Add(new SearchEvidence(
                SearchEvidenceCode.AllPositiveClausesMatched,
                SearchEvidenceField.Summary));
        }

        if (query.ExclusionClauseCount > 0)
        {
            EnsureExclusionsSatisfied(document, query, userContext);
            evidence.Add(new SearchEvidence(
                SearchEvidenceCode.ExclusionSatisfied,
                SearchEvidenceField.Exclusion));
        }

        SearchLexicalTier? tier = identityTier ?? DetermineTier(
            clauseMatches.MoveToImmutable(),
            positiveTokenCount,
            hasPositivePhrase);
        if (identityTier is null &&
            tier == SearchLexicalTier.CanonicalTitlePhraseOrAllTokens &&
            !hasPositivePhrase &&
            positiveTokenCount > 1)
        {
            AddAllCanonicalTitleTokensEvidence(evidence, document, query);
        }

        SearchLocalStatus localStatus = userContext.GetStatusUnchecked(
            document.Document.CandidateId);
        if (localStatus != SearchLocalStatus.Unseen)
        {
            AddStatusEvidence(evidence, localStatus);
        }

        SearchEvidence[] ordered = [.. evidence];
        Array.Sort(ordered, SearchEvidenceComparer.Instance);
        return new LexicalEvidenceCollection(
            tier,
            [.. ordered],
            positiveHardFacetCount);
    }

    private static SearchLexicalTier? TryCollectTitleIdentity(
        SearchIndexDocumentData document,
        ParsedSearchQuery query,
        HashSet<SearchEvidence> evidence)
    {
        string? positiveTitleQuery = DocumentLexicalMatcher.BuildPositiveTitleQuery(
            query,
            out SearchNormalizationChanges queryChanges);
        if (positiveTitleQuery is null)
        {
            return null;
        }

        foreach (TitleVariant variant in document.TitleVariants)
        {
            if (!string.Equals(
                positiveTitleQuery,
                variant.Value,
                StringComparison.Ordinal))
            {
                continue;
            }

            SearchEvidenceCode code = GetTitleIdentityCode(
                variant,
                queryChanges);
            evidence.Add(new SearchEvidence(
                code,
                GetTitleField(variant.Origin),
                positiveTitleQuery,
                variant.Value,
                editDistance: null,
                variant.Origin));
            return variant.Origin is
                SearchTitleVariantOrigin.CanonicalTitle or
                SearchTitleVariantOrigin.CanonicalPunctuationDeletion
                ? SearchLexicalTier.ExactCanonicalTitle
                : SearchLexicalTier.ExactOriginalTitleOrAlias;
        }

        return null;
    }

    private static SearchEvidenceCode GetTitleIdentityCode(
        TitleVariant variant,
        SearchNormalizationChanges queryChanges) => variant.Origin switch
        {
            SearchTitleVariantOrigin.CanonicalPunctuationDeletion =>
                SearchEvidenceCode.CanonicalTitlePunctuationAlias,
            SearchTitleVariantOrigin.RomanNumeralAlias =>
                SearchEvidenceCode.RomanNumeralTitleAlias,
            SearchTitleVariantOrigin.OriginalTitle =>
                SearchEvidenceCode.OriginalTitleExact,
            SearchTitleVariantOrigin.ExplicitAlias =>
                SearchEvidenceCode.ExplicitTitleAlias,
            SearchTitleVariantOrigin.CanonicalTitle
                when variant.NormalizationChanges.HasFlag(
                    SearchNormalizationChanges.ApostropheRemoved) &&
                    !queryChanges.HasFlag(
                        SearchNormalizationChanges.ApostropheRemoved) =>
                SearchEvidenceCode.CanonicalTitlePunctuationAlias,
            SearchTitleVariantOrigin.CanonicalTitle
                when (variant.NormalizationChanges & ~BenignIdentityChanges) == 0 &&
                    (queryChanges & ~BenignIdentityChanges) == 0 =>
                SearchEvidenceCode.CanonicalTitleExact,
            SearchTitleVariantOrigin.CanonicalTitle =>
                SearchEvidenceCode.CanonicalTitleNormalizedExact,
            _ => throw new InvalidOperationException(
                "Title variant origin has no identity evidence code."),
        };

    private static void AddLexicalEvidence(
        HashSet<SearchEvidence> evidence,
        SearchLexicalClause clause,
        LexicalClauseMatch match)
    {
        foreach (LexicalMatchFact fact in match.Facts)
        {
            SearchEvidenceCode code = GetPrimaryLexicalEvidenceCode(
                clause,
                match,
                fact);
            evidence.Add(CreateEvidence(code, fact));

            SearchEvidenceCode formCode = GetFormEvidenceCode(fact);
            if (formCode != code)
            {
                evidence.Add(CreateEvidence(formCode, fact));
            }
        }
    }

    private static SearchEvidenceCode GetPrimaryLexicalEvidenceCode(
        SearchLexicalClause clause,
        LexicalClauseMatch match,
        LexicalMatchFact fact)
    {
        if (fact.Field == SearchEvidenceField.CanonicalTitle)
        {
            return fact.Form switch
            {
                LexicalMatchForm.Phrase => SearchEvidenceCode.CanonicalTitlePhrase,
                LexicalMatchForm.ExactToken => SearchEvidenceCode.CanonicalTitleToken,
                LexicalMatchForm.PrefixToken => SearchEvidenceCode.CanonicalTitlePrefix,
                LexicalMatchForm.FuzzyTokenDistance1 =>
                    SearchEvidenceCode.FuzzyTitleEditDistance1,
                LexicalMatchForm.FuzzyTokenDistance2 =>
                    SearchEvidenceCode.FuzzyTitleEditDistance2,
                _ => throw new InvalidOperationException(
                    "Canonical-title match form has no evidence code."),
            };
        }

        if (IsTitleField(fact.Field) &&
            fact.Form is LexicalMatchForm.FuzzyTokenDistance1 or
                LexicalMatchForm.FuzzyTokenDistance2)
        {
            return fact.Form == LexicalMatchForm.FuzzyTokenDistance1
                ? SearchEvidenceCode.FuzzyTitleEditDistance1
                : SearchEvidenceCode.FuzzyTitleEditDistance2;
        }

        if (fact.Field == SearchEvidenceField.Tag)
        {
            if (clause.Field == SearchField.Tag && match.IsCompleteValue)
            {
                return (fact.IndexedNormalizationChanges & NormalizedTagChanges) != 0
                    ? SearchEvidenceCode.TagExactNormalized
                    : SearchEvidenceCode.TagExact;
            }

            return fact.Form == LexicalMatchForm.ExactToken
                ? SearchEvidenceCode.TagTokenMatch
                : GetGenericEvidenceCode(fact.Form);
        }

        if (fact.Field == SearchEvidenceField.PrimaryLens)
        {
            return SearchEvidenceCode.PrimaryLensMatch;
        }

        if (fact.Field == SearchEvidenceField.SecondaryLens)
        {
            return SearchEvidenceCode.SecondaryLensMatch;
        }

        if (fact.Field == SearchEvidenceField.Franchise &&
            clause.Field == SearchField.Franchise)
        {
            return SearchEvidenceCode.FranchiseMatch;
        }

        if (fact.Field == SearchEvidenceField.OriginalLanguage)
        {
            return SearchEvidenceCode.OriginalLanguageMatch;
        }

        return GetGenericEvidenceCode(fact.Form);
    }

    private static SearchEvidenceCode GetGenericEvidenceCode(
        LexicalMatchForm form) => form switch
        {
            LexicalMatchForm.Phrase => SearchEvidenceCode.LexicalPhraseMatch,
            LexicalMatchForm.ExactToken => SearchEvidenceCode.LexicalTokenMatch,
            LexicalMatchForm.PrefixToken => SearchEvidenceCode.LexicalTokenPrefix,
            LexicalMatchForm.FuzzyTokenDistance1 =>
                SearchEvidenceCode.FuzzyNonTitleEditDistance1,
            LexicalMatchForm.FuzzyTokenDistance2 =>
                SearchEvidenceCode.FuzzyNonTitleEditDistance2,
            _ => throw new InvalidOperationException(
                "Lexical match form has no generic evidence code."),
        };

    private static SearchEvidenceCode GetFormEvidenceCode(
        LexicalMatchFact fact) => fact.Form switch
        {
            LexicalMatchForm.FuzzyTokenDistance1 when IsTitleField(fact.Field) =>
                SearchEvidenceCode.FuzzyTitleEditDistance1,
            LexicalMatchForm.FuzzyTokenDistance2 when IsTitleField(fact.Field) =>
                SearchEvidenceCode.FuzzyTitleEditDistance2,
            LexicalMatchForm.FuzzyTokenDistance1 =>
                SearchEvidenceCode.FuzzyNonTitleEditDistance1,
            LexicalMatchForm.FuzzyTokenDistance2 =>
                SearchEvidenceCode.FuzzyNonTitleEditDistance2,
            _ => GetGenericEvidenceCode(fact.Form),
        };

    private static SearchEvidence CreateEvidence(
        SearchEvidenceCode code,
        LexicalMatchFact fact) => new(
            code,
            fact.Field,
            fact.QueryValue,
            fact.IndexedValue,
            IsFuzzyEvidence(code) ? GetEditDistance(fact.Form) : null,
            fact.TitleOrigin);

    private static bool IsFuzzyEvidence(SearchEvidenceCode code) => code is
        SearchEvidenceCode.FuzzyTitleEditDistance1 or
        SearchEvidenceCode.FuzzyTitleEditDistance2 or
        SearchEvidenceCode.FuzzyNonTitleEditDistance1 or
        SearchEvidenceCode.FuzzyNonTitleEditDistance2;

    private static int? GetEditDistance(LexicalMatchForm form) => form switch
    {
        LexicalMatchForm.FuzzyTokenDistance1 => 1,
        LexicalMatchForm.FuzzyTokenDistance2 => 2,
        _ => null,
    };

    private static SearchLexicalTier? DetermineTier(
        ImmutableArray<LexicalClauseMatch> clauseMatches,
        int positiveTokenCount,
        bool hasPositivePhrase)
    {
        if (clauseMatches.IsDefaultOrEmpty)
        {
            return null;
        }

        if (clauseMatches.Any(static match => match.HasNonTitleFuzzyMatch))
        {
            return SearchLexicalTier.FuzzyNonTitleMatch;
        }

        if (clauseMatches.Any(static match => match.HasFuzzyMatch))
        {
            return SearchLexicalTier.FuzzyTitleMatch;
        }

        bool allExactCanonical = clauseMatches.All(static match =>
            match.IsExactCanonicalTitleOnly);
        if (allExactCanonical && (hasPositivePhrase || positiveTokenCount > 1))
        {
            return SearchLexicalTier.CanonicalTitlePhraseOrAllTokens;
        }

        if (clauseMatches.All(static match => match.IsCanonicalTitleOnly))
        {
            return SearchLexicalTier.CanonicalTitlePrefixOrToken;
        }

        if (clauseMatches.All(static match => match.IsExactCompleteNonTitleValue))
        {
            return SearchLexicalTier.ExactMetadataPhrase;
        }

        return SearchLexicalTier.ExactTokenMatch;
    }

    private static void AddFacetEvidence(
        HashSet<SearchEvidence> evidence,
        HorrorSearchDocument document,
        SearchQueryClause clause,
        SearchUserContext userContext)
    {
        if (!HardFacetEvaluator.Matches(document, clause, userContext))
        {
            throw new InvalidOperationException(
                "Evidence collection received a document that does not satisfy every positive hard facet.");
        }

        (SearchEvidenceCode Code, SearchEvidenceField Field) mapped = clause switch
        {
            SearchNumericClause numeric => MapNumericFacet(numeric.Field),
            SearchBooleanClause boolean => MapBooleanFacet(boolean.Field),
            SearchAvailabilityClause availability => MapAvailabilityFacet(
                availability.Kind),
            SearchStatusClause status => MapStatusFacet(status.Value),
            _ => throw new InvalidOperationException(
                "Positive hard facet has no evidence mapping."),
        };
        evidence.Add(new SearchEvidence(mapped.Code, mapped.Field));
    }

    private static void AddAllCanonicalTitleTokensEvidence(
        HashSet<SearchEvidence> evidence,
        SearchIndexDocumentData document,
        ParsedSearchQuery query)
    {
        string? queryValue = DocumentLexicalMatcher.BuildPositiveTitleQuery(
            query,
            out _);
        TitleVariant canonical = document.TitleVariants.First(static variant =>
            variant.Origin == SearchTitleVariantOrigin.CanonicalTitle);
        evidence.Add(new SearchEvidence(
            SearchEvidenceCode.CanonicalTitleAllQueryTokens,
            SearchEvidenceField.CanonicalTitle,
            queryValue,
            canonical.Value,
            editDistance: null,
            SearchTitleVariantOrigin.CanonicalTitle));
    }

    private static void AddStatusEvidence(
        HashSet<SearchEvidence> evidence,
        SearchLocalStatus status)
    {
        (SearchEvidenceCode Code, SearchEvidenceField Field) mapped =
            MapStatusFacet(status);
        evidence.Add(new SearchEvidence(mapped.Code, mapped.Field));
    }

    private static (SearchEvidenceCode, SearchEvidenceField) MapNumericFacet(
        SearchField field) => field switch
        {
            SearchField.Year => (
                SearchEvidenceCode.YearFilterSatisfied,
                SearchEvidenceField.ReleaseYear),
            SearchField.Runtime => (
                SearchEvidenceCode.RuntimeFilterSatisfied,
                SearchEvidenceField.Runtime),
            SearchField.Scare => (
                SearchEvidenceCode.ScareFilterSatisfied,
                SearchEvidenceField.Scare),
            SearchField.Gore => (
                SearchEvidenceCode.GoreFilterSatisfied,
                SearchEvidenceField.Gore),
            SearchField.Dread => (
                SearchEvidenceCode.DreadFilterSatisfied,
                SearchEvidenceField.Dread),
            _ => throw new InvalidOperationException(
                "Numeric facet has no evidence mapping."),
        };

    private static (SearchEvidenceCode, SearchEvidenceField) MapBooleanFacet(
        SearchField field) => field switch
        {
            SearchField.Slasher => (
                SearchEvidenceCode.SlasherFilterSatisfied,
                SearchEvidenceField.Slasher),
            SearchField.Extreme => (
                SearchEvidenceCode.ExtremeFilterSatisfied,
                SearchEvidenceField.Extreme),
            _ => throw new InvalidOperationException(
                "Boolean facet has no evidence mapping."),
        };

    private static (SearchEvidenceCode, SearchEvidenceField) MapAvailabilityFacet(
        SearchAvailabilityKind kind) => kind switch
        {
            SearchAvailabilityKind.Unknown => (
                SearchEvidenceCode.AvailabilityUnknown,
                SearchEvidenceField.Availability),
            SearchAvailabilityKind.Verified => (
                SearchEvidenceCode.AvailabilityVerified,
                SearchEvidenceField.Availability),
            SearchAvailabilityKind.ProviderDefined => (
                SearchEvidenceCode.AvailabilityProviderMatch,
                SearchEvidenceField.Availability),
            _ => throw new InvalidOperationException(
                "Availability facet has no evidence mapping."),
        };

    private static (SearchEvidenceCode, SearchEvidenceField) MapStatusFacet(
        SearchLocalStatus status) => status switch
        {
            SearchLocalStatus.Unseen => (
                SearchEvidenceCode.StatusUnseen,
                SearchEvidenceField.Status),
            SearchLocalStatus.Watched => (
                SearchEvidenceCode.StatusWatched,
                SearchEvidenceField.Status),
            SearchLocalStatus.Interested => (
                SearchEvidenceCode.StatusInterested,
                SearchEvidenceField.Status),
            SearchLocalStatus.NotNow => (
                SearchEvidenceCode.StatusNotNow,
                SearchEvidenceField.Status),
            SearchLocalStatus.Disliked => (
                SearchEvidenceCode.StatusDisliked,
                SearchEvidenceField.Status),
            _ => throw new InvalidOperationException(
                "Local status has no evidence mapping."),
        };

    private static void EnsureExclusionsSatisfied(
        SearchIndexDocumentData document,
        ParsedSearchQuery query,
        SearchUserContext userContext)
    {
        foreach (SearchQueryClause clause in query.Clauses)
        {
            if (clause.Occurrence != SearchClauseOccurrence.Excluded)
            {
                continue;
            }

            bool excluded = clause is SearchLexicalClause lexical
                ? LexicalClauseMatcher.Matches(document, lexical)
                : HardFacetEvaluator.Matches(
                    document.Document,
                    clause,
                    userContext);
            if (excluded)
            {
                throw new InvalidOperationException(
                    "Evidence collection received a document that violates an exclusion clause.");
            }
        }
    }

    private static SearchEvidenceField GetTitleField(
        SearchTitleVariantOrigin origin) => origin switch
        {
            SearchTitleVariantOrigin.CanonicalTitle or
            SearchTitleVariantOrigin.CanonicalPunctuationDeletion =>
                SearchEvidenceField.CanonicalTitle,
            SearchTitleVariantOrigin.OriginalTitle => SearchEvidenceField.OriginalTitle,
            SearchTitleVariantOrigin.RomanNumeralAlias or
            SearchTitleVariantOrigin.ExplicitAlias => SearchEvidenceField.TitleAlias,
            _ => throw new InvalidOperationException(
                "Title variant origin has no evidence field."),
        };

    private static bool IsTitleField(SearchEvidenceField field) => field is
        SearchEvidenceField.CanonicalTitle or
        SearchEvidenceField.OriginalTitle or
        SearchEvidenceField.TitleAlias;

    private sealed class SearchEvidenceComparer : IComparer<SearchEvidence>
    {
        public static SearchEvidenceComparer Instance { get; } = new();

        public int Compare(SearchEvidence? left, SearchEvidence? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            int result = GetEvidenceRank(left.Code).CompareTo(
                GetEvidenceRank(right.Code));
            if (result != 0)
            {
                return result;
            }

            result = StringComparer.Ordinal.Compare(left.StableCode, right.StableCode);
            if (result != 0)
            {
                return result;
            }

            result = DocumentLexicalMatcher.GetFieldRank(left.Field).CompareTo(
                DocumentLexicalMatcher.GetFieldRank(right.Field));
            if (result != 0)
            {
                return result;
            }

            result = StringComparer.Ordinal.Compare(
                left.NormalizedQueryValue,
                right.NormalizedQueryValue);
            if (result != 0)
            {
                return result;
            }

            result = StringComparer.Ordinal.Compare(
                left.NormalizedIndexedValue,
                right.NormalizedIndexedValue);
            if (result != 0)
            {
                return result;
            }

            result = Nullable.Compare(left.EditDistance, right.EditDistance);
            return result != 0
                ? result
                : Nullable.Compare(left.TitleOrigin, right.TitleOrigin);
        }

        private static int GetEvidenceRank(SearchEvidenceCode code) => code switch
        {
            SearchEvidenceCode.CanonicalTitleExact or
            SearchEvidenceCode.CanonicalTitleNormalizedExact or
            SearchEvidenceCode.CanonicalTitlePunctuationAlias => 0,
            SearchEvidenceCode.RomanNumeralTitleAlias or
            SearchEvidenceCode.OriginalTitleExact or
            SearchEvidenceCode.ExplicitTitleAlias => 1,
            SearchEvidenceCode.CanonicalTitlePhrase or
            SearchEvidenceCode.CanonicalTitleAllQueryTokens => 2,
            SearchEvidenceCode.CanonicalTitleToken or
            SearchEvidenceCode.CanonicalTitlePrefix => 3,
            SearchEvidenceCode.TagExact or
            SearchEvidenceCode.TagExactNormalized or
            SearchEvidenceCode.PrimaryLensMatch or
            SearchEvidenceCode.SecondaryLensMatch or
            SearchEvidenceCode.FranchiseMatch => 4,
            SearchEvidenceCode.LexicalPhraseMatch or
            SearchEvidenceCode.LexicalTokenMatch or
            SearchEvidenceCode.LexicalTokenPrefix or
            SearchEvidenceCode.TagTokenMatch or
            SearchEvidenceCode.OriginalLanguageMatch => 5,
            SearchEvidenceCode.FuzzyTitleEditDistance1 or
            SearchEvidenceCode.FuzzyTitleEditDistance2 => 6,
            SearchEvidenceCode.FuzzyNonTitleEditDistance1 or
            SearchEvidenceCode.FuzzyNonTitleEditDistance2 => 7,
            SearchEvidenceCode.AllPositiveClausesMatched => 8,
            SearchEvidenceCode.YearFilterSatisfied or
            SearchEvidenceCode.RuntimeFilterSatisfied or
            SearchEvidenceCode.ScareFilterSatisfied or
            SearchEvidenceCode.GoreFilterSatisfied or
            SearchEvidenceCode.DreadFilterSatisfied or
            SearchEvidenceCode.SlasherFilterSatisfied or
            SearchEvidenceCode.ExtremeFilterSatisfied or
            SearchEvidenceCode.AvailabilityUnknown or
            SearchEvidenceCode.AvailabilityVerified or
            SearchEvidenceCode.AvailabilityProviderMatch or
            SearchEvidenceCode.StatusUnseen or
            SearchEvidenceCode.StatusWatched or
            SearchEvidenceCode.StatusInterested or
            SearchEvidenceCode.StatusNotNow or
            SearchEvidenceCode.StatusDisliked => 9,
            SearchEvidenceCode.ExclusionSatisfied => 10,
            _ => throw new InvalidOperationException(
                "Search evidence code has no deterministic rank."),
        };
    }
}
