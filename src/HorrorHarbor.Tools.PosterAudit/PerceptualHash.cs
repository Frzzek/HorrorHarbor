using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace HorrorHarbor.Tools.PosterAudit;

public static class PerceptualHash
{
    public static ulong Calculate(ReadOnlySpan<byte> encoded, int expectedWidth, int expectedHeight)
    {
        byte[] pixels = DecodeRgba(encoded, expectedWidth, expectedHeight);
        Span<double> samples = stackalloc double[72];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 9; x++)
            {
                int sourceX = Math.Min(expectedWidth - 1, (x * expectedWidth + expectedWidth / 2) / 9);
                int sourceY = Math.Min(expectedHeight - 1, (y * expectedHeight + expectedHeight / 2) / 8);
                int index = (sourceY * expectedWidth + sourceX) * 4;
                samples[y * 9 + x] = pixels[index] * 0.299 + pixels[index + 1] * 0.587 + pixels[index + 2] * 0.114;
            }
        }

        ulong hash = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if (samples[y * 9 + x] > samples[y * 9 + x + 1])
                {
                    hash |= 1UL << (y * 8 + x);
                }
            }
        }

        return hash;
    }

    public static int Distance(ulong first, ulong second) => BitOperations.PopCount(first ^ second);

    private static byte[] DecodeRgba(ReadOnlySpan<byte> encoded, int width, int height)
    {
        int offset = 8;
        using var compressed = new MemoryStream();
        while (offset <= encoded.Length - 12)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(offset, 4)));
            ReadOnlySpan<byte> type = encoded.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(encoded.Slice(offset + 8, length));
            }

            offset += length + 12;
            if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        int stride = checked(width * 4);
        var filtered = new byte[checked((stride + 1) * height)];
        compressed.Position = 0;
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            inflater.ReadExactly(filtered);
            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("PNG contains more pixel data than expected.");
            }
        }

        var pixels = new byte[checked(stride * height)];
        for (int row = 0; row < height; row++)
        {
            int filter = filtered[row * (stride + 1)];
            ReadOnlySpan<byte> source = filtered.AsSpan(row * (stride + 1) + 1, stride);
            Span<byte> destination = pixels.AsSpan(row * stride, stride);
            ReadOnlySpan<byte> previous = row == 0
                ? ReadOnlySpan<byte>.Empty
                : pixels.AsSpan((row - 1) * stride, stride);
            for (int index = 0; index < stride; index++)
            {
                byte left = index < 4 ? (byte)0 : destination[index - 4];
                byte above = previous.IsEmpty ? (byte)0 : previous[index];
                byte upperLeft = previous.IsEmpty || index < 4 ? (byte)0 : previous[index - 4];
                destination[index] = filter switch
                {
                    0 => source[index],
                    1 => Add(source[index], left),
                    2 => Add(source[index], above),
                    3 => Add(source[index], (byte)((left + above) / 2)),
                    4 => Add(source[index], Paeth(left, above, upperLeft)),
                    _ => throw new InvalidDataException("PNG uses an unsupported row filter."),
                };
            }
        }

        return pixels;
    }

    private static byte Add(byte value, byte predictor) => unchecked((byte)(value + predictor));

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int aboveDistance = Math.Abs(estimate - above);
        int upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance
                ? above
                : upperLeft;
    }
}
