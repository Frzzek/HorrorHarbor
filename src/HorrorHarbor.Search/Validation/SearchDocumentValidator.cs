using HorrorHarbor.Core.Models;
using HorrorHarbor.Search.Normalization;

namespace HorrorHarbor.Search.Validation;

internal static class SearchDocumentValidator
{
    public static void ValidateCandidateFields(RecommendationCandidate candidate)
    {
        _ = SearchDocumentValueValidator.NormalizeRequiredSearchableText(
            candidate.Title,
            nameof(candidate.Title));
        SearchDocumentValueValidator.ValidateRequiredSafeText(
            candidate.ScopeTier,
            nameof(candidate.ScopeTier));
        SearchDocumentValueValidator.ValidateOptionalSearchableText(
            candidate.PrimaryLens,
            nameof(candidate.PrimaryLens));
        SearchDocumentValueValidator.ValidateOptionalSearchableText(
            candidate.SecondaryLens,
            nameof(candidate.SecondaryLens));
        SearchDocumentValueValidator.ValidateOptionalSearchableText(
            candidate.Franchise,
            nameof(candidate.Franchise));
        SearchDocumentValueValidator.ValidateRequiredSafeText(
            candidate.AvailabilityLabel,
            nameof(candidate.AvailabilityLabel));
        SearchDocumentValueValidator.ValidateRequiredSafeText(
            candidate.PosterTone,
            nameof(candidate.PosterTone));
        SearchDocumentValueValidator.ValidateOptionalSearchableText(
            candidate.OriginalTitle,
            nameof(candidate.OriginalTitle));
        SearchDocumentValueValidator.ValidateOptionalSearchableText(
            candidate.OriginalLanguage,
            nameof(candidate.OriginalLanguage));
    }

    public static string[] NormalizeAndDeduplicateTags(
        IReadOnlyList<string> tags)
    {
        var values = new List<NormalizedTag>(tags.Count);
        foreach (string tag in tags)
        {
            NormalizedSearchText normalized =
                SearchDocumentValueValidator.NormalizeRequiredSearchableText(
                    tag,
                    "tag");
            values.Add(new NormalizedTag(tag, normalized.Value));
        }

        values.Sort(static (left, right) =>
        {
            int result = StringComparer.Ordinal.Compare(
                left.NormalizedValue,
                right.NormalizedValue);
            return result != 0
                ? result
                : StringComparer.Ordinal.Compare(left.DisplayValue, right.DisplayValue);
        });

        var result = new List<string>(values.Count);
        string? previousNormalizedValue = null;
        foreach (NormalizedTag value in values)
        {
            if (string.Equals(
                previousNormalizedValue,
                value.NormalizedValue,
                StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(value.DisplayValue);
            previousNormalizedValue = value.NormalizedValue;
        }

        return [.. result];
    }

    private sealed record NormalizedTag(
        string DisplayValue,
        string NormalizedValue);
}
