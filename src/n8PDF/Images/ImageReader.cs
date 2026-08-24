namespace n8PDF.Images;

/// <summary>
/// Turns the bytes of an image part into something a PDF can carry.
/// </summary>
/// <remarks>
/// JPEG is handled by reading its header and passing the file through untouched: PDF's DCTDecode
/// filter is JPEG, so decoding and re-encoding would cost quality for nothing. Everything else —
/// PNG, GIF, BMP and TIFF — has to be unpacked to samples, which is what the decoders beside this
/// do. A metafile is not pixels at all but the record of a drawing being made, and is kept as one:
/// its commands are read here and written out again as the PDF's own.
/// </remarks>
internal static class ImageReader
{
    public static bool IsSupported(byte[] data) =>
        PngDecoder.IsPng(data) || IsJpeg(data) || GifDecoder.IsGif(data) ||
        BmpDecoder.IsBmp(data) || TiffDecoder.IsTiff(data) || EmfDecoder.IsEmf(data);

    /// <param name="maximumPixels">
    /// What a picture may decode to, width times height. Carried down to whichever decoder reads
    /// the header, because that is where the dimensions are known and where the memory is asked
    /// for. See <see cref="ImageLimits"/>.
    /// </param>
    /// <param name="nesting">
    /// How many pictures this one is already inside. Zero for a part of the document; one more
    /// each time a decoder finds a whole image file within the one it is reading and hands it
    /// back here. See <see cref="ImageLimits.MaximumNesting"/>.
    /// </param>
    public static ImageData Read(
        byte[] data, long maximumPixels = ImageLimits.DefaultMaximumPixels, int nesting = 0)
    {
        // Before anything is looked at, because what is being refused is the looking: a picture
        // that holds itself is read again at every level, and it is the stack that gives out.
        if (nesting > ImageLimits.MaximumNesting)
        {
            throw new ImageFormatException(
                $"An image is nested more than {ImageLimits.MaximumNesting} deep inside another.");
        }

        if (PngDecoder.IsPng(data)) return PngDecoder.Decode(data, maximumPixels);
        if (IsJpeg(data)) return ReadJpeg(data, maximumPixels);
        if (GifDecoder.IsGif(data)) return GifDecoder.Decode(data, maximumPixels);
        if (BmpDecoder.IsBmp(data)) return BmpDecoder.Decode(data, maximumPixels);
        if (TiffDecoder.IsTiff(data)) return TiffDecoder.Decode(data, maximumPixels, nesting);
        if (EmfDecoder.IsEmf(data)) return EmfDecoder.Decode(data, maximumPixels, nesting);

        throw new ImageFormatException("Unsupported image format.");
    }

    /// <summary>Reads an image if the format is one we handle, and returns null otherwise.</summary>
    public static ImageData? TryRead(
        byte[] data, long maximumPixels = ImageLimits.DefaultMaximumPixels, int nesting = 0)
    {
        try
        {
            return IsSupported(data) ? Read(data, maximumPixels, nesting) : null;
        }
        catch (Exception e) when (e is ImageFormatException
            or IndexOutOfRangeException or ArgumentException or OverflowException
            or DivideByZeroException or InvalidDataException)
        {
            // A malformed image should cost its own placement, not the whole conversion — and
            // that sentence has to hold for the files the decoders did not think to refuse, not
            // only the ones they did (#48). The types here are exactly what the audit reproduced
            // escaping from crafted files of a few dozen bytes; each hole is filed as its own
            // issue with its own validation fix, tested at the decoder level where this net
            // cannot swallow the evidence, and this is the defence in depth behind those fixes
            // rather than a substitute for any of them. OutOfMemoryException is deliberately not
            // here: that one means the process is in trouble, and hiding it helps nobody.
            _ = e;
            return null;
        }
    }

    private static bool IsJpeg(byte[] data) =>
        data.Length > 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;

    /// <summary>
    /// Reads a JPEG's dimensions and component count from its start-of-frame marker.
    /// </summary>
    /// <remarks>
    /// A JPEG is a sequence of marker segments. Two are of interest. The frame header carries the
    /// size and the number of channels, which is what decides the PDF colour space; and the marker
    /// Adobe's tools write says, of a file of four channels, that its ink is written the other way
    /// up. Every other segment is skipped by its declared length.
    /// </remarks>
    private static ImageData ReadJpeg(byte[] data, long maximumPixels)
    {
        var position = 2;
        var adobe = false;

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

            if (marker == 0xee && position + 6 < data.Length &&
                data.AsSpan(position + 2, 5).SequenceEqual("Adobe"u8))
            {
                adobe = true;
            }

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

                // Nothing here decodes it — the compressed data goes into the PDF as it stands —
                // but what a viewer will decode is the same picture, so it takes the same limit.
                ImageLimits.Check(width, height, maximumPixels, "JPEG");

                return new ImageData(width, height, data, ImageEncoding.Jpeg, colorSpace)
                {
                    InvertedInk = adobe && components == 4
                };
            }

            position += length;
        }

        throw new ImageFormatException("JPEG has no frame header.");
    }
}
