using System.Globalization;

namespace HorrorHarbor.Posters;

public enum PosterTextureStyle
{
    Automatic,
    Clean,
    Atmospheric,
    Weathered,
}

public sealed record PosterGenerationOptions(
    PosterCompositionFamily? CompositionFamily = null,
    PosterEraStyle? EraStyle = null,
    PosterTitleStyle? TitleStyle = null,
    PosterTextureStyle TextureStyle = PosterTextureStyle.Automatic,
    int VariationOffset = 0,
    string? AccentTone = null)
{
    private const string FormatVersion = "1";

    public static PosterGenerationOptions Automatic { get; } = new();

    public bool IsAutomatic =>
        CompositionFamily is null &&
        EraStyle is null &&
        TitleStyle is null &&
        TextureStyle == PosterTextureStyle.Automatic &&
        VariationOffset == 0 &&
        AccentTone is null;

    public PosterGenerationOptions Normalize()
    {
        if (CompositionFamily is { } family && !Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(CompositionFamily));
        }

        if (EraStyle is { } era && !Enum.IsDefined(era))
        {
            throw new ArgumentOutOfRangeException(nameof(EraStyle));
        }

        if (TitleStyle is { } title && !Enum.IsDefined(title))
        {
            throw new ArgumentOutOfRangeException(nameof(TitleStyle));
        }

        if (!Enum.IsDefined(TextureStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(TextureStyle));
        }

        if (VariationOffset is < 0 or > 11)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VariationOffset),
                "Poster variation must be between 0 and 11.");
        }

        string? accent = string.IsNullOrWhiteSpace(AccentTone)
            ? null
            : AccentTone.Trim().ToUpperInvariant();
        if (accent is not null && !IsHexTone(accent))
        {
            throw new ArgumentException(
                "Poster accent tone must use #RRGGBB notation.",
                nameof(AccentTone));
        }

        return this with { AccentTone = accent };
    }

    public string ToCanonicalString()
    {
        PosterGenerationOptions normalized = Normalize();
        return string.Join(
            '|',
            FormatVersion,
            normalized.CompositionFamily?.ToString() ?? "*",
            normalized.EraStyle?.ToString() ?? "*",
            normalized.TitleStyle?.ToString() ?? "*",
            normalized.TextureStyle,
            normalized.VariationOffset.ToString(CultureInfo.InvariantCulture),
            normalized.AccentTone ?? "*");
    }

    public static bool TryParse(string? value, out PosterGenerationOptions options)
    {
        options = Automatic;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] fields = value.Split('|', StringSplitOptions.None);
        if (fields.Length != 7 || fields[0] != FormatVersion ||
            !TryParseOptional(fields[1], out PosterCompositionFamily? family) ||
            !TryParseOptional(fields[2], out PosterEraStyle? era) ||
            !TryParseOptional(fields[3], out PosterTitleStyle? title) ||
            !Enum.TryParse(fields[4], ignoreCase: false, out PosterTextureStyle texture) ||
            !Enum.IsDefined(texture) ||
            !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out int variation))
        {
            return false;
        }

        try
        {
            options = new PosterGenerationOptions(
                family,
                era,
                title,
                texture,
                variation,
                fields[6] == "*" ? null : fields[6]).Normalize();
            return true;
        }
        catch (ArgumentException)
        {
            options = Automatic;
            return false;
        }
    }

    private static bool TryParseOptional<T>(string value, out T? result)
        where T : struct, Enum
    {
        if (value == "*")
        {
            result = null;
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    private static bool IsHexTone(string value) =>
        value.Length == 7 &&
        value[0] == '#' &&
        byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) &&
        byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) &&
        byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
}
