using System.Globalization;
using System.Text;

namespace HorrorHarbor.Posters;

internal static class PosterRenderIdentity
{
    public static string Create(
        PosterDescriptor descriptor,
        PosterGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(options);

        var builder = new StringBuilder(512);
        Append(builder, descriptor.CandidateId?.Trim());
        Append(builder, descriptor.Title?.Trim());
        Append(builder, descriptor.ReleaseYear?.ToString(CultureInfo.InvariantCulture));
        Append(builder, descriptor.PrimaryLens?.Trim());
        Append(builder, descriptor.SecondaryLens?.Trim());
        Append(builder, descriptor.Franchise?.Trim());
        Append(builder, descriptor.PosterTone?.Trim());
        Append(builder, descriptor.OriginalTitle?.Trim());
        Append(builder, descriptor.OriginalLanguage?.Trim());
        Append(builder, descriptor.TmdbId?.ToString(CultureInfo.InvariantCulture));
        Append(builder, descriptor.ScareIntensity.ToString(CultureInfo.InvariantCulture));
        Append(builder, descriptor.GoreIntensity.ToString(CultureInfo.InvariantCulture));
        Append(builder, descriptor.DreadIntensity.ToString(CultureInfo.InvariantCulture));

        string[] tags = (descriptor.Tags ?? Array.Empty<string>())
            .Select(static tag => tag?.Trim() ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Append(builder, tags.Length.ToString(CultureInfo.InvariantCulture));
        foreach (string tag in tags)
        {
            Append(builder, tag);
        }

        Append(builder, options.ToCanonicalString());
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string? value)
    {
        string normalized = value ?? string.Empty;
        builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(normalized);
        builder.Append(';');
    }
}
