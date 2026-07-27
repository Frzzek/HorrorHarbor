using System.Collections.Frozen;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Indexing;

public sealed class SearchIndexSnapshot
{
    public const int SchemaVersion = 1;

    private readonly SearchIndexData _data;

    internal SearchIndexSnapshot(
        SearchIndexData data,
        string catalogFingerprint)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFingerprint);

        CatalogFingerprint = catalogFingerprint;
        NormalizerVersion = SearchTextNormalizerV1.Version;
        SnapshotVersion = $"{SchemaVersion}:{catalogFingerprint}";
    }

    public int DocumentCount => _data.Documents.Length;

    public int NormalizerVersion { get; }

    public string SnapshotVersion { get; }

    public string CatalogFingerprint { get; }

    public SearchIndexCoverage Coverage => _data.Coverage;

    internal HorrorSearchDocument GetDocument(int documentId) =>
        _data.Documents[documentId].Document;

    internal SearchIndexDocumentData GetDocumentData(int documentId) =>
        _data.Documents[documentId];

    internal bool TryGetDocumentId(string candidateId, out int documentId) =>
        _data.CandidateIds.TryGetValue(candidateId, out documentId);

    internal ReadOnlySpan<int> GetAllDocumentIds() => _data.AllDocumentIds;

    internal ReadOnlySpan<int> GetCanonicalTitlePostings(string value) =>
        _data.CanonicalTitles.Get(value);

    internal ReadOnlySpan<int> GetTitleVariantPostings(string value) =>
        _data.TitleVariants.Get(value);

    internal ReadOnlySpan<int> GetTitleTokenPostings(string value) =>
        _data.TitleTokens.Get(value);

    internal ReadOnlySpan<int> GetExactTagPostings(string value) =>
        _data.ExactTags.Get(value);

    internal ReadOnlySpan<int> GetTagTokenPostings(string value) =>
        _data.TagTokens.Get(value);

    internal ReadOnlySpan<int> GetPrimaryLensPostings(string value) =>
        _data.PrimaryLenses.Get(value);

    internal ReadOnlySpan<int> GetSecondaryLensPostings(string value) =>
        _data.SecondaryLenses.Get(value);

    internal ReadOnlySpan<int> GetLensTokenPostings(string value) =>
        _data.LensTokens.Get(value);

    internal ReadOnlySpan<int> GetFranchisePostings(string value) =>
        _data.Franchises.Get(value);

    internal ReadOnlySpan<int> GetFranchiseTokenPostings(string value) =>
        _data.FranchiseTokens.Get(value);

    internal ReadOnlySpan<int> GetOriginalLanguagePostings(string value) =>
        _data.OriginalLanguages.Get(value);

    internal ReadOnlySpan<int> GetOriginalLanguageTokenPostings(string value) =>
        _data.OriginalLanguageTokens.Get(value);

    internal ReadOnlySpan<int> GetAvailabilityPostings(string value) =>
        _data.AvailabilityProviders.Get(value);

    internal ReadOnlySpan<int> GetSlasherPostings() => _data.SlasherDocumentIds;

    internal ReadOnlySpan<int> GetExtremePostings() => _data.ExtremeDocumentIds;

    internal ReadOnlySpan<NumericFacetValue> GetReleaseYears() =>
        _data.ReleaseYears;

    internal ReadOnlySpan<NumericFacetValue> GetRuntimes() => _data.Runtimes;

    internal ReadOnlySpan<NumericFacetValue> GetScareIntensities() =>
        _data.ScareIntensities;

    internal ReadOnlySpan<NumericFacetValue> GetGoreIntensities() =>
        _data.GoreIntensities;

    internal ReadOnlySpan<NumericFacetValue> GetDreadIntensities() =>
        _data.DreadIntensities;

    internal ReadOnlySpan<string> GetTitleVocabulary() =>
        _data.TitleTokens.Vocabulary;

    internal ReadOnlySpan<string> GetTagVocabulary() =>
        _data.TagTokens.Vocabulary;

    internal ReadOnlySpan<string> GetLensVocabulary() =>
        _data.LensTokens.Vocabulary;

    internal ReadOnlySpan<string> GetFranchiseVocabulary() =>
        _data.FranchiseTokens.Vocabulary;

    internal ReadOnlySpan<string> GetOriginalLanguageVocabulary() =>
        _data.OriginalLanguageTokens.Vocabulary;

    internal FuzzyTermIndex GetTitleFuzzyTerms() => _data.TitleFuzzyTerms;

    internal FuzzyTermIndex GetTagFuzzyTerms() => _data.TagFuzzyTerms;

    internal FuzzyTermIndex GetLensFuzzyTerms() => _data.LensFuzzyTerms;

    internal FuzzyTermIndex GetFranchiseFuzzyTerms() =>
        _data.FranchiseFuzzyTerms;

    internal FuzzyTermIndex GetOriginalLanguageFuzzyTerms() =>
        _data.OriginalLanguageFuzzyTerms;
}

internal sealed class SearchIndexData
{
    public required SearchIndexDocumentData[] Documents { get; init; }

    public required FrozenDictionary<string, int> CandidateIds { get; init; }

    public required int[] AllDocumentIds { get; init; }

    public required PostingListIndex CanonicalTitles { get; init; }

    public required PostingListIndex TitleVariants { get; init; }

    public required PostingListIndex TitleTokens { get; init; }

    public required PostingListIndex ExactTags { get; init; }

    public required PostingListIndex TagTokens { get; init; }

    public required PostingListIndex PrimaryLenses { get; init; }

    public required PostingListIndex SecondaryLenses { get; init; }

    public required PostingListIndex LensTokens { get; init; }

    public required PostingListIndex Franchises { get; init; }

    public required PostingListIndex FranchiseTokens { get; init; }

    public required PostingListIndex OriginalLanguages { get; init; }

    public required PostingListIndex OriginalLanguageTokens { get; init; }

    public required FuzzyTermIndex TitleFuzzyTerms { get; init; }

    public required FuzzyTermIndex TagFuzzyTerms { get; init; }

    public required FuzzyTermIndex LensFuzzyTerms { get; init; }

    public required FuzzyTermIndex FranchiseFuzzyTerms { get; init; }

    public required FuzzyTermIndex OriginalLanguageFuzzyTerms { get; init; }

    public required PostingListIndex AvailabilityProviders { get; init; }

    public required int[] SlasherDocumentIds { get; init; }

    public required int[] ExtremeDocumentIds { get; init; }

    public required NumericFacetValue[] ReleaseYears { get; init; }

    public required NumericFacetValue[] Runtimes { get; init; }

    public required NumericFacetValue[] ScareIntensities { get; init; }

    public required NumericFacetValue[] GoreIntensities { get; init; }

    public required NumericFacetValue[] DreadIntensities { get; init; }

    public required SearchIndexCoverage Coverage { get; init; }
}
