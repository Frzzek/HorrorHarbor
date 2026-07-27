using System.Text;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Validation;

internal static class SearchDocumentValueValidator
{
    public static NormalizedSearchText NormalizeRequiredSearchableText(
        string value,
        string parameterName)
    {
        ValidateRequiredText(
            value,
            parameterName,
            SearchDocumentLimits.MaximumSearchableTextCodeUnits);

        NormalizedSearchText normalized = SearchTextNormalizerV1.Normalize(value);
        if (normalized.Value.Length == 0 || normalized.Tokens.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Searchable text must contain at least one letter or digit.",
                parameterName);
        }

        foreach (string token in normalized.Tokens)
        {
            if (CountRunes(token) > SearchGrammarLimits.MaximumNormalizedTokenRunes)
            {
                throw new ArgumentException(
                    $"A normalized search token cannot exceed {SearchGrammarLimits.MaximumNormalizedTokenRunes} Unicode scalar values.",
                    parameterName);
            }
        }

        return normalized;
    }

    public static void ValidateRequiredSafeText(
        string value,
        string parameterName,
        int maximumCodeUnits = SearchDocumentLimits.MaximumSearchableTextCodeUnits)
    {
        ValidateRequiredText(value, parameterName, maximumCodeUnits);
        _ = SearchTextNormalizerV1.Normalize(value);
    }

    public static void ValidateOptionalSearchableText(
        string? value,
        string parameterName)
    {
        if (value is not null)
        {
            _ = NormalizeRequiredSearchableText(value, parameterName);
        }
    }

    public static string ValidateStableKey(
        string value,
        string parameterName,
        int maximumCodeUnits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumCodeUnits)
        {
            throw new ArgumentException(
                $"Stable key cannot exceed {maximumCodeUnits} characters.",
                parameterName);
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':')
            {
                throw new ArgumentException(
                    "Stable keys may contain only ASCII letters, digits, hyphens, underscores, periods, and colons.",
                    parameterName);
            }
        }

        return value;
    }

    private static void ValidateRequiredText(
        string value,
        string parameterName,
        int maximumCodeUnits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumCodeUnits)
        {
            throw new ArgumentException(
                $"Text cannot exceed {maximumCodeUnits} UTF-16 code units.",
                parameterName);
        }
    }

    private static int CountRunes(string value)
    {
        int count = 0;
        foreach (Rune _ in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }
}
