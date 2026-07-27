using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HorrorHarbor.Posters;

public static class ProceduralPosterRenderer
{
    public const int RenderVersion = 3;

    public static byte[] Render(PosterDescriptor descriptor, int width = 480, int height = 720)
    {
        Validate(descriptor, width, height);
        return GeneratedPosterRenderer.Render(descriptor, width, height);
    }

    public static string GetCacheKey(PosterDescriptor descriptor, int width = 480, int height = 720)
    {
        Validate(descriptor, width, height);
        string source = string.Create(
            CultureInfo.InvariantCulture,
            $"{RenderVersion}|{width}|{height}|{Canonicalize(descriptor)}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static void Validate(PosterDescriptor descriptor, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (width is < 240 or > 1200 || height is < 360 or > 1800)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Poster dimensions are outside the supported bounds.");
        }
    }

    private static string Canonicalize(PosterDescriptor descriptor) => string.Join(
        '|',
        descriptor.CandidateId.Trim(),
        descriptor.Title.Trim(),
        descriptor.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        descriptor.PrimaryLens?.Trim() ?? string.Empty,
        descriptor.SecondaryLens?.Trim() ?? string.Empty,
        descriptor.Franchise?.Trim() ?? string.Empty,
        descriptor.PosterTone.Trim(),
        descriptor.OriginalTitle?.Trim() ?? string.Empty,
        descriptor.OriginalLanguage?.Trim() ?? string.Empty,
        descriptor.TmdbId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        descriptor.ScareIntensity.ToString(CultureInfo.InvariantCulture),
        descriptor.GoreIntensity.ToString(CultureInfo.InvariantCulture),
        descriptor.DreadIntensity.ToString(CultureInfo.InvariantCulture),
        string.Join(',', descriptor.Tags.Select(static tag => tag.Trim()).Order(StringComparer.Ordinal)));
}
