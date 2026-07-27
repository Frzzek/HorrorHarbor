using System.Globalization;
using SkiaSharp;

namespace HorrorHarbor.Posters;

internal sealed record GeneratedPosterPalette(
    SKColor Ink,
    SKColor Shadow,
    SKColor Midtone,
    SKColor Accent,
    SKColor Highlight,
    SKColor Paper,
    SKColor Title);

internal static class GeneratedPosterPaletteFactory
{
    public static GeneratedPosterPalette Create(
        PosterDescriptor descriptor,
        GeneratedPosterArtDirection direction)
    {
        SKColor catalogTone = ParseTone(descriptor.PosterTone);
        (float hue, float saturation, float lightness) = ToHsl(catalogTone);
        float familyShift = direction.Family switch
        {
            PosterCompositionFamily.Slasher => -8,
            PosterCompositionFamily.Occult => 24,
            PosterCompositionFamily.Psychological => 178,
            PosterCompositionFamily.BodyHorror => -18,
            PosterCompositionFamily.Cosmic => 92,
            PosterCompositionFamily.Creature => 58,
            PosterCompositionFamily.Isolation => 155,
            PosterCompositionFamily.FoundFootage => 118,
            PosterCompositionFamily.Gothic => 10,
            PosterCompositionFamily.HomeInvasion => -4,
            PosterCompositionFamily.HorrorComedy => 205,
            _ => 32,
        };

        float shiftedHue = NormalizeHue(hue + familyShift);
        float accentSaturation = Math.Clamp(saturation + 0.18f, 0.48f, 0.92f);
        SKColor accent = FromHsl(shiftedHue, accentSaturation, Math.Clamp(lightness, 0.34f, 0.58f));
        SKColor secondary = FromHsl(
            NormalizeHue(shiftedHue + (direction.Family is PosterCompositionFamily.HorrorComedy ? 122 : 28)),
            Math.Clamp(accentSaturation - 0.12f, 0.35f, 0.78f),
            0.32f);

        SKColor paper = direction.EraStyle switch
        {
            PosterEraStyle.Grindhouse => new SKColor(235, 214, 170),
            PosterEraStyle.Analog => new SKColor(215, 221, 203),
            PosterEraStyle.Millennium => new SKColor(206, 218, 226),
            _ => new SKColor(229, 226, 218),
        };

        return new GeneratedPosterPalette(
            new SKColor(5, 7, 11),
            Mix(new SKColor(6, 8, 14), accent, 0.12f),
            secondary,
            accent,
            Mix(accent, paper, 0.58f),
            paper,
            new SKColor(246, 241, 226));
    }

    private static SKColor ParseTone(string tone)
    {
        if (tone.Length == 7 && tone[0] == '#' &&
            byte.TryParse(tone.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) &&
            byte.TryParse(tone.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) &&
            byte.TryParse(tone.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
        {
            return new SKColor(red, green, blue);
        }

        return new SKColor(126, 42, 58);
    }

    private static SKColor Mix(SKColor first, SKColor second, float amount)
    {
        float clamped = Math.Clamp(amount, 0, 1);
        return new SKColor(
            MixChannel(first.Red, second.Red, clamped),
            MixChannel(first.Green, second.Green, clamped),
            MixChannel(first.Blue, second.Blue, clamped));
    }

    private static byte MixChannel(byte first, byte second, float amount) =>
        (byte)Math.Clamp((int)MathF.Round(first + (second - first) * amount), 0, 255);

    private static (float Hue, float Saturation, float Lightness) ToHsl(SKColor color)
    {
        float red = color.Red / 255f;
        float green = color.Green / 255f;
        float blue = color.Blue / 255f;
        float maximum = Math.Max(red, Math.Max(green, blue));
        float minimum = Math.Min(red, Math.Min(green, blue));
        float lightness = (maximum + minimum) / 2;
        if (Math.Abs(maximum - minimum) < 0.0001f)
        {
            return (0, 0, lightness);
        }

        float difference = maximum - minimum;
        float saturation = lightness > 0.5f
            ? difference / (2 - maximum - minimum)
            : difference / (maximum + minimum);
        float hue = maximum == red
            ? (green - blue) / difference + (green < blue ? 6 : 0)
            : maximum == green
                ? (blue - red) / difference + 2
                : (red - green) / difference + 4;
        return (hue * 60, saturation, lightness);
    }

    private static SKColor FromHsl(float hue, float saturation, float lightness)
    {
        float chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        float segment = NormalizeHue(hue) / 60;
        float secondary = chroma * (1 - Math.Abs(segment % 2 - 1));
        (float red, float green, float blue) = segment switch
        {
            < 1 => (chroma, secondary, 0f),
            < 2 => (secondary, chroma, 0f),
            < 3 => (0f, chroma, secondary),
            < 4 => (0f, secondary, chroma),
            < 5 => (secondary, 0f, chroma),
            _ => (chroma, 0f, secondary),
        };
        float match = lightness - chroma / 2;
        return new SKColor(ToChannel(red + match), ToChannel(green + match), ToChannel(blue + match));
    }

    private static byte ToChannel(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);

    private static float NormalizeHue(float hue)
    {
        float normalized = hue % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }
}
