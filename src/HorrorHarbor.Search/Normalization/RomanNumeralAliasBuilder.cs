using System.Collections.Immutable;

namespace HorrorHarbor.Search.Normalization;

internal static class RomanNumeralAliasBuilder
{
    private static readonly string[] RomanNumerals =
    [
        string.Empty,
        "i",
        "ii",
        "iii",
        "iv",
        "v",
        "vi",
        "vii",
        "viii",
        "ix",
        "x",
        "xi",
        "xii",
        "xiii",
        "xiv",
        "xv",
        "xvi",
        "xvii",
        "xviii",
        "xix",
        "xx",
    ];

    private static readonly string[] ArabicNumerals =
    [
        string.Empty,
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
        "7",
        "8",
        "9",
        "10",
        "11",
        "12",
        "13",
        "14",
        "15",
        "16",
        "17",
        "18",
        "19",
        "20",
    ];

    public static string? Build(ImmutableArray<string> normalizedTokens)
    {
        if (normalizedTokens.IsDefaultOrEmpty)
        {
            return null;
        }

        string[] variantTokens = normalizedTokens.ToArray();
        bool changed = false;
        for (int index = 0; index < variantTokens.Length; index++)
        {
            if (TryConvert(variantTokens[index], out string? converted))
            {
                variantTokens[index] = converted;
                changed = true;
            }
        }

        return changed ? string.Join(' ', variantTokens) : null;
    }

    private static bool TryConvert(string token, out string converted)
    {
        for (int value = 1; value < RomanNumerals.Length; value++)
        {
            if (string.Equals(token, RomanNumerals[value], StringComparison.Ordinal))
            {
                converted = ArabicNumerals[value];
                return true;
            }

            if (string.Equals(token, ArabicNumerals[value], StringComparison.Ordinal))
            {
                converted = RomanNumerals[value];
                return true;
            }
        }

        converted = string.Empty;
        return false;
    }
}
