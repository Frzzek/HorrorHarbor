using System.Security.Cryptography;
using System.Text;

namespace HorrorHarbor.Posters;

public static class ProceduralPosterRenderer
{
    public const int RenderVersion = 4;

    public static byte[] Render(PosterDescriptor descriptor, int width = 480, int height = 720)
        => Render(descriptor, PosterGenerationOptions.Automatic, width, height);

    public static byte[] Render(
        PosterDescriptor descriptor,
        PosterGenerationOptions options,
        int width = 480,
        int height = 720)
    {
        PosterGenerationOptions normalized = Validate(descriptor, options, width, height);
        return GeneratedPosterRenderer.Render(descriptor, normalized, width, height);
    }

    public static string GetCacheKey(PosterDescriptor descriptor, int width = 480, int height = 720)
        => GetCacheKey(descriptor, PosterGenerationOptions.Automatic, width, height);

    public static string GetCacheKey(
        PosterDescriptor descriptor,
        PosterGenerationOptions options,
        int width = 480,
        int height = 720)
    {
        PosterGenerationOptions normalized = Validate(descriptor, options, width, height);
        string source = $"{RenderVersion}|{width}|{height}|{PosterRenderIdentity.Create(descriptor, normalized)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static PosterGenerationOptions Validate(
        PosterDescriptor descriptor,
        PosterGenerationOptions options,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);
        if (width is < 240 or > 1200 || height is < 360 or > 1800)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Poster dimensions are outside the supported bounds.");
        }

        return options.Normalize();
    }
}
