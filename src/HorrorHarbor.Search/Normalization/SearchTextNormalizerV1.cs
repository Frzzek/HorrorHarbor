using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace HorrorHarbor.Search.Normalization;

[Flags]
public enum SearchNormalizationChanges
{
    None = 0,
    CompatibilityOrCanonicalForm = 1 << 0,
    LatinDiacriticRemoved = 1 << 1,
    ApostropheRemoved = 1 << 2,
    PunctuationOrSymbolNormalized = 1 << 3,
    InvariantLowercased = 1 << 4,
    WhitespaceNormalized = 1 << 5,
}

public sealed class NormalizedSearchText
{
    internal NormalizedSearchText(
        string value,
        ImmutableArray<string> tokens,
        SearchNormalizationChanges changes)
    {
        Value = value;
        Tokens = tokens;
        Changes = changes;
    }

    public string Value { get; }

    public ImmutableArray<string> Tokens { get; }

    public SearchNormalizationChanges Changes { get; }
}

public static class SearchTextNormalizerV1
{
    public const int Version = 1;

    public static NormalizedSearchText Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureWellFormedUtf16(value);

        string decomposed = value.Normalize(NormalizationForm.FormKD);
        SearchNormalizationChanges changes = string.Equals(value, decomposed, StringComparison.Ordinal)
            ? SearchNormalizationChanges.None
            : SearchNormalizationChanges.CompatibilityOrCanonicalForm;

        var builder = new StringBuilder(decomposed.Length);
        bool separatorPending = false;
        bool pendingWhitespace = false;
        Rune? previousBase = null;

        for (int index = 0; index < decomposed.Length;)
        {
            Rune rune = Rune.GetRuneAt(decomposed, index);
            index += rune.Utf16SequenceLength;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (IsCombiningMark(category))
            {
                if (previousBase is Rune baseRune)
                {
                    if (IsLatinLetter(baseRune))
                    {
                        changes |= SearchNormalizationChanges.LatinDiacriticRemoved;
                    }
                    else
                    {
                        AppendRune(builder, rune);
                    }
                }
                else
                {
                    changes |= SearchNormalizationChanges.PunctuationOrSymbolNormalized;
                }

                continue;
            }

            if (SearchCharacterPolicy.IsJoinControl(rune))
            {
                AppendRune(builder, rune);
                continue;
            }

            if (IsApostrophe(rune) &&
                previousBase is Rune prior &&
                Rune.IsLetterOrDigit(prior) &&
                NextBaseIsLetterOrDigit(decomposed, index))
            {
                changes |= SearchNormalizationChanges.ApostropheRemoved;
                continue;
            }

            if (Rune.IsLetterOrDigit(rune))
            {
                if (separatorPending && builder.Length > 0)
                {
                    builder.Append(' ');
                    separatorPending = false;
                    pendingWhitespace = false;
                }

                Rune lowered = Rune.ToLowerInvariant(rune);
                if (lowered != rune)
                {
                    changes |= SearchNormalizationChanges.InvariantLowercased;
                }

                AppendRune(builder, lowered);
                previousBase = rune;
                continue;
            }

            previousBase = null;
            if (Rune.IsWhiteSpace(rune))
            {
                if (builder.Length == 0 || separatorPending || rune.Value != ' ')
                {
                    changes |= SearchNormalizationChanges.WhitespaceNormalized;
                }

                if (builder.Length > 0)
                {
                    separatorPending = true;
                    pendingWhitespace = true;
                }

                continue;
            }

            changes |= SearchNormalizationChanges.PunctuationOrSymbolNormalized;
            if (separatorPending && pendingWhitespace)
            {
                changes |= SearchNormalizationChanges.WhitespaceNormalized;
            }

            if (builder.Length > 0)
            {
                separatorPending = true;
            }
        }

        if (separatorPending && pendingWhitespace)
        {
            changes |= SearchNormalizationChanges.WhitespaceNormalized;
        }

        string normalized = builder.ToString();
        return new NormalizedSearchText(normalized, Tokenize(normalized), changes);
    }

    private static bool NextBaseIsLetterOrDigit(string value, int index)
    {
        while (index < value.Length)
        {
            Rune candidate = Rune.GetRuneAt(value, index);
            index += candidate.Utf16SequenceLength;
            UnicodeCategory category = Rune.GetUnicodeCategory(candidate);
            if (IsCombiningMark(category))
            {
                continue;
            }

            return Rune.IsLetterOrDigit(candidate);
        }

        return false;
    }

    private static ImmutableArray<string> Tokenize(string value)
    {
        if (value.Length == 0)
        {
            return [];
        }

        int tokenCount = 1;
        foreach (char character in value)
        {
            if (character == ' ')
            {
                tokenCount++;
            }
        }

        var tokens = ImmutableArray.CreateBuilder<string>(tokenCount);
        int tokenStart = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] != ' ')
            {
                continue;
            }

            tokens.Add(value.Substring(tokenStart, index - tokenStart));
            tokenStart = index + 1;
        }

        tokens.Add(value[tokenStart..]);
        return tokens.MoveToImmutable();
    }

    private static void AppendRune(StringBuilder builder, Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        int written = rune.EncodeToUtf16(buffer);
        builder.Append(buffer[..written]);
    }

    private static bool IsCombiningMark(UnicodeCategory category) => category is
        UnicodeCategory.NonSpacingMark or
        UnicodeCategory.SpacingCombiningMark or
        UnicodeCategory.EnclosingMark;

    private static bool IsApostrophe(Rune rune) => rune.Value is
        0x0027 or
        0x02BC or
        0x2018 or
        0x2019 or
        0xFF07;

    private static bool IsLatinLetter(Rune rune)
    {
        if (!Rune.IsLetter(rune))
        {
            return false;
        }

        int value = rune.Value;
        return value is >= 0x0041 and <= 0x024F or
            >= 0x1D00 and <= 0x1D7F or
            >= 0x1D80 and <= 0x1DBF or
            >= 0x1E00 and <= 0x1EFF or
            >= 0x2C60 and <= 0x2C7F or
            >= 0xA720 and <= 0xA7FF or
            >= 0xAB30 and <= 0xAB6F or
            >= 0x10780 and <= 0x107BF or
            >= 0x1DF00 and <= 0x1DFFF;
    }

    private static void EnsureWellFormedUtf16(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            Rune rune;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    throw new ArgumentException("Value must contain well-formed UTF-16.", nameof(value));
                }

                rune = new Rune(character, value[index + 1]);
                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                throw new ArgumentException("Value must contain well-formed UTF-16.", nameof(value));
            }
            else
            {
                rune = new Rune(character);
            }

            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                throw new ArgumentException(
                    "Value must not contain control characters.",
                    nameof(value));
            }

            if (SearchCharacterPolicy.IsUnsafeFormatCharacter(rune))
            {
                throw new ArgumentException(
                    "Value must not contain Unicode format characters.",
                    nameof(value));
            }
        }
    }
}
