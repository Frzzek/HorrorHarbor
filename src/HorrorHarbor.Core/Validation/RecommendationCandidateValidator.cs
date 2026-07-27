using System.Globalization;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.ValueObjects;

namespace HorrorHarbor.Core.Validation;

public static class RecommendationCandidateValidator
{
    private const int EarliestReleaseYear = 1888;
    private const int LatestReleaseYear = 2200;
    private const int MaximumRuntimeMinutes = 1_000;
    private const int MaximumTextLength = 512;
    private const int MaximumTagCount = 32;

    public static void Validate(RecommendationCandidate candidate, string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(candidate, parameterName);
        CandidateIdValidator.Validate(candidate.CandidateId, nameof(candidate.CandidateId));
        ValidateRequired(candidate.Title, nameof(candidate.Title), MaximumTextLength);
        ValidateRequired(candidate.ScopeTier, nameof(candidate.ScopeTier), MaximumTextLength);
        ValidateRequired(candidate.AvailabilityLabel, nameof(candidate.AvailabilityLabel), MaximumTextLength);
        ValidateRequired(candidate.PosterTone, nameof(candidate.PosterTone), MaximumTextLength);
        ValidateOptional(candidate.OriginalTitle, nameof(candidate.OriginalTitle), MaximumTextLength);
        ValidateOptional(candidate.OriginalLanguage, nameof(candidate.OriginalLanguage), 20);
        if (candidate.TmdbId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(candidate),
                candidate.TmdbId,
                "TMDB ID must be positive when supplied.");
        }

        if (candidate.PosterTone.Length != 7 || candidate.PosterTone[0] != '#' ||
            !candidate.PosterTone.Skip(1).All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Poster tone must use #RRGGBB hexadecimal format.",
                parameterName ?? nameof(candidate));
        }

        if (candidate.ReleaseYear is < EarliestReleaseYear or > LatestReleaseYear)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(candidate),
                candidate.ReleaseYear,
                $"Release year must be between {EarliestReleaseYear} and {LatestReleaseYear}.");
        }

        ValidateIntensity(candidate.ScareIntensity, nameof(candidate.ScareIntensity));
        ValidateIntensity(candidate.GoreIntensity, nameof(candidate.GoreIntensity));
        ValidateIntensity(candidate.DreadIntensity, nameof(candidate.DreadIntensity));
        ValidateUnitInterval(candidate.DiscoveryScore, nameof(candidate.DiscoveryScore));
        ValidateUnitInterval(candidate.QualityScore, nameof(candidate.QualityScore));
        ValidateUnitInterval(candidate.AvailabilityScore, nameof(candidate.AvailabilityScore));

        if (!double.IsFinite(candidate.UserFeedbackBoost) || candidate.UserFeedbackBoost is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(candidate),
                candidate.UserFeedbackBoost,
                "Feedback boost must be finite and between -1 and 1.");
        }

        if (candidate.RuntimeMinutes is <= 0 or > MaximumRuntimeMinutes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(candidate),
                candidate.RuntimeMinutes,
                $"Runtime must be between 1 and {MaximumRuntimeMinutes} minutes when supplied.");
        }

        if (candidate.Tags is null)
        {
            throw new ArgumentException("Tags cannot be null.", parameterName ?? nameof(candidate));
        }

        if (candidate.Tags.Count > MaximumTagCount)
        {
            throw new ArgumentException($"A candidate cannot contain more than {MaximumTagCount} tags.", parameterName ?? nameof(candidate));
        }

        var uniqueTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in candidate.Tags)
        {
            ValidateRequired(tag, "tag", MaximumTextLength);
            if (!uniqueTags.Add(tag.Trim()))
            {
                throw new ArgumentException($"Duplicate tag '{tag}'.", parameterName ?? nameof(candidate));
            }
        }

        _ = TitleNormalizer.NormalizeForKey(candidate.Title);
    }

    public static string IdentityKey(RecommendationCandidate candidate)
    {
        Validate(candidate);
        return $"{TitleNormalizer.NormalizeForKey(candidate.Title)}|{candidate.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}";
    }

    private static void ValidateIntensity(int value, string name)
    {
        if (value is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(name, value, "Intensity must be between 0 and 5.");
        }
    }

    private static void ValidateUnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must be finite and between 0 and 1.");
        }
    }

    private static void ValidateRequired(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"{name} cannot exceed {maximumLength} characters.", name);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException($"{name} cannot contain control characters.", name);
        }
    }

    private static void ValidateOptional(string? value, string name, int maximumLength)
    {
        if (value is null)
        {
            return;
        }

        ValidateRequired(value, name, maximumLength);
    }
}
