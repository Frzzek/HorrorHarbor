using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace HorrorHarbor.Posters;

internal static class GeneratedPosterRenderer
{
    public static byte[] Render(PosterDescriptor descriptor, int width, int height)
    {
        GeneratedPosterArtDirection direction = GeneratedPosterArtDirector.Analyze(descriptor);
        GeneratedPosterPalette palette = GeneratedPosterPaletteFactory.Create(descriptor, direction);
        byte[] seed = SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(descriptor)));
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
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [upper, palette.Ink, palette.Shadow],
            [0, 0.58f, 1],
            SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, width, height, paint);

        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint(width * 0.54f, height * 0.35f),
            width * 0.72f,
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
                DrawFoundFootage(canvas, width, height, palette, direction);
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

    private static void DrawSlasher(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
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

    private static void DrawBodyHorror(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
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

    private static void DrawCreature(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
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

    private static void DrawIsolation(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction,
        ref PosterRandom random)
    {
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

    private static void DrawFoundFootage(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
        using var line = Stroke(WithAlpha(palette.Highlight, 205), width * 0.006f);
        float inset = width * 0.08f;
        float length = width * 0.15f;
        DrawCorner(canvas, inset, inset, length, 1, 1, line);
        DrawCorner(canvas, width - inset, inset, length, -1, 1, line);
        DrawCorner(canvas, inset, height * 0.61f, length, 1, -1, line);
        DrawCorner(canvas, width - inset, height * 0.61f, length, -1, -1, line);
        canvas.DrawCircle(width * 0.50f, height * 0.34f, width * 0.11f, line);
        canvas.DrawLine(width * 0.50f, height * 0.19f, width * 0.50f, height * 0.49f, line);
        canvas.DrawLine(width * 0.35f, height * 0.34f, width * 0.65f, height * 0.34f, line);
        using var record = Paint(new SKColor(239, 53, 65));
        canvas.DrawCircle(width * 0.12f, height * 0.11f, width * 0.018f, record);
        using var scan = Paint(WithAlpha(palette.Paper, 24));
        for (float y = 0; y < height * 0.68f; y += Math.Max(3, height * 0.008f))
        {
            canvas.DrawRect(0, y, width, Math.Max(1, height * 0.002f), scan);
        }

        if (direction.Motif is "interface")
        {
            using var panel = Paint(WithAlpha(palette.Midtone, 110));
            canvas.DrawRect(width * 0.68f, height * 0.13f, width * 0.22f, height * 0.25f, panel);
        }
    }

    private static void DrawGothic(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
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

    private static void DrawHomeInvasion(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
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

    private static void DrawHorrorComedy(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
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

    private static void DrawGeneral(
        SKCanvas canvas,
        int width,
        int height,
        GeneratedPosterPalette palette,
        GeneratedPosterArtDirection direction)
    {
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

    private static string Canonicalize(PosterDescriptor descriptor) => string.Join(
        '|',
        descriptor.CandidateId.Trim(),
        descriptor.Title.Trim(),
        descriptor.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        descriptor.PrimaryLens?.Trim() ?? string.Empty,
        descriptor.SecondaryLens?.Trim() ?? string.Empty,
        descriptor.Franchise?.Trim() ?? string.Empty,
        descriptor.PosterTone.Trim(),
        descriptor.ScareIntensity.ToString(CultureInfo.InvariantCulture),
        descriptor.GoreIntensity.ToString(CultureInfo.InvariantCulture),
        descriptor.DreadIntensity.ToString(CultureInfo.InvariantCulture),
        string.Join(',', descriptor.Tags.Select(static tag => tag.Trim()).Order(StringComparer.Ordinal)));
}
