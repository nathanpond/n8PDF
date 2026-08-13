namespace n8PDF.Images;

/// <summary>How an image's bytes should be handed to the PDF.</summary>
public enum ImageEncoding
{
    /// <summary>Raw samples, to be Flate-compressed on the way out.</summary>
    Raw,

    /// <summary>Already JPEG; the bytes go straight through with a DCTDecode filter.</summary>
    Jpeg
}

public enum ImageColorSpace
{
    Gray,
    Rgb,

    /// <summary>Four-component JPEG. Rare, and usually wants the decode array inverted.</summary>
    Cmyk
}

/// <summary>
/// An image ready to be written into a PDF.
/// </summary>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="Data">
/// Either raw interleaved samples at 8 bits per component, or the original JPEG bytes.
/// </param>
/// <param name="Alpha">
/// One byte of opacity per pixel, or null when the image is fully opaque. PDF carries
/// transparency as a separate soft mask rather than as a fourth channel.
/// </param>
public sealed record ImageData(
    int Width,
    int Height,
    byte[] Data,
    ImageEncoding Encoding,
    ImageColorSpace ColorSpace,
    byte[]? Alpha = null)
{
    /// <summary>
    /// The commands this picture is drawn with, where it is a drawing rather than pixels. A
    /// metafile keeps its commands all the way to the PDF, which has commands of its own to write
    /// them out as.
    /// </summary>
    public VectorDrawing? Drawing { get; init; }

    public bool IsDrawing => Drawing is not null;

    public int ComponentCount => ColorSpace switch
    {
        ImageColorSpace.Gray => 1,
        ImageColorSpace.Cmyk => 4,
        _ => 3
    };

    public bool HasAlpha => Alpha is not null;
}

/// <summary>Raised when image data cannot be decoded.</summary>
public sealed class ImageFormatException(string message) : Exception(message);
