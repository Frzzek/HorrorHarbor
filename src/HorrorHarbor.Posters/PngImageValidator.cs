using System.Buffers.Binary;
using System.IO.Compression;

namespace HorrorHarbor.Posters;

public static class PngImageValidator
{
    private static ReadOnlySpan<byte> Signature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool IsValidFile(
        string path,
        int expectedWidth,
        int expectedHeight,
        long maximumFileBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumFileBytes <= 0 || expectedWidth <= 0 || expectedHeight <= 0)
        {
            return false;
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 || file.Length > maximumFileBytes)
            {
                return false;
            }

            return IsValid(File.ReadAllBytes(path), expectedWidth, expectedHeight, maximumFileBytes);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsValid(
        ReadOnlySpan<byte> encoded,
        int expectedWidth,
        int expectedHeight,
        long maximumFileBytes)
    {
        if (expectedWidth <= 0 || expectedHeight <= 0 ||
            encoded.Length is < 45 || encoded.Length > maximumFileBytes ||
            !encoded[..8].SequenceEqual(Signature))
        {
            return false;
        }

        try
        {
            int offset = 8;
            int chunkCount = 0;
            bool sawHeader = false;
            bool sawEnd = false;
            using var compressed = new MemoryStream();

            while (offset <= encoded.Length - 12 && chunkCount++ < 64)
            {
                int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(offset, 4)));
                offset += 4;
                if (length < 0 || length > maximumFileBytes || offset > encoded.Length - length - 8)
                {
                    return false;
                }

                ReadOnlySpan<byte> type = encoded.Slice(offset, 4);
                ReadOnlySpan<byte> payload = encoded.Slice(offset + 4, length);
                uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(offset + 4 + length, 4));
                if (Crc32.Calculate(type, payload) != storedCrc)
                {
                    return false;
                }

                if (type.SequenceEqual("IHDR"u8))
                {
                    if (sawHeader || chunkCount != 1 || length != 13 ||
                        BinaryPrimitives.ReadUInt32BigEndian(payload[..4]) != expectedWidth ||
                        BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) != expectedHeight ||
                        payload[8] != 8 || payload[9] != 6 || payload[10] != 0 ||
                        payload[11] != 0 || payload[12] != 0)
                    {
                        return false;
                    }

                    sawHeader = true;
                }
                else if (type.SequenceEqual("IDAT"u8))
                {
                    if (!sawHeader || sawEnd || compressed.Length + length > maximumFileBytes)
                    {
                        return false;
                    }

                    compressed.Write(payload);
                }
                else if (type.SequenceEqual("IEND"u8))
                {
                    if (!sawHeader || length != 0)
                    {
                        return false;
                    }

                    sawEnd = true;
                }
                else if ((type[0] & 0x20) == 0)
                {
                    return false;
                }

                offset += length + 8;
                if (sawEnd)
                {
                    break;
                }
            }

            if (!sawHeader || !sawEnd || compressed.Length == 0 || offset != encoded.Length)
            {
                return false;
            }

            int rowBytes = checked(expectedWidth * 4 + 1);
            int expectedDecodedBytes = checked(rowBytes * expectedHeight);
            if (expectedDecodedBytes > 100_000_000)
            {
                return false;
            }

            compressed.Position = 0;
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: false);
            var decoded = new byte[expectedDecodedBytes + 1];
            int totalRead = 0;
            while (totalRead < decoded.Length)
            {
                int read = inflater.Read(decoded, totalRead, decoded.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead != expectedDecodedBytes)
            {
                return false;
            }

            for (int row = 0; row < expectedHeight; row++)
            {
                if (decoded[row * rowBytes] > 4)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or OverflowException or ArgumentException)
        {
            return false;
        }
    }
}

internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Calculate(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in first)
        {
            crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        foreach (byte value in second)
        {
            crc = Table[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : 0xedb88320U ^ (value >> 1);
            }

            table[index] = value;
        }

        return table;
    }
}
