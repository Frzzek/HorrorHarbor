using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HorrorHarbor.Posters;

public enum PosterCompositionFamily
{
    Slasher,
    Occult,
    Psychological,
    BodyHorror,
    Cosmic,
    Creature,
    Isolation,
    FoundFootage,
    Gothic,
    HomeInvasion,
    HorrorComedy,
    General,
}

public enum PosterEraStyle
{
    Grindhouse,
    Analog,
    Millennium,
    Contemporary,
}

public enum PosterTitleStyle
{
    Condensed,
    Monumental,
    Editorial,
    Distressed,
    Technical,
}

public sealed record GeneratedPosterArtDirection(
    PosterCompositionFamily Family,
    PosterEraStyle EraStyle,
    PosterTitleStyle TitleStyle,
    string Motif,
    string Eyebrow,
    int Variation,
    double TextureStrength);

public static class GeneratedPosterArtDirector
{
    public static GeneratedPosterArtDirection Analyze(PosterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        string metadata = BuildMetadata(descriptor);
        PosterCompositionFamily family = SelectFamily(descriptor, metadata);
        PosterEraStyle eraStyle = SelectEra(descriptor.ReleaseYear);
        PosterTitleStyle titleStyle = SelectTitleStyle(family, eraStyle);
        byte[] identity = SHA256.HashData(Encoding.UTF8.GetBytes(BuildIdentity(descriptor)));
        int variation = BinaryPrimitives.ReadUInt16LittleEndian(identity) % 12;
        int maximumIntensity = Math.Max(
            Math.Clamp(descriptor.ScareIntensity, 0, 5),
            Math.Max(
                Math.Clamp(descriptor.GoreIntensity, 0, 5),
                Math.Clamp(descriptor.DreadIntensity, 0, 5)));

        return new GeneratedPosterArtDirection(
            family,
            eraStyle,
            titleStyle,
            SelectMotif(family, metadata, variation),
            BuildEyebrow(descriptor, family),
            variation,
            0.18 + maximumIntensity * 0.045);
    }

    private static PosterCompositionFamily SelectFamily(PosterDescriptor descriptor, string metadata)
    {
        if (ContainsAny(metadata, "found footage", "screenlife", "surveillance", "webcam"))
        {
            return PosterCompositionFamily.FoundFootage;
        }

        string lens = descriptor.PrimaryLens?.ToLowerInvariant() ?? string.Empty;
        if (lens.Contains("slasher", StringComparison.Ordinal))
        {
            return PosterCompositionFamily.Slasher;
        }

        if (ContainsAny(lens, "supernatural", "occult"))
        {
            return PosterCompositionFamily.Occult;
        }

        if (lens.Contains("psychological", StringComparison.Ordinal))
        {
            return PosterCompositionFamily.Psychological;
        }

        if (lens.Contains("body horror", StringComparison.Ordinal))
        {
            return PosterCompositionFamily.BodyHorror;
        }

        if (lens.Contains("horror comedy", StringComparison.Ordinal))
        {
            return PosterCompositionFamily.HorrorComedy;
        }

        if (ContainsAny(lens, "sci-fi", "cosmic"))
        {
            return PosterCompositionFamily.Cosmic;
        }

        if (ContainsAny(lens, "creature", "animal"))
        {
            return PosterCompositionFamily.Creature;
        }

        if (ContainsAny(lens, "survival", "isolation"))
        {
            return PosterCompositionFamily.Isolation;
        }

        if (ContainsAny(lens, "folk", "gothic"))
        {
            return PosterCompositionFamily.Gothic;
        }

        if (ContainsAny(lens, "home invasion", "stalker"))
        {
            return PosterCompositionFamily.HomeInvasion;
        }

        if (lens.Contains("general horror", StringComparison.Ordinal))
        {
            return SelectGeneralFamily(metadata);
        }

        return SelectGeneralFamily(metadata);
    }

    private static PosterCompositionFamily SelectGeneralFamily(string metadata)
    {
        if (ContainsAny(metadata, "horror comedy", "dark comedy", "satire", "campy"))
        {
            return PosterCompositionFamily.HorrorComedy;
        }

        if (ContainsAny(metadata, "body horror", "transformation", "mutation", "infection"))
        {
            return PosterCompositionFamily.BodyHorror;
        }

        if (ContainsAny(metadata, "sci-fi", "cosmic", "alien", "space horror"))
        {
            return PosterCompositionFamily.Cosmic;
        }

        if (ContainsAny(metadata, "creature", "animal", "monster", "shark"))
        {
            return PosterCompositionFamily.Creature;
        }

        if (ContainsAny(metadata, "folk", "gothic", "period horror", "witch"))
        {
            return PosterCompositionFamily.Gothic;
        }

        if (ContainsAny(metadata, "home invasion", "stalker"))
        {
            return PosterCompositionFamily.HomeInvasion;
        }

        if (ContainsAny(metadata, "slasher", "serial killer", "killer", "revenge"))
        {
            return PosterCompositionFamily.Slasher;
        }

        if (ContainsAny(metadata, "supernatural", "occult", "possession", "ritual", "haunted"))
        {
            return PosterCompositionFamily.Occult;
        }

        if (ContainsAny(metadata, "psychological", "mind-bending", "disturbing", "paranoia"))
        {
            return PosterCompositionFamily.Psychological;
        }

        if (ContainsAny(metadata, "survival", "isolation", "snow", "cabin", "wilderness"))
        {
            return PosterCompositionFamily.Isolation;
        }

        return PosterCompositionFamily.General;
    }

    private static PosterEraStyle SelectEra(int? releaseYear) => releaseYear switch
    {
        <= 1984 => PosterEraStyle.Grindhouse,
        <= 1999 => PosterEraStyle.Analog,
        <= 2012 => PosterEraStyle.Millennium,
        _ => PosterEraStyle.Contemporary,
    };

    private static PosterTitleStyle SelectTitleStyle(
        PosterCompositionFamily family,
        PosterEraStyle eraStyle) => family switch
        {
            PosterCompositionFamily.FoundFootage => PosterTitleStyle.Technical,
            PosterCompositionFamily.Cosmic => PosterTitleStyle.Monumental,
            PosterCompositionFamily.Psychological => PosterTitleStyle.Editorial,
            PosterCompositionFamily.Gothic => PosterTitleStyle.Editorial,
            PosterCompositionFamily.HorrorComedy => PosterTitleStyle.Condensed,
            _ when eraStyle is PosterEraStyle.Grindhouse => PosterTitleStyle.Distressed,
            _ => PosterTitleStyle.Condensed,
        };

    private static string SelectMotif(
        PosterCompositionFamily family,
        string metadata,
        int variation) => family switch
        {
            PosterCompositionFamily.Slasher when metadata.Contains("trap", StringComparison.Ordinal) => "trap",
            PosterCompositionFamily.Slasher => variation % 2 == 0 ? "blade" : "mask",
            PosterCompositionFamily.Occult when metadata.Contains("haunted house", StringComparison.Ordinal) => "threshold",
            PosterCompositionFamily.Occult => variation % 2 == 0 ? "sigil" : "apparition",
            PosterCompositionFamily.Psychological => variation % 2 == 0 ? "fracture" : "double",
            PosterCompositionFamily.BodyHorror => variation % 2 == 0 ? "cellular" : "metamorphosis",
            PosterCompositionFamily.Cosmic => variation % 2 == 0 ? "eclipse" : "monolith",
            PosterCompositionFamily.Creature => variation % 2 == 0 ? "maw" : "claw",
            PosterCompositionFamily.Isolation when metadata.Contains("snow", StringComparison.Ordinal) => "whiteout",
            PosterCompositionFamily.Isolation => variation % 2 == 0 ? "cabin" : "horizon",
            PosterCompositionFamily.FoundFootage when metadata.Contains("screenlife", StringComparison.Ordinal) => "interface",
            PosterCompositionFamily.FoundFootage => "viewfinder",
            PosterCompositionFamily.Gothic => variation % 2 == 0 ? "spire" : "woodcut",
            PosterCompositionFamily.HomeInvasion => variation % 2 == 0 ? "doorway" : "window",
            PosterCompositionFamily.HorrorComedy => variation % 2 == 0 ? "neon-skull" : "pop-shadow",
            _ => variation % 2 == 0 ? "labyrinth" : "void",
        };

    private static string BuildEyebrow(
        PosterDescriptor descriptor,
        PosterCompositionFamily family)
    {
        string category = string.IsNullOrWhiteSpace(descriptor.PrimaryLens)
            ? family.ToString()
            : descriptor.PrimaryLens;
        string year = descriptor.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? "NOCTURNE SELECT";
        return string.Create(CultureInfo.InvariantCulture, $"{category.ToUpperInvariant()}  •  {year}");
    }

    private static string BuildMetadata(PosterDescriptor descriptor) => string.Join(
        ' ',
        new[]
        {
            descriptor.PrimaryLens,
            descriptor.SecondaryLens,
            descriptor.Title,
            descriptor.OriginalTitle,
            string.Join(' ', descriptor.Tags ?? []),
        }.Where(static value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

    private static string BuildIdentity(PosterDescriptor descriptor) => string.Join(
        '|',
        descriptor.CandidateId,
        descriptor.Title,
        descriptor.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        descriptor.Franchise ?? string.Empty);

    private static bool ContainsAny(string source, params string[] values) =>
        values.Any(value => source.Contains(value, StringComparison.Ordinal));
}
