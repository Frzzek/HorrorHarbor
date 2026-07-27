using System.Globalization;
using System.Text;

namespace HorrorHarbor.Posters;

public static class TmdbIdentityMatcher
{
    private const double MinimumConfidence = 0.85;
    private const double MinimumMargin = 0.10;

    public static TmdbMatch? Select(PosterDescriptor descriptor, IReadOnlyList<TmdbMovieResult?> results)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(results);

        if (descriptor.TmdbId is int explicitId)
        {
            TmdbMovieResult? explicitMatch = results.FirstOrDefault(movie => movie?.Id == explicitId);
            return explicitMatch is null || string.IsNullOrWhiteSpace(explicitMatch.PosterPath)
                ? null
                : new TmdbMatch(explicitMatch, 1, "catalog-tmdb-id");
        }

        TmdbMatch[] ranked = results
            .Where(static movie => movie is not null && !string.IsNullOrWhiteSpace(movie.PosterPath))
            .Select(movie => Score(descriptor, movie!))
            .OrderByDescending(static match => match.Confidence)
            .ThenBy(static match => match.Movie.Id)
            .ToArray();
        if (ranked.Length == 0 || ranked[0].Confidence < MinimumConfidence)
        {
            return null;
        }

        if (ranked.Length > 1 && ranked[0].Confidence - ranked[1].Confidence < MinimumMargin)
        {
            return null;
        }

        return ranked[0];
    }

    private static TmdbMatch Score(PosterDescriptor descriptor, TmdbMovieResult movie)
    {
        string wantedTitle = Normalize(descriptor.Title);
        string title = Normalize(movie.Title);
        string originalTitle = Normalize(movie.OriginalTitle);
        double score = 0;
        var method = new List<string>(3);
        if (wantedTitle == title)
        {
            score += 0.65;
            method.Add("title-exact");
        }
        else if (wantedTitle == originalTitle)
        {
            score += 0.60;
            method.Add("original-title-exact");
        }

        if (descriptor.ReleaseYear is int wantedYear && TryYear(movie.ReleaseDate, out int foundYear))
        {
            int difference = Math.Abs(wantedYear - foundYear);
            if (difference == 0)
            {
                score += 0.30;
                method.Add("year-exact");
            }
            else if (difference == 1)
            {
                score += 0.18;
                method.Add("year-adjacent");
            }
        }

        if (!string.IsNullOrWhiteSpace(descriptor.OriginalLanguage) &&
            string.Equals(descriptor.OriginalLanguage, movie.OriginalLanguage, StringComparison.OrdinalIgnoreCase))
        {
            score += 0.05;
            method.Add("language-exact");
        }

        return new TmdbMatch(movie, Math.Min(1, score), string.Join('+', method));
    }

    private static bool TryYear(string? date, out int year)
    {
        year = 0;
        if (!DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed))
        {
            return false;
        }

        year = parsed.Year;
        return year > 0;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
