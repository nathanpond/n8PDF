using n8PDF.Images;

namespace n8PDF.Pdf;

/// <summary>An image as it appears in a PDF: an XObject plus the name a page refers to it by.</summary>
public sealed class PdfImage
{
    internal PdfImage(ImageData image, string resourceName)
    {
        Image = image;
        ResourceName = resourceName;
    }

    public ImageData Image { get; }

    public string ResourceName { get; }

    /// <summary>Writes the image's object graph into the document and returns its reference.</summary>
    internal PdfReference Build(PdfDocument document)
    {
        var stream = new PdfStream(Image.Data)
            .Set("Type", "XObject")
            .Set("Subtype", "Image")
            .Set("Width", Image.Width)
            .Set("Height", Image.Height)
            .Set("BitsPerComponent", 8)
            .Set("ColorSpace", ColorSpaceName(Image.ColorSpace));

        if (Image.Encoding == ImageEncoding.Jpeg)
        {
            // The bytes are already JPEG, which is exactly what DCTDecode expects. Compressing
            // them again would only make the file larger.
            ((PdfStream)stream).Compress = false;
            stream.Set("Filter", "DCTDecode");

            // Adobe's CMYK JPEGs are written inverted, and are the only kind in practice.
            if (Image.ColorSpace == ImageColorSpace.Cmyk)
            {
                stream.Set("Decode", new PdfArray()
                    .Add(1).Add(0).Add(1).Add(0).Add(1).Add(0).Add(1).Add(0));
            }
        }

        if (Image.Alpha is { } alpha)
        {
            // PDF keeps transparency in a separate greyscale image rather than as a fourth
            // channel, referenced from the image it applies to.
            var mask = new PdfStream(alpha)
                .Set("Type", "XObject")
                .Set("Subtype", "Image")
                .Set("Width", Image.Width)
                .Set("Height", Image.Height)
                .Set("BitsPerComponent", 8)
                .Set("ColorSpace", "DeviceGray");

            stream.Set("SMask", document.Add(mask));
        }

        return document.Add(stream);
    }

    private static string ColorSpaceName(ImageColorSpace colorSpace) => colorSpace switch
    {
        ImageColorSpace.Gray => "DeviceGray",
        ImageColorSpace.Cmyk => "DeviceCMYK",
        _ => "DeviceRGB"
    };
}
