using System.Buffers.Binary;

namespace HorrorHarbor.Posters;

public sealed record ValidatedImage(int PixelWidth, int PixelHeight, string Extension, string MediaType);

public static class EncodedImageValidator
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static ValidatedImage Validate(
        ReadOnlySpan<byte> encoded,
        long maximumBytes,
        int maximumWidth,
        int maximumHeight)
    {
        if (encoded.Length == 0 || encoded.Length > maximumBytes)
        {
            throw new InvalidDataException("Artwork encoded size is outside supported bounds.");
        }

        ValidatedImage result = encoded.StartsWith(PngSignature)
            ? ValidatePng(encoded, maximumBytes)
            : ValidateJpeg(encoded);
        if (result.PixelWidth is < 1 || result.PixelHeight is < 1 ||
            result.PixelWidth > maximumWidth || result.PixelHeight > maximumHeight ||
            (long)result.PixelWidth * result.PixelHeight > (long)maximumWidth * maximumHeight)
        {
            throw new InvalidDataException("Artwork pixel dimensions are outside supported bounds.");
        }

        return result;
    }

    private static ValidatedImage ValidatePng(ReadOnlySpan<byte> encoded, long maximumBytes)
    {
        if (encoded.Length < 24)
        {
            throw new InvalidDataException("Artwork PNG is truncated.");
        }

        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(encoded.Slice(20, 4)));
        if (!PngImageValidator.IsValid(encoded, width, height, maximumBytes))
        {
            throw new InvalidDataException("Artwork PNG failed structural validation.");
        }

        return new ValidatedImage(width, height, ".png", "image/png");
    }

    private static ValidatedImage ValidateJpeg(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length < 4 || encoded[0] != 0xff || encoded[1] != 0xd8 ||
            encoded[^2] != 0xff || encoded[^1] != 0xd9)
        {
            throw new InvalidDataException("Artwork JPEG signature is invalid.");
        }

        int offset = 2;
        while (offset < encoded.Length - 4)
        {
            if (encoded[offset++] != 0xff)
            {
                throw new InvalidDataException("Artwork JPEG marker stream is invalid.");
            }

            while (offset < encoded.Length && encoded[offset] == 0xff)
            {
                offset++;
            }

            byte marker = encoded[offset++];
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
            {
                continue;
            }

            if (offset > encoded.Length - 2)
            {
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(encoded.Slice(offset, 2));
            if (length < 2 || offset > encoded.Length - length)
            {
                throw new InvalidDataException("Artwork JPEG segment is truncated.");
            }

            if (IsStartOfFrame(marker))
            {
                if (length < 7)
                {
                    throw new InvalidDataException("Artwork JPEG frame is invalid.");
                }

                int height = BinaryPrimitives.ReadUInt16BigEndian(encoded.Slice(offset + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(encoded.Slice(offset + 5, 2));
                return new ValidatedImage(width, height, ".jpg", "image/jpeg");
            }

            offset += length;
        }

        throw new InvalidDataException("Artwork JPEG has no supported frame header.");
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7 or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;
}
