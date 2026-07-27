using System.Collections.Frozen;
using System.Collections.Immutable;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Indexing;

internal sealed class SearchIndexAssembler
{
    private readonly SearchIndexDocumentData[] _documents;
    private readonly Dictionary<string, int> _candidateIds;
    private readonly PostingListBuilder _canonicalTitles = new();
    private readonly PostingListBuilder _titleVariants = new();
    private readonly PostingListBuilder _titleTokens = new();
    private readonly PostingListBuilder _exactTags = new();
    private readonly PostingListBuilder _tagTokens = new();
    private readonly PostingListBuilder _primaryLenses = new();
    private readonly PostingListBuilder _secondaryLenses = new();
    private readonly PostingListBuilder _lensTokens = new();
    private readonly PostingListBuilder _franchises = new();
    private readonly PostingListBuilder _franchiseTokens = new();
    private readonly PostingListBuilder _originalLanguages = new();
    private readonly PostingListBuilder _originalLanguageTokens = new();
    private readonly PostingListBuilder _availabilityProviders = new();
    private readonly List<int> _slasherDocumentIds = [];
    private readonly List<int> _extremeDocumentIds = [];
    private readonly List<NumericFacetValue> _releaseYears = [];
    private readonly List<NumericFacetValue> _runtimes = [];
    private readonly List<NumericFacetValue> _scareIntensities = [];
    private readonly List<NumericFacetValue> _goreIntensities = [];
    private readonly List<NumericFacetValue> _dreadIntensities = [];
    private int _primaryLensCoverage;
    private int _secondaryLensCoverage;
    private int _franchiseCoverage;
    private int _originalTitleCoverage;
    private int _originalLanguageCoverage;
    private int _verifiedAvailabilityCoverage;

    public SearchIndexAssembler(int documentCount)
    {
        _documents = new SearchIndexDocumentData[documentCount];
        _candidateIds = new Dictionary<string, int>(
            documentCount,
            StringComparer.Ordinal);
    }

    public void Add(HorrorSearchDocument document, int documentId)
    {
        RecommendationCandidate candidate = document.Candidate;
        ImmutableArray<TitleVariant> titleVariants =
            TitleVariantBuilder.Build(document);
        ImmutableArray<NormalizedSearchText> tags =
            NormalizeMany(candidate.Tags);
        NormalizedSearchText? primaryLens = NormalizeOptional(candidate.PrimaryLens);
        NormalizedSearchText? secondaryLens = NormalizeOptional(candidate.SecondaryLens);
        NormalizedSearchText? franchise = NormalizeOptional(candidate.Franchise);
        NormalizedSearchText? originalLanguage =
            NormalizeOptional(candidate.OriginalLanguage);

        var data = new SearchIndexDocumentData(
            document,
            titleVariants,
            tags,
            primaryLens,
            secondaryLens,
            franchise,
            originalLanguage);
        _documents[documentId] = data;
        _candidateIds.Add(candidate.CandidateId, documentId);

        IndexTitles(titleVariants, documentId);
        IndexTags(tags, documentId);
        IndexOptionalField(
            primaryLens,
            _primaryLenses,
            _lensTokens,
            documentId);
        IndexOptionalField(
            secondaryLens,
            _secondaryLenses,
            _lensTokens,
            documentId);
        IndexOptionalField(
            franchise,
            _franchises,
            _franchiseTokens,
            documentId);
        IndexOptionalField(
            originalLanguage,
            _originalLanguages,
            _originalLanguageTokens,
            documentId);

        foreach (SearchAvailabilityFact fact in document.AvailabilityFacts)
        {
            _availabilityProviders.Add(
                fact.NormalizedProviderValue,
                documentId);
        }

        IndexFacets(candidate, document, documentId);
    }

    public SearchIndexSnapshot Build(
        string catalogFingerprint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SortNumericFacets(_releaseYears);
        SortNumericFacets(_runtimes);
        SortNumericFacets(_scareIntensities);
        SortNumericFacets(_goreIntensities);
        SortNumericFacets(_dreadIntensities);

        PostingListIndex titleTokens = _titleTokens.Build();
        PostingListIndex tagTokens = _tagTokens.Build();
        PostingListIndex lensTokens = _lensTokens.Build();
        PostingListIndex franchiseTokens = _franchiseTokens.Build();
        PostingListIndex originalLanguageTokens =
            _originalLanguageTokens.Build();

        int[] allDocumentIds = new int[_documents.Length];
        for (int index = 0; index < allDocumentIds.Length; index++)
        {
            allDocumentIds[index] = index;
        }

        var coverage = new SearchIndexCoverage(
            _releaseYears.Count,
            _runtimes.Count,
            _primaryLensCoverage,
            _secondaryLensCoverage,
            _franchiseCoverage,
            _originalTitleCoverage,
            _originalLanguageCoverage,
            _verifiedAvailabilityCoverage,
            _slasherDocumentIds.Count,
            _extremeDocumentIds.Count);

        var data = new SearchIndexData
        {
            Documents = _documents,
            CandidateIds = _candidateIds.ToFrozenDictionary(StringComparer.Ordinal),
            AllDocumentIds = allDocumentIds,
            CanonicalTitles = _canonicalTitles.Build(),
            TitleVariants = _titleVariants.Build(),
            TitleTokens = titleTokens,
            ExactTags = _exactTags.Build(),
            TagTokens = tagTokens,
            PrimaryLenses = _primaryLenses.Build(),
            SecondaryLenses = _secondaryLenses.Build(),
            LensTokens = lensTokens,
            Franchises = _franchises.Build(),
            FranchiseTokens = franchiseTokens,
            OriginalLanguages = _originalLanguages.Build(),
            OriginalLanguageTokens = originalLanguageTokens,
            TitleFuzzyTerms = FuzzyTermIndex.Build(
                titleTokens.Vocabulary,
                cancellationToken),
            TagFuzzyTerms = FuzzyTermIndex.Build(
                tagTokens.Vocabulary,
                cancellationToken),
            LensFuzzyTerms = FuzzyTermIndex.Build(
                lensTokens.Vocabulary,
                cancellationToken),
            FranchiseFuzzyTerms = FuzzyTermIndex.Build(
                franchiseTokens.Vocabulary,
                cancellationToken),
            OriginalLanguageFuzzyTerms = FuzzyTermIndex.Build(
                originalLanguageTokens.Vocabulary,
                cancellationToken),
            AvailabilityProviders = _availabilityProviders.Build(),
            SlasherDocumentIds = _slasherDocumentIds.ToArray(),
            ExtremeDocumentIds = _extremeDocumentIds.ToArray(),
            ReleaseYears = _releaseYears.ToArray(),
            Runtimes = _runtimes.ToArray(),
            ScareIntensities = _scareIntensities.ToArray(),
            GoreIntensities = _goreIntensities.ToArray(),
            DreadIntensities = _dreadIntensities.ToArray(),
            Coverage = coverage,
        };

        return new SearchIndexSnapshot(data, catalogFingerprint);
    }

    private void IndexTitles(
        ImmutableArray<TitleVariant> variants,
        int documentId)
    {
        foreach (TitleVariant variant in variants)
        {
            if (variant.Origin == SearchTitleVariantOrigin.CanonicalTitle)
            {
                _canonicalTitles.Add(variant.Value, documentId);
            }

            _titleVariants.Add(variant.Value, documentId);
            AddTokens(_titleTokens, variant.Tokens, documentId);
        }
    }

    private void IndexTags(
        ImmutableArray<NormalizedSearchText> tags,
        int documentId)
    {
        foreach (NormalizedSearchText tag in tags)
        {
            _exactTags.Add(tag.Value, documentId);
            AddTokens(_tagTokens, tag.Tokens, documentId);
        }
    }

    private static void IndexOptionalField(
        NormalizedSearchText? normalized,
        PostingListBuilder exactPostings,
        PostingListBuilder tokenPostings,
        int documentId)
    {
        if (normalized is null)
        {
            return;
        }

        exactPostings.Add(normalized.Value, documentId);
        AddTokens(tokenPostings, normalized.Tokens, documentId);
    }

    private void IndexFacets(
        RecommendationCandidate candidate,
        HorrorSearchDocument document,
        int documentId)
    {
        AddOptionalNumeric(_releaseYears, candidate.ReleaseYear, documentId);
        AddOptionalNumeric(_runtimes, candidate.RuntimeMinutes, documentId);
        _scareIntensities.Add(new NumericFacetValue(
            candidate.ScareIntensity,
            documentId));
        _goreIntensities.Add(new NumericFacetValue(
            candidate.GoreIntensity,
            documentId));
        _dreadIntensities.Add(new NumericFacetValue(
            candidate.DreadIntensity,
            documentId));

        if (candidate.IsSlasher)
        {
            _slasherDocumentIds.Add(documentId);
        }

        if (candidate.IsExtreme)
        {
            _extremeDocumentIds.Add(documentId);
        }

        _primaryLensCoverage += candidate.PrimaryLens is null ? 0 : 1;
        _secondaryLensCoverage += candidate.SecondaryLens is null ? 0 : 1;
        _franchiseCoverage += candidate.Franchise is null ? 0 : 1;
        _originalTitleCoverage += candidate.OriginalTitle is null ? 0 : 1;
        _originalLanguageCoverage += candidate.OriginalLanguage is null ? 0 : 1;
        _verifiedAvailabilityCoverage +=
            document.AvailabilityState == SearchAvailabilityState.Verified ? 1 : 0;
    }

    private static ImmutableArray<NormalizedSearchText> NormalizeMany(
        IReadOnlyList<string> values)
    {
        var result = ImmutableArray.CreateBuilder<NormalizedSearchText>(values.Count);
        foreach (string value in values)
        {
            result.Add(SearchTextNormalizerV1.Normalize(value));
        }

        return result.MoveToImmutable();
    }

    private static NormalizedSearchText? NormalizeOptional(string? value) =>
        value is null ? null : SearchTextNormalizerV1.Normalize(value);

    private static void AddTokens(
        PostingListBuilder postings,
        ImmutableArray<string> tokens,
        int documentId)
    {
        foreach (string token in tokens)
        {
            postings.Add(token, documentId);
        }
    }

    private static void AddOptionalNumeric(
        List<NumericFacetValue> values,
        int? value,
        int documentId)
    {
        if (value.HasValue)
        {
            values.Add(new NumericFacetValue(value.Value, documentId));
        }
    }

    private static void SortNumericFacets(List<NumericFacetValue> values) =>
        values.Sort(static (left, right) =>
        {
            int result = left.Value.CompareTo(right.Value);
            return result != 0
                ? result
                : left.DocumentId.CompareTo(right.DocumentId);
        });
}
