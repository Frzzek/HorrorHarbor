using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace HorrorHarbor.Posters;

internal static class GeneratedPosterRenderer
{
    public static byte[] Render(
        PosterDescriptor descriptor,
        PosterGenerationOptions options,
        int width,
        int height)
    {
        PosterGenerationOptions normalized = options.Normalize();
        GeneratedPosterArtDirection direction = GeneratedPosterArtDirector.Analyze(descriptor, normalized);
        GeneratedPosterPalette palette = GeneratedPosterPaletteFactory.Create(descriptor, direction, normalized);
        byte[] seed = SHA256.HashData(Encoding.UTF8.GetBytes(PosterRenderIdentity.Create(descriptor, normalized)));
        var random = new PosterRandom(BinaryPrimitives.ReadUInt64LittleEndian(seed));
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info) ??
            throw new InvalidOperationException("The poster rendering surface could not be created.");
        SKCanvas canvas = surface.Canvas;

        DrawBackground(canvas, width, height, palette, direction);
        DrawComposition(canvas, width, height, palette, direction, ref random);
        DrawTexture(canvas, width, height, palette, direction, ref random);
        DrawTypography(canvas, width, height, descriptor, palette, direction);
        DrawFrame(canvas, width, height, palette, direction);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100) ??
            throw new InvalidOperationException("The rendered poster could not be encoded as PNG.");
        return encoded.ToArray();
    }

    private static void DrawBackground(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        using var paint = Paint();
        SKColor upper = direction.EraStyle is PosterEraStyle.Grindhouse
            ? palette.Midtone
            : palette.Shadow;
        bool reverse = direction.Variation % 2 == 1;
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(reverse ? width : 0, 0),
            new SKPoint(reverse ? 0 : width, height),
            [upper, palette.Ink, palette.Shadow],
            [0, 0.58f, 1],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, width, height, paint);

        float glowX = width * (0.26f + direction.Variation % 5 * 0.12f);
        float glowY = height * (0.24f + direction.Variation / 5 * 0.07f);
        float glowRadius = width * (0.56f + direction.Variation % 3 * 0.08f);
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(glowX, glowY),
            glowRadius,
            [WithAlpha(palette.Accent, 120), SKColors.Transparent],
            [0, 1],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, width, height * 0.82f, paint);
    }

    private static void DrawComposition(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
        canvas.Save();
        try
        {
            if (direction.Variation % 2 == 1)
            {
                canvas.Scale(-1, 1, width / 2f, height * 0.34f);
            }

            float tilt = ((direction.Variation / 2) % 3 - 1) * 1.6f;
            canvas.RotateDegrees(tilt, width / 2f, height * 0.34f);
            switch (direction.Family)
            {
                case PosterCompositionFamily.Slasher:
                    DrawSlasher(canvas, width, height, palette, direction);
                    break;
                case PosterCompositionFamily.Occult:
                    DrawOccult(canvas, width, height, palette, direction);
                    break;
                case PosterCompositionFamily.Psychological:
                    DrawPsychological(canvas, width, height, palette, direction);
                    break;
                case PosterCompositionFamily.BodyHorror:
                    DrawBodyHorror(canvas, width, height, palette, direction, ref random);
                    break;
                case PosterCompositionFamily.Cosmic:
                    DrawCosmic(canvas, width, height, palette, direction, ref random);
                    break;
                case PosterCompositionFamily.Creature:
                    DrawCreature(canvas, width, height, palette, direction);
                    break;
                case PosterCompositionFamily.Isolation:
                    DrawIsolation(canvas, width, height, palette, direction, ref random);
                    break;
                case PosterCompositionFamily.FoundFootage:
                    DrawFoundFootage(canvas, width, height, palette, direction, ref random);
                    break;
                case PosterCompositionFamily.Gothic:
                    DrawGothic(canvas, width, height, palette, direction);
                    break;
                case PosterCompositionFamily.HomeInvasion:
                    DrawHomeInvasion(canvas, width, height, palette, direction);
                    break;
                case PosterCompositionFamily.HorrorComedy:
                    DrawHorrorComedy(canvas, width, height, palette, direction);
                    break;
                default:
                    DrawGeneral(canvas, width, height, palette, direction);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void DrawSlasher(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "trap")
        {
            DrawSlasherTrap(canvas, width, height, palette, direction);
            return;
        }

        bool reverse = direction.Variation % 2 == 0;
        float startX = reverse ? width * 0.10f : width * 0.90f;
        float endX = reverse ? width * 0.86f : width * 0.14f;
        using SKPath blade = BuildPath(builder =>
        {
            builder.MoveTo(startX, height * 0.12f);
            builder.LineTo(endX, height * 0.58f);
            builder.LineTo(endX + (reverse ? -width : width) * 0.08f, height * 0.61f);
            builder.LineTo(startX + (reverse ? width : -width) * 0.04f, height * 0.18f);
            builder.Close();
        });
        using var paint = Paint(palette.Highlight);
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [palette.Title, palette.Accent, palette.Ink],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawPath(blade, paint);

        if (direction.Motif is "mask")
        {
            paint.Shader = null;
            paint.Color = WithAlpha(palette.Paper, 205);
            canvas.DrawOval(new SKRect(width * 0.31f, height * 0.17f, width * 0.69f, height * 0.54f), paint);
            paint.Color = palette.Ink;
            canvas.DrawOval(new SKRect(width * 0.37f, height * 0.29f, width * 0.47f, height * 0.35f), paint);
            canvas.DrawOval(new SKRect(width * 0.53f, height * 0.29f, width * 0.63f, height * 0.35f), paint);
            canvas.DrawRect(width * 0.46f, height * 0.43f, width * 0.08f, height * 0.035f, paint);
        }
    }

    private static void DrawSlasherTrap(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float centerX = width * (0.44f + direction.Variation % 4 * 0.04f);
        float centerY = height * 0.32f;
        int pointCount = 24;
        var jawPoints = new SKPoint[pointCount];
        for (int index = 0; index < pointCount; index++)
        {
            float angle = index * MathF.PI * 2 / pointCount;
            float radius = width * (index % 2 == 0 ? 0.34f : 0.23f);
            jawPoints[index] = new SKPoint(
                centerX + MathF.Cos(angle) * radius,
                centerY + MathF.Sin(angle) * radius * 0.72f);
        }

        using SKPath jaws = BuildPolygon(jawPoints);
        using var metal = Stroke(WithAlpha(palette.Highlight, 225), width * 0.017f);
        canvas.DrawPath(jaws, metal);
        using var voidPaint = Paint(palette.Ink);
        canvas.DrawCircle(centerX, centerY, width * 0.16f, voidPaint);
        using var trigger = Stroke(WithAlpha(palette.Accent, 235), width * 0.011f);
        canvas.DrawLine(centerX - width * 0.20f, centerY, centerX + width * 0.20f, centerY, trigger);
        canvas.DrawLine(centerX, centerY - height * 0.13f, centerX, centerY + height * 0.13f, trigger);
        canvas.DrawLine(centerX, centerY - height * 0.28f, centerX, centerY - height * 0.18f, metal);
    }

    private static void DrawOccult(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "threshold")
        {
            DrawOccultThreshold(canvas, width, height, palette, direction);
            return;
        }

        if (direction.Motif is "apparition")
        {
            DrawOccultApparition(canvas, width, height, palette, direction);
            return;
        }

        float centerX = width * (0.44f + direction.Variation % 3 * 0.03f);
        float centerY = height * 0.34f;
        float radius = width * 0.31f;
        using var stroke = Stroke(palette.Highlight, width * 0.008f);
        stroke.Color = WithAlpha(palette.Highlight, 190);
        canvas.DrawCircle(centerX, centerY, radius, stroke);
        canvas.DrawCircle(centerX, centerY, radius * 0.72f, stroke);
        var points = new SKPoint[5];
        for (int index = 0; index < 5; index++)
        {
            double angle = -Math.PI / 2 + index * Math.PI * 4 / 5;
            points[index] = new SKPoint(
                centerX + MathF.Cos((float)angle) * radius * 0.72f,
                centerY + MathF.Sin((float)angle) * radius * 0.72f);
        }

        using SKPath sigil = BuildPolygon(points);
        canvas.DrawPath(sigil, stroke);
        using var glow = Paint(WithAlpha(palette.Accent, 45));
        for (int ring = 1; ring <= 5; ring++)
        {
            canvas.DrawCircle(centerX, centerY, radius + ring * width * 0.018f, glow);
        }
    }

    private static void DrawOccultApparition(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float centerX = width * (0.42f + direction.Variation % 4 * 0.05f);
        using SKPath apparition = BuildPath(builder =>
        {
            builder.MoveTo(centerX, height * 0.08f);
            builder.CubicTo(
                centerX + width * 0.25f,
                height * 0.12f,
                centerX + width * 0.30f,
                height * 0.43f,
                centerX + width * 0.18f,
                height * 0.62f);
            builder.LineTo(centerX + width * 0.03f, height * 0.52f);
            builder.LineTo(centerX - width * 0.08f, height * 0.64f);
            builder.LineTo(centerX - width * 0.17f, height * 0.51f);
            builder.LineTo(centerX - width * 0.28f, height * 0.61f);
            builder.CubicTo(
                centerX - width * 0.31f,
                height * 0.38f,
                centerX - width * 0.23f,
                height * 0.12f,
                centerX,
                height * 0.08f);
            builder.Close();
        });
        using var ghost = Paint();
        ghost.Shader = SKShader.CreateLinearGradient(
            new SKPoint(centerX, height * 0.08f),
            new SKPoint(centerX, height * 0.64f),
            [WithAlpha(palette.Highlight, 210), WithAlpha(palette.Accent, 95), SKColors.Transparent],
            [0, 0.58f, 1],
            SKShaderTileMode.Clamp);
        canvas.DrawPath(apparition, ghost);
        using var eyes = Paint(palette.Ink);
        canvas.DrawOval(new SKRect(centerX - width * 0.09f, height * 0.24f, centerX - width * 0.02f, height * 0.28f), eyes);
        canvas.DrawOval(new SKRect(centerX + width * 0.02f, height * 0.24f, centerX + width * 0.09f, height * 0.28f), eyes);
    }

    private static void DrawOccultThreshold(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        using var house = Paint(WithAlpha(palette.Ink, 245));
        canvas.DrawRect(width * 0.22f, height * 0.25f, width * 0.56f, height * 0.36f, house);
        using SKPath roof = BuildPolygon(
        [
            new SKPoint(width * 0.15f, height * 0.28f),
            new SKPoint(width * 0.50f, height * 0.08f),
            new SKPoint(width * 0.85f, height * 0.28f),
        ]);
        canvas.DrawPath(roof, house);
        float doorX = width * (direction.Variation % 2 == 0 ? 0.41f : 0.50f);
        using var doorway = Paint(palette.Accent);
        doorway.Shader = SKShader.CreateLinearGradient(
            new SKPoint(doorX, height * 0.34f),
            new SKPoint(doorX + width * 0.17f, height * 0.59f),
            [palette.Highlight, palette.Accent, palette.Ink],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawRect(doorX, height * 0.34f, width * 0.17f, height * 0.27f, doorway);
        using var window = Paint(WithAlpha(palette.Highlight, 165));
        canvas.DrawRect(width * 0.29f, height * 0.34f, width * 0.08f, height * 0.07f, window);
        canvas.DrawRect(width * 0.63f, height * 0.34f, width * 0.08f, height * 0.07f, window);
        using var rays = Stroke(WithAlpha(palette.Highlight, 80), width * 0.006f);
        canvas.DrawLine(doorX, height * 0.61f, width * 0.08f, height * 0.66f, rays);
        canvas.DrawLine(doorX + width * 0.17f, height * 0.61f, width * 0.92f, height * 0.66f, rays);
    }

    private static void DrawPsychological(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "double")
        {
            DrawPsychologicalDouble(canvas, width, height, palette, direction);
            return;
        }

        using SKPath face = BuildPath(builder =>
        {
            builder.MoveTo(width * 0.28f, height * 0.12f);
            builder.CubicTo(width * 0.76f, height * 0.08f, width * 0.82f, height * 0.47f, width * 0.59f, height * 0.58f);
            builder.CubicTo(width * 0.42f, height * 0.64f, width * 0.28f, height * 0.48f, width * 0.28f, height * 0.12f);
            builder.Close();
        });
        using var fill = Paint(WithAlpha(palette.Paper, 175));
        canvas.DrawPath(face, fill);
        using var cut = Paint(palette.Ink);
        int slices = 7;
        for (int index = 0; index < slices; index++)
        {
            float y = height * (0.18f + index * 0.055f);
            float offset = (index % 2 == 0 ? 1 : -1) * width * (0.02f + direction.Variation * 0.002f);
            canvas.DrawRect(width * 0.22f + offset, y, width * 0.62f, height * 0.018f, cut);
        }

        cut.Color = palette.Accent;
        canvas.DrawCircle(width * 0.58f, height * 0.29f, width * 0.027f, cut);
        using var fracture = Stroke(WithAlpha(palette.Highlight, 200), width * 0.006f);
        canvas.DrawLine(width * 0.50f, height * 0.08f, width * 0.43f, height * 0.57f, fracture);
        canvas.DrawLine(width * 0.50f, height * 0.27f, width * 0.68f, height * 0.52f, fracture);
    }

    private static void DrawPsychologicalDouble(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float separation = width * (0.05f + direction.Variation % 3 * 0.018f);
        using var pale = Paint(WithAlpha(palette.Paper, 170));
        using SKPath left = BuildPath(builder =>
        {
            builder.MoveTo(width * 0.10f, height * 0.16f);
            builder.CubicTo(width * 0.40f, height * 0.06f, width * 0.53f - separation, height * 0.26f, width * 0.48f - separation, height * 0.58f);
            builder.CubicTo(width * 0.30f, height * 0.55f, width * 0.12f, height * 0.43f, width * 0.10f, height * 0.16f);
            builder.Close();
        });
        canvas.DrawPath(left, pale);
        canvas.Save();
        canvas.Scale(-1, 1, width / 2f, height * 0.34f);
        pale.Color = WithAlpha(palette.Midtone, 185);
        canvas.DrawPath(left, pale);
        canvas.Restore();
        using var divide = Stroke(WithAlpha(palette.Accent, 235), width * 0.012f);
        canvas.DrawLine(width * 0.50f, height * 0.07f, width * 0.50f, height * 0.64f, divide);
        using var eyes = Paint(palette.Ink);
        canvas.DrawCircle(width * 0.36f, height * 0.29f, width * 0.024f, eyes);
        canvas.DrawCircle(width * 0.64f, height * 0.29f, width * 0.024f, eyes);
    }

    private static void DrawBodyHorror(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
        if (direction.Motif is "metamorphosis")
        {
            DrawBodyMetamorphosis(canvas, width, height, palette, direction);
            return;
        }

        using SKPath membrane = BuildPath(builder =>
        {
            builder.MoveTo(width * 0.08f, height * 0.39f);
            builder.CubicTo(width * 0.22f, height * 0.04f, width * 0.67f, height * 0.08f, width * 0.92f, height * 0.32f);
            builder.CubicTo(width * 0.75f, height * 0.67f, width * 0.26f, height * 0.63f, width * 0.08f, height * 0.39f);
            builder.Close();
        });
        using var paint = Paint(WithAlpha(palette.Accent, 215));
        canvas.DrawPath(membrane, paint);
        for (int index = 0; index < 28; index++)
        {
            float x = random.Between(width * 0.16f, width * 0.84f);
            float y = random.Between(height * 0.18f, height * 0.54f);
            float radius = random.Between(width * 0.012f, width * 0.055f);
            paint.Color = index % 3 == 0 ? palette.Highlight : WithAlpha(palette.Ink, 170);
            canvas.DrawOval(new SKRect(x - radius, y - radius * 0.7f, x + radius, y + radius * 0.7f), paint);
        }

        using var vein = Stroke(WithAlpha(palette.Paper, 135), width * 0.004f);
        for (int index = 0; index < 8; index++)
        {
            float y = height * (0.19f + index * 0.045f);
            canvas.DrawLine(width * 0.13f, y, width * (0.72f + index * 0.02f), height * 0.38f, vein);
        }
    }

    private static void DrawBodyMetamorphosis(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float centerX = width * (0.46f + direction.Variation % 3 * 0.04f);
        using SKPath cocoon = BuildPath(builder =>
        {
            builder.MoveTo(centerX, height * 0.07f);
            builder.CubicTo(centerX + width * 0.26f, height * 0.16f, centerX + width * 0.22f, height * 0.53f, centerX, height * 0.65f);
            builder.CubicTo(centerX - width * 0.22f, height * 0.53f, centerX - width * 0.26f, height * 0.16f, centerX, height * 0.07f);
            builder.Close();
        });
        using var shell = Paint();
        shell.Shader = SKShader.CreateRadialGradient(
            new SKPoint(centerX - width * 0.07f, height * 0.25f),
            width * 0.35f,
            [palette.Highlight, palette.Accent, palette.Ink],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawPath(cocoon, shell);
        using var seam = Stroke(WithAlpha(palette.Paper, 155), width * 0.006f);
        for (int index = 0; index < 9; index++)
        {
            float y = height * (0.15f + index * 0.05f);
            float halfWidth = width * (0.08f + MathF.Sin(index / 8f * MathF.PI) * 0.12f);
            canvas.DrawLine(centerX - halfWidth, y, centerX + halfWidth, y + height * 0.025f, seam);
        }

        using var silhouette = Paint(WithAlpha(palette.Ink, 220));
        canvas.DrawCircle(centerX, height * 0.26f, width * 0.045f, silhouette);
        canvas.DrawOval(new SKRect(centerX - width * 0.045f, height * 0.30f, centerX + width * 0.045f, height * 0.50f), silhouette);
    }

    private static void DrawCosmic(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
        using var star = Paint(palette.Title);
        for (int index = 0; index < 90; index++)
        {
            float x = random.Between(width * 0.04f, width * 0.96f);
            float y = random.Between(height * 0.04f, height * 0.58f);
            float radius = random.Between(0.5f, width * 0.006f);
            star.Color = WithAlpha(index % 7 == 0 ? palette.Highlight : palette.Title, (byte)(90 + random.Next(150)));
            canvas.DrawCircle(x, y, radius, star);
        }

        if (direction.Motif is "monolith")
        {
            DrawCosmicMonolith(canvas, width, height, palette, direction);
            return;
        }

        float planetX = width * (direction.Variation % 2 == 0 ? 0.39f : 0.61f);
        float planetY = height * 0.28f;
        float planetRadius = width * 0.27f;
        using var planet = Paint();
        planet.Shader = SKShader.CreateRadialGradient(
            new SKPoint(planetX - planetRadius * 0.25f, planetY - planetRadius * 0.22f),
            planetRadius * 1.2f,
            [palette.Highlight, palette.Accent, palette.Ink],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawCircle(planetX, planetY, planetRadius, planet);
        using var eclipse = Paint(palette.Ink);
        canvas.DrawCircle(planetX + planetRadius * 0.22f, planetY - planetRadius * 0.04f, planetRadius * 0.94f, eclipse);
        using var monolith = Paint(WithAlpha(palette.Shadow, 245));
        canvas.DrawRect(width * 0.46f, height * 0.37f, width * 0.08f, height * 0.30f, monolith);
    }

    private static void DrawCosmicMonolith(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float centerX = width * (0.40f + direction.Variation % 4 * 0.065f);
        using var halo = Stroke(WithAlpha(palette.Highlight, 100), width * 0.008f);
        for (int ring = 0; ring < 5; ring++)
        {
            canvas.DrawOval(
                new SKRect(
                    centerX - width * (0.14f + ring * 0.055f),
                    height * (0.10f + ring * 0.025f),
                    centerX + width * (0.14f + ring * 0.055f),
                    height * (0.53f - ring * 0.01f)),
                halo);
            halo.Color = WithAlpha(palette.Accent, (byte)(115 - ring * 14));
        }

        using SKPath monolith = BuildPolygon(
        [
            new SKPoint(centerX - width * 0.11f, height * 0.13f),
            new SKPoint(centerX + width * 0.08f, height * 0.09f),
            new SKPoint(centerX + width * 0.13f, height * 0.61f),
            new SKPoint(centerX - width * 0.14f, height * 0.61f),
        ]);
        using var stone = Paint();
        stone.Shader = SKShader.CreateLinearGradient(
            new SKPoint(centerX - width * 0.14f, height * 0.2f),
            new SKPoint(centerX + width * 0.13f, height * 0.5f),
            [palette.Ink, palette.Midtone, palette.Shadow],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawPath(monolith, stone);
        using var horizon = Stroke(WithAlpha(palette.Highlight, 115), width * 0.006f);
        canvas.DrawLine(0, height * 0.61f, width, height * 0.61f, horizon);
    }

    private static void DrawCreature(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "claw")
        {
            DrawCreatureClaw(canvas, width, height, palette, direction);
            return;
        }

        using SKPath maw = BuildPath(builder =>
        {
            builder.MoveTo(width * 0.06f, height * 0.30f);
            builder.CubicTo(width * 0.28f, height * 0.04f, width * 0.72f, height * 0.04f, width * 0.94f, height * 0.30f);
            builder.CubicTo(width * 0.72f, height * 0.63f, width * 0.28f, height * 0.63f, width * 0.06f, height * 0.30f);
            builder.Close();
        });
        using var paint = Paint(WithAlpha(palette.Accent, 210));
        canvas.DrawPath(maw, paint);
        using var mouth = Paint(palette.Ink);
        canvas.DrawOval(new SKRect(width * 0.16f, height * 0.21f, width * 0.84f, height * 0.51f), mouth);
        using var teeth = Paint(palette.Paper);
        int count = 11 + direction.Variation % 4;
        for (int index = 0; index < count; index++)
        {
            float x = width * (0.19f + index * 0.62f / (count - 1));
            using SKPath tooth = BuildPolygon(
            [
                new SKPoint(x - width * 0.025f, height * 0.22f),
                new SKPoint(x + width * 0.025f, height * 0.22f),
                new SKPoint(x, height * (0.32f + (index % 3) * 0.015f)),
            ]);
            canvas.DrawPath(tooth, teeth);
            canvas.Save();
            canvas.Scale(1, -1, width / 2f, height * 0.365f);
            canvas.DrawPath(tooth, teeth);
            canvas.Restore();
        }
    }

    private static void DrawCreatureClaw(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        using var wound = Paint(WithAlpha(palette.Accent, 230));
        float drift = width * (direction.Variation % 3 * 0.04f);
        for (int index = 0; index < 4; index++)
        {
            float startX = width * (0.18f + index * 0.16f) + drift;
            using SKPath slash = BuildPath(builder =>
            {
                builder.MoveTo(startX, height * 0.08f);
                builder.CubicTo(
                    startX - width * 0.06f,
                    height * 0.24f,
                    startX + width * 0.12f,
                    height * 0.44f,
                    startX + width * 0.04f,
                    height * 0.62f);
                builder.LineTo(startX + width * 0.11f, height * 0.59f);
                builder.CubicTo(
                    startX + width * 0.18f,
                    height * 0.40f,
                    startX,
                    height * 0.23f,
                    startX + width * 0.06f,
                    height * 0.10f);
                builder.Close();
            });
            canvas.DrawPath(slash, wound);
            wound.Color = index % 2 == 0
                ? WithAlpha(palette.Highlight, 210)
                : WithAlpha(palette.Accent, 225);
        }

        using var eye = Paint(palette.Ink);
        canvas.DrawOval(new SKRect(width * 0.25f, height * 0.24f, width * 0.75f, height * 0.42f), eye);
        eye.Color = palette.Highlight;
        canvas.DrawCircle(width * 0.5f, height * 0.33f, width * 0.055f, eye);
        eye.Color = palette.Ink;
        canvas.DrawCircle(width * 0.5f, height * 0.33f, width * 0.024f, eye);
    }

    private static void DrawIsolation(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
        if (direction.Motif is "whiteout")
        {
            DrawIsolationWhiteout(canvas, width, height, palette, ref random);
            return;
        }

        if (direction.Motif is "horizon")
        {
            DrawIsolationHorizon(canvas, width, height, palette, direction);
            return;
        }

        using var distant = Paint(WithAlpha(palette.Highlight, 120));
        using var near = Paint(palette.Ink);
        for (int layer = 0; layer < 3; layer++)
        {
            float baseY = height * (0.34f + layer * 0.10f);
            var ridgePoints = new SKPoint[11];
            ridgePoints[0] = new SKPoint(0, height * 0.68f);
            for (int point = 0; point <= 8; point++)
            {
                ridgePoints[point + 1] = new SKPoint(
                    width * point / 8f,
                    baseY - random.Between(0, height * 0.12f));
            }

            ridgePoints[^1] = new SKPoint(width, height * 0.68f);
            using SKPath ridge = BuildPolygon(ridgePoints);
            canvas.DrawPath(ridge, layer == 2 ? near : distant);
            distant.Color = WithAlpha(palette.Midtone, (byte)(130 + layer * 35));
        }

        using var cabin = Paint(palette.Shadow);
        float cabinX = width * (0.32f + direction.Variation % 4 * 0.08f);
        canvas.DrawRect(cabinX, height * 0.48f, width * 0.22f, height * 0.13f, cabin);
        using SKPath roof = BuildPolygon(
        [
            new SKPoint(cabinX - width * 0.03f, height * 0.49f),
            new SKPoint(cabinX + width * 0.11f, height * 0.40f),
            new SKPoint(cabinX + width * 0.25f, height * 0.49f),
        ]);
        canvas.DrawPath(roof, cabin);
        cabin.Color = palette.Highlight;
        canvas.DrawRect(cabinX + width * 0.13f, height * 0.52f, width * 0.045f, height * 0.04f, cabin);
    }

    private static void DrawIsolationWhiteout(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        ref PosterRandom random)
    {
        using var fog = Paint(WithAlpha(palette.Paper, 155));
        fog.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, height * 0.12f),
            new SKPoint(0, height * 0.65f),
            [WithAlpha(palette.Paper, 45), WithAlpha(palette.Highlight, 190), WithAlpha(palette.Paper, 80)],
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, height * 0.08f, width, height * 0.58f, fog);
        using var snow = Stroke(WithAlpha(palette.Paper, 185), width * 0.005f);
        for (int index = 0; index < 70; index++)
        {
            float x = random.Between(-width * 0.1f, width);
            float y = random.Between(height * 0.08f, height * 0.64f);
            float length = random.Between(width * 0.025f, width * 0.14f);
            canvas.DrawLine(x, y, x + length, y - length * 0.32f, snow);
        }

        using var figure = Paint(WithAlpha(palette.Ink, 235));
        canvas.DrawCircle(width * 0.51f, height * 0.43f, width * 0.025f, figure);
        using SKPath body = BuildPolygon(
        [
            new SKPoint(width * 0.49f, height * 0.46f),
            new SKPoint(width * 0.45f, height * 0.61f),
            new SKPoint(width * 0.57f, height * 0.61f),
            new SKPoint(width * 0.53f, height * 0.46f),
        ]);
        canvas.DrawPath(body, figure);
    }

    private static void DrawIsolationHorizon(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float vanishingX = width * (0.28f + direction.Variation % 5 * 0.11f);
        float horizonY = height * 0.31f;
        using var sun = Paint(WithAlpha(palette.Accent, 165));
        canvas.DrawCircle(vanishingX, horizonY, width * 0.13f, sun);
        using var ground = Paint(palette.Ink);
        canvas.DrawRect(0, horizonY, width, height * 0.36f, ground);
        using SKPath road = BuildPolygon(
        [
            new SKPoint(vanishingX - width * 0.012f, horizonY),
            new SKPoint(vanishingX + width * 0.012f, horizonY),
            new SKPoint(width * 0.82f, height * 0.67f),
            new SKPoint(width * 0.18f, height * 0.67f),
        ]);
        using var roadPaint = Paint(WithAlpha(palette.Midtone, 210));
        canvas.DrawPath(road, roadPaint);
        using var markers = Stroke(WithAlpha(palette.Highlight, 180), width * 0.008f);
        for (int index = 1; index <= 5; index++)
        {
            float fraction = index / 6f;
            float y = horizonY + height * 0.36f * fraction * fraction;
            float half = width * 0.02f * fraction;
            canvas.DrawLine(vanishingX - half, y, vanishingX + half, y, markers);
        }
    }

    private static void DrawFoundFootage(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
        if (direction.Motif is "interface")
        {
            DrawFoundFootageInterface(canvas, width, height, palette, direction);
            return;
        }

        using var line = Stroke(WithAlpha(palette.Highlight, 205), width * 0.006f);
        float inset = width * (0.065f + direction.Variation % 3 * 0.018f);
        float length = width * (0.12f + direction.Variation % 4 * 0.018f);
        DrawCorner(canvas, inset, inset, length, 1, 1, line);
        DrawCorner(canvas, width - inset, inset, length, -1, 1, line);
        DrawCorner(canvas, inset, height * 0.61f, length, 1, -1, line);
        DrawCorner(canvas, width - inset, height * 0.61f, length, -1, -1, line);
        float targetX = random.Between(width * 0.34f, width * 0.66f);
        float targetY = random.Between(height * 0.24f, height * 0.42f);
        float targetRadius = random.Between(width * 0.075f, width * 0.145f);
        if (direction.Variation % 3 == 0)
        {
            canvas.DrawRect(
                targetX - targetRadius,
                targetY - targetRadius,
                targetRadius * 2,
                targetRadius * 2,
                line);
        }
        else
        {
            canvas.DrawCircle(targetX, targetY, targetRadius, line);
        }

        canvas.DrawLine(targetX, targetY - targetRadius * 1.55f, targetX, targetY + targetRadius * 1.55f, line);
        canvas.DrawLine(targetX - targetRadius * 1.55f, targetY, targetX + targetRadius * 1.55f, targetY, line);
        using var subject = Paint(WithAlpha(palette.Ink, 230));
        float subjectX = random.Between(width * 0.20f, width * 0.80f);
        float subjectY = random.Between(height * 0.38f, height * 0.50f);
        canvas.DrawCircle(subjectX, subjectY, width * 0.028f, subject);
        canvas.DrawOval(
            new SKRect(
                subjectX - width * 0.035f,
                subjectY + height * 0.025f,
                subjectX + width * 0.035f,
                subjectY + height * 0.17f),
            subject);
        using var record = Paint(new SKColor(239, 53, 65));
        canvas.DrawCircle(width * 0.12f, height * 0.11f, width * 0.018f, record);
        using var scan = Paint(WithAlpha(palette.Paper, 24));
        for (float y = 0; y < height * 0.68f; y += Math.Max(3, height * 0.008f))
        {
            canvas.DrawRect(0, y, width, Math.Max(1, height * 0.002f), scan);
        }

    }

    private static void DrawFoundFootageInterface(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        using var panel = Paint(WithAlpha(palette.Midtone, 150));
        using var border = Stroke(WithAlpha(palette.Highlight, 190), width * 0.005f);
        int columns = direction.Variation % 3 == 0 ? 3 : 2;
        int rows = 3;
        float gap = width * 0.025f;
        float panelWidth = (width * 0.84f - gap * (columns - 1)) / columns;
        float panelHeight = height * 0.14f;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                float x = width * 0.08f + column * (panelWidth + gap);
                float y = height * 0.08f + row * (panelHeight + height * 0.025f);
                SKRect rect = new(x, y, x + panelWidth, y + panelHeight);
                canvas.DrawRect(rect, panel);
                canvas.DrawRect(rect, border);
                if ((row + column) % 2 == 0)
                {
                    using var signal = Paint(WithAlpha(palette.Accent, 165));
                    canvas.DrawCircle(rect.MidX, rect.MidY, Math.Min(panelWidth, panelHeight) * 0.18f, signal);
                }
            }
        }

        using var glitch = Paint(WithAlpha(palette.Highlight, 80));
        for (int index = 0; index < 7; index++)
        {
            float y = height * (0.11f + index * 0.073f);
            float x = width * ((index * 19 + direction.Variation * 7) % 61) / 100f;
            canvas.DrawRect(x, y, width * (0.28f + index % 3 * 0.09f), height * 0.009f, glitch);
        }

        using var record = Paint(new SKColor(239, 53, 65));
        canvas.DrawCircle(width * 0.91f, height * 0.08f, width * 0.016f, record);
    }

    private static void DrawGothic(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "woodcut")
        {
            DrawGothicWoodcut(canvas, width, height, palette, direction);
            return;
        }

        using var architecture = Paint(WithAlpha(palette.Ink, 245));
        int towers = 5 + direction.Variation % 3;
        for (int index = 0; index < towers; index++)
        {
            float towerWidth = width * (0.08f + index % 2 * 0.02f);
            float x = width * (0.12f + index * 0.76f / Math.Max(1, towers - 1)) - towerWidth / 2;
            float top = height * (0.18f + index % 3 * 0.08f);
            canvas.DrawRect(x, top, towerWidth, height * 0.49f - top, architecture);
            using SKPath spire = BuildPolygon(
            [
                new SKPoint(x - width * 0.015f, top),
                new SKPoint(x + towerWidth / 2, top - height * 0.13f),
                new SKPoint(x + towerWidth + width * 0.015f, top),
            ]);
            canvas.DrawPath(spire, architecture);
        }

        using var window = Paint(WithAlpha(palette.Accent, 220));
        for (int index = 0; index < towers; index++)
        {
            float x = width * (0.12f + index * 0.76f / Math.Max(1, towers - 1));
            canvas.DrawOval(new SKRect(x - width * 0.014f, height * 0.33f, x + width * 0.014f, height * 0.39f), window);
        }
    }

    private static void DrawGothicWoodcut(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float moonX = width * (0.33f + direction.Variation % 4 * 0.11f);
        using var moon = Paint(WithAlpha(palette.Paper, 180));
        canvas.DrawCircle(moonX, height * 0.25f, width * 0.20f, moon);
        using var branch = Stroke(WithAlpha(palette.Ink, 245), width * 0.018f);
        branch.StrokeCap = SKStrokeCap.Square;
        for (int side = -1; side <= 1; side += 2)
        {
            float trunkX = side < 0 ? width * 0.12f : width * 0.88f;
            canvas.DrawLine(trunkX, height * 0.66f, trunkX + side * width * -0.06f, height * 0.13f, branch);
            for (int index = 0; index < 5; index++)
            {
                float y = height * (0.20f + index * 0.08f);
                float reach = width * (0.12f + index * 0.035f);
                canvas.DrawLine(trunkX, y + height * 0.08f, trunkX - side * reach, y, branch);
            }
        }

        using var hill = Paint(palette.Ink);
        canvas.DrawOval(new SKRect(-width * 0.1f, height * 0.52f, width * 1.1f, height * 0.80f), hill);
        using var hatch = Stroke(WithAlpha(palette.Highlight, 75), width * 0.004f);
        for (int index = 0; index < 17; index++)
        {
            float x = width * index / 16f;
            canvas.DrawLine(x, height * 0.50f, x + width * 0.12f, height * 0.65f, hatch);
        }
    }

    private static void DrawHomeInvasion(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "window")
        {
            DrawHomeInvasionWindow(canvas, width, height, palette, direction);
            return;
        }

        using var wall = Paint(WithAlpha(palette.Paper, 95));
        canvas.DrawRect(width * 0.08f, height * 0.08f, width * 0.84f, height * 0.56f, wall);
        using var doorway = Paint(palette.Ink);
        float doorX = width * (direction.Variation % 2 == 0 ? 0.21f : 0.55f);
        canvas.DrawRect(doorX, height * 0.17f, width * 0.24f, height * 0.47f, doorway);
        using SKPath light = BuildPolygon(
        [
            new SKPoint(doorX + width * 0.24f, height * 0.17f),
            new SKPoint(width * 0.88f, height * 0.64f),
            new SKPoint(doorX + width * 0.24f, height * 0.64f),
        ]);
        using var lightPaint = Paint(WithAlpha(palette.Accent, 125));
        canvas.DrawPath(light, lightPaint);
        using var shadow = Paint(WithAlpha(palette.Ink, 245));
        float figureX = doorX + width * 0.13f;
        canvas.DrawCircle(figureX, height * 0.31f, width * 0.045f, shadow);
        using SKPath body = BuildPolygon(
        [
            new SKPoint(figureX - width * 0.045f, height * 0.36f),
            new SKPoint(figureX - width * 0.08f, height * 0.61f),
            new SKPoint(figureX + width * 0.08f, height * 0.61f),
            new SKPoint(figureX + width * 0.045f, height * 0.36f),
        ]);
        canvas.DrawPath(body, shadow);
    }

    private static void DrawHomeInvasionWindow(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        SKRect windowRect = new(width * 0.17f, height * 0.08f, width * 0.83f, height * 0.61f);
        using var night = Paint(WithAlpha(palette.Midtone, 190));
        canvas.DrawRect(windowRect, night);
        using var frame = Stroke(WithAlpha(palette.Paper, 210), width * 0.018f);
        frame.StrokeCap = SKStrokeCap.Square;
        canvas.DrawRect(windowRect, frame);
        float split = width * (0.42f + direction.Variation % 4 * 0.055f);
        canvas.DrawLine(split, windowRect.Top, split, windowRect.Bottom, frame);
        canvas.DrawLine(windowRect.Left, height * 0.34f, windowRect.Right, height * 0.34f, frame);
        using var figure = Paint(WithAlpha(palette.Ink, 245));
        float figureX = width * (0.28f + direction.Variation % 5 * 0.10f);
        canvas.DrawCircle(figureX, height * 0.28f, width * 0.055f, figure);
        canvas.DrawOval(
            new SKRect(figureX - width * 0.08f, height * 0.34f, figureX + width * 0.08f, height * 0.62f),
            figure);
        using var curtain = Paint(WithAlpha(palette.Accent, 150));
        using SKPath leftCurtain = BuildPolygon(
        [
            new SKPoint(windowRect.Left, windowRect.Top),
            new SKPoint(windowRect.Left + width * 0.22f, windowRect.Top),
            new SKPoint(windowRect.Left + width * 0.10f, windowRect.Bottom),
            new SKPoint(windowRect.Left, windowRect.Bottom),
        ]);
        canvas.DrawPath(leftCurtain, curtain);
    }

    private static void DrawHorrorComedy(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "pop-shadow")
        {
            DrawHorrorComedyPopShadow(canvas, width, height, palette, direction);
            return;
        }

        float centerX = width * 0.5f;
        float centerY = height * 0.33f;
        int points = 22;
        var burstPoints = new SKPoint[points];
        for (int index = 0; index < points; index++)
        {
            double angle = index * Math.PI * 2 / points;
            float radius = width * (index % 2 == 0 ? 0.43f : 0.28f);
            burstPoints[index] = new SKPoint(
                centerX + MathF.Cos((float)angle) * radius,
                centerY + MathF.Sin((float)angle) * radius);
        }

        using SKPath burst = BuildPolygon(burstPoints);
        using var paint = Paint(palette.Midtone);
        canvas.DrawPath(burst, paint);
        paint.Color = palette.Highlight;
        canvas.DrawCircle(centerX, centerY, width * 0.21f, paint);
        paint.Color = palette.Ink;
        canvas.DrawCircle(centerX - width * 0.075f, centerY - height * 0.025f, width * 0.045f, paint);
        using var crossedEye = Stroke(palette.Ink, width * 0.018f);
        canvas.DrawLine(centerX + width * 0.035f, centerY - height * 0.06f, centerX + width * 0.115f, centerY + height * 0.01f, crossedEye);
        canvas.DrawLine(centerX + width * 0.115f, centerY - height * 0.06f, centerX + width * 0.035f, centerY + height * 0.01f, crossedEye);
        using var grin = Stroke(palette.Ink, width * 0.025f);
        canvas.DrawArc(new SKRect(centerX - width * 0.12f, centerY, centerX + width * 0.12f, centerY + height * 0.12f), 8, 164, false, grin);
        using var fang = Paint(palette.Ink);
        using SKPath fangPath = BuildPolygon(
        [
            new SKPoint(centerX + width * 0.025f, centerY + height * 0.075f),
            new SKPoint(centerX + width * 0.065f, centerY + height * 0.072f),
            new SKPoint(centerX + width * 0.045f, centerY + height * 0.125f),
        ]);
        canvas.DrawPath(fangPath, fang);
        if (direction.Variation % 2 == 0)
        {
            using var offset = Stroke(WithAlpha(palette.Accent, 210), width * 0.012f);
            canvas.DrawCircle(centerX + width * 0.025f, centerY + width * 0.02f, width * 0.24f, offset);
        }
    }

    private static void DrawHorrorComedyPopShadow(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        int rays = 18;
        var burst = new SKPoint[rays];
        float centerX = width * (0.43f + direction.Variation % 4 * 0.045f);
        float centerY = height * 0.33f;
        for (int index = 0; index < rays; index++)
        {
            float angle = index * MathF.PI * 2 / rays;
            float radius = width * (index % 2 == 0 ? 0.45f : 0.12f);
            burst[index] = new SKPoint(
                centerX + MathF.Cos(angle) * radius,
                centerY + MathF.Sin(angle) * radius * 0.82f);
        }

        using SKPath burstPath = BuildPolygon(burst);
        using var bright = Paint(palette.Highlight);
        canvas.DrawPath(burstPath, bright);
        using var shadow = Paint(WithAlpha(palette.Ink, 235));
        canvas.DrawCircle(centerX, centerY - height * 0.04f, width * 0.15f, shadow);
        canvas.DrawOval(
            new SKRect(centerX - width * 0.20f, centerY + height * 0.07f, centerX + width * 0.20f, centerY + height * 0.29f),
            shadow);
        using SKPath horns = BuildPolygon(
        [
            new SKPoint(centerX - width * 0.14f, centerY - height * 0.12f),
            new SKPoint(centerX - width * 0.24f, centerY - height * 0.24f),
            new SKPoint(centerX - width * 0.04f, centerY - height * 0.15f),
            new SKPoint(centerX + width * 0.04f, centerY - height * 0.15f),
            new SKPoint(centerX + width * 0.24f, centerY - height * 0.24f),
            new SKPoint(centerX + width * 0.14f, centerY - height * 0.12f),
        ]);
        canvas.DrawPath(horns, shadow);
        using var eyes = Paint(palette.Accent);
        canvas.DrawCircle(centerX - width * 0.06f, centerY - height * 0.05f, width * 0.025f, eyes);
        canvas.DrawCircle(centerX + width * 0.06f, centerY - height * 0.05f, width * 0.025f, eyes);
    }

    private static void DrawGeneral(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        if (direction.Motif is "void")
        {
            DrawGeneralVoid(canvas, width, height, palette, direction);
            return;
        }

        using var line = Stroke(WithAlpha(palette.Highlight, 190), width * 0.018f);
        float inset = width * 0.08f;
        int turns = 8 + direction.Variation % 5;
        for (int index = 0; index < turns; index++)
        {
            float fraction = index / (float)turns;
            SKRect rect = new(
                inset + width * fraction * 0.31f,
                height * 0.08f + height * fraction * 0.20f,
                width - inset - width * fraction * 0.31f,
                height * 0.61f - height * fraction * 0.20f);
            canvas.DrawRect(rect, line);
            line.Color = index % 2 == 0 ? WithAlpha(palette.Accent, 190) : WithAlpha(palette.Highlight, 160);
        }
    }

    private static void DrawGeneralVoid(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float centerX = width * (0.38f + direction.Variation % 4 * 0.08f);
        float centerY = height * 0.32f;
        using var halo = Paint(WithAlpha(palette.Accent, 160));
        canvas.DrawCircle(centerX, centerY, width * 0.35f, halo);
        using var voidPaint = Paint(palette.Ink);
        canvas.DrawCircle(centerX, centerY, width * 0.25f, voidPaint);
        using var orbit = Stroke(WithAlpha(palette.Highlight, 145), width * 0.008f);
        canvas.DrawOval(
            new SKRect(centerX - width * 0.43f, centerY - height * 0.12f, centerX + width * 0.43f, centerY + height * 0.12f),
            orbit);
        canvas.Save();
        canvas.RotateDegrees(62, centerX, centerY);
        canvas.DrawOval(
            new SKRect(centerX - width * 0.39f, centerY - height * 0.10f, centerX + width * 0.39f, centerY + height * 0.10f),
            orbit);
        canvas.Restore();
        using var figure = Paint(WithAlpha(palette.Paper, 200));
        canvas.DrawCircle(centerX, height * 0.54f, width * 0.022f, figure);
        canvas.DrawRect(centerX - width * 0.016f, height * 0.565f, width * 0.032f, height * 0.085f, figure);
    }

    private static void DrawTexture(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
        int flecks = (int)(width * height * direction.TextureStrength / 520);
        using var texture = Paint();
        for (int index = 0; index < flecks; index++)
        {
            float x = random.Between(0, width);
            float y = random.Between(0, height);
            float size = random.Between(0.5f, Math.Max(1.2f, width * 0.004f));
            byte alpha = (byte)(12 + random.Next(34));
            texture.Color = WithAlpha(index % 4 == 0 ? palette.Paper : palette.Ink, alpha);
            canvas.DrawCircle(x, y, size, texture);
        }

        if (direction.EraStyle is PosterEraStyle.Grindhouse or PosterEraStyle.Analog)
        {
            using var scratch = Stroke(WithAlpha(palette.Paper, 40), Math.Max(1, width * 0.002f));
            int scratches = 8 + direction.Variation;
            for (int index = 0; index < scratches; index++)
            {
                float x = random.Between(0, width);
                float y = random.Between(0, height * 0.8f);
                canvas.DrawLine(x, y, x + random.Between(-width * 0.02f, width * 0.02f), y + random.Between(height * 0.03f, height * 0.25f), scratch);
            }
        }
    }

    private static void DrawTypography(
        SKCanvas canvas,
        int width,
        int height,
        PosterDescriptor descriptor,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float panelTop = height * 0.66f;
        using var panel = Paint();
        panel.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, panelTop - height * 0.06f),
            new SKPoint(0, height),
            [SKColors.Transparent, WithAlpha(palette.Ink, 244), palette.Ink],
            [0, 0.22f, 1],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, panelTop - height * 0.06f, width, height - panelTop + height * 0.06f, panel);

        string family = direction.TitleStyle switch
        {
            PosterTitleStyle.Technical => "Consolas",
            PosterTitleStyle.Editorial => "Georgia",
            PosterTitleStyle.Monumental => "Arial Black",
            _ => "Bahnschrift",
        };
        using SKTypeface typeface = SKTypeface.FromFamilyName(family, SKFontStyle.Bold) ?? SKTypeface.Default;
        using var titlePaint = Paint(palette.Title);
        titlePaint.Style = SKPaintStyle.Fill;
        List<string> lines = BreakTitle(descriptor.Title, 18);
        float availableWidth = width * 0.84f;
        float maximumSize = height * (lines.Count >= 3 ? 0.070f : 0.088f);
        float titleSize = FitText(lines, typeface, titlePaint, availableWidth, maximumSize, height * 0.045f);
        using var titleFont = new SKFont(typeface, titleSize)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };
        float leading = titleSize * 0.91f;
        float titleY = panelTop + titleSize;
        foreach (string line in lines)
        {
            if (direction.TitleStyle is PosterTitleStyle.Distressed)
            {
                using var shadow = Paint(WithAlpha(palette.Accent, 210));
                canvas.DrawText(line.ToUpperInvariant(), width * 0.081f, titleY + width * 0.008f, SKTextAlign.Left, titleFont, shadow);
            }

            canvas.DrawText(line.ToUpperInvariant(), width * 0.08f, titleY, SKTextAlign.Left, titleFont, titlePaint);
            titleY += leading;
        }

        using SKTypeface labelTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var labelPaint = Paint(WithAlpha(palette.Highlight, 225));
        using var labelFont = new SKFont(labelTypeface, height * 0.018f)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };
        canvas.DrawText(direction.Eyebrow, width * 0.08f, panelTop - height * 0.018f, SKTextAlign.Left, labelFont, labelPaint);

        string footer = string.IsNullOrWhiteSpace(descriptor.Franchise)
            ? direction.Motif.Replace('-', ' ').ToUpperInvariant()
            : descriptor.Franchise.ToUpperInvariant();
        using var footerFont = new SKFont(labelTypeface, height * 0.014f)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };
        labelPaint.Color = WithAlpha(palette.Paper, 150);
        canvas.DrawText(footer, width * 0.08f, height * 0.925f, SKTextAlign.Left, footerFont, labelPaint);
        using var accentRule = Paint(WithAlpha(palette.Accent, 210));
        canvas.DrawRect(width * 0.08f, height * 0.948f, width * 0.24f, Math.Max(2, height * 0.004f), accentRule);
        labelPaint.Color = WithAlpha(palette.Paper, 105);
        canvas.DrawText("A NOCTURNE HARBOR SELECTION", width * 0.92f, height * 0.956f, SKTextAlign.Right, footerFont, labelPaint);
    }

    private static void DrawFrame(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        float thickness = direction.EraStyle is PosterEraStyle.Grindhouse ? width * 0.018f : width * 0.009f;
        using var frame = Stroke(WithAlpha(palette.Paper, 80), thickness);
        float inset = thickness / 2;
        canvas.DrawRect(inset, inset, width - thickness, height - thickness, frame);
    }

    private static float FitText(
        IReadOnlyList<string> lines,
        SKTypeface typeface,
        SKPaint paint,
        float availableWidth,
        float maximum,
        float minimum)
    {
        float size = maximum;
        while (size > minimum)
        {
            using var font = new SKFont(typeface, size);
            if (lines.All(line => font.MeasureText(line.ToUpperInvariant(), paint) <= availableWidth))
            {
                return size;
            }

            size -= 1;
        }

        return minimum;
    }

    private static List<string> BreakTitle(string title, int targetCharacters)
    {
        string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return ["UNTITLED"];
        }

        var lines = new List<string>(3);
        var current = new StringBuilder();
        foreach (string word in words)
        {
            if (current.Length > 0 && current.Length + word.Length + 1 > targetCharacters && lines.Count < 2)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private static void DrawCorner(
        SKCanvas canvas,
        float x,
        float y,
        float length,
        int horizontal,
        int vertical,
        SKPaint paint)
    {
        canvas.DrawLine(x, y, x + length * horizontal, y, paint);
        canvas.DrawLine(x, y, x, y + length * vertical, paint);
    }

    private static SKPath BuildPath(Action<SKPathBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        using var builder = new SKPathBuilder();
        configure(builder);
        return builder.Detach();
    }

    private static SKPath BuildPolygon(ReadOnlySpan<SKPoint> points)
    {
        using var builder = new SKPathBuilder();
        builder.AddPoly(points, close: true);
        return builder.Detach();
    }

    private static SKPaint Paint(SKColor? color = null) => new()
    {
        IsAntialias = true,
        Color = color ?? SKColors.White,
        Style = SKPaintStyle.Fill,
    };

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        IsAntialias = true,
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    private static SKColor WithAlpha(SKColor color, byte alpha) =>
        new(color.Red, color.Green, color.Blue, alpha);

}
