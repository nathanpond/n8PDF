namespace n8PDF.Images;

/// <summary>
/// Turns the bytes of an image part into something a PDF can carry.
/// </summary>
/// <remarks>
/// JPEG is handled by reading its header and passing the file through untouched: PDF's DCTDecode
/// filter is JPEG, so decoding and re-encoding would cost quality for nothing. Everything else
/// has to be unpacked to samples.
/// </remarks>
public static class ImageReader
{
    public static bool IsSupported(byte[] data) => PngDecoder.IsPng(data) || IsJpeg(data);

    public static ImageData Read(byte[] data)
    {
        if (PngDecoder.IsPng(data)) return PngDecoder.Decode(data);
        if (IsJpeg(data)) return ReadJpeg(data);

        throw new ImageFormatException(
            "Unsupported image format. PNG and JPEG are handled; GIF, BMP, TIFF and EMF are not.");
    }

    /// <summary>Reads an image if the format is one we handle, and returns null otherwise.</summary>
    public static ImageData? TryRead(byte[] data)
    {
        try
        {
            return IsSupported(data) ? Read(data) : null;
        }
        catch (ImageFormatException)
        {
            // A malformed image should cost its own placement, not the whole conversion.
            return null;
        }
    }

    private static bool IsJpeg(byte[] data) =>
        data.Length > 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;

    /// <summary>
    /// Reads a JPEG's dimensions and component count from its start-of-frame marker.
    /// </summary>
    /// <remarks>
    /// A JPEG is a sequence of marker segments. Only the frame header is of interest; it carries
    /// the size and the number of components, which is what decides the PDF colour space. Every
    /// other segment is skipped by its declared length.
    /// </remarks>
    private static ImageData ReadJpeg(byte[] data)
    {
        var position = 2;

        while (position + 3 < data.Length)
        {
            if (data[position] != 0xff)
            {
                position++;
                continue;
            }

            var marker = data[position + 1];
            position += 2;

            // Standalone markers carry no payload.
            if (marker is 0xd8 or 0x01 || marker is >= 0xd0 and <= 0xd7) continue;

            if (position + 1 >= data.Length) break;
            var length = (data[position] << 8) | data[position + 1];

            // SOF0 through SOF15, excluding the two that are not frame headers.
            if (marker is >= 0xc0 and <= 0xcf && marker != 0xc4 && marker != 0xc8 && marker != 0xcc)
            {
                if (position + 7 >= data.Length) break;

                var height = (data[position + 3] << 8) | data[position + 4];
                var width = (data[position + 5] << 8) | data[position + 6];
                var components = data[position + 7];

                var colorSpace = components switch
                {
                    1 => ImageColorSpace.Gray,
                    4 => ImageColorSpace.Cmyk,
                    _ => ImageColorSpace.Rgb
                };

                if (width <= 0 || height <= 0)
                    throw new ImageFormatException("JPEG frame header declares an empty image.");

                return new ImageData(width, height, data, ImageEncoding.Jpeg, colorSpace);
            }

            position += length;
        }

        throw new ImageFormatException("JPEG has no frame header.");
    }
}
