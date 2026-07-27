using System.Globalization;
using System.Text;

namespace HorrorHarbor.Core.ValueObjects;

public static class TitleNormalizer
{
    public static string NormalizeForKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
