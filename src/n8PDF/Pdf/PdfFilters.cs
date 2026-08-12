using System.IO.Compression;

namespace n8PDF.Pdf;

/// <summary>Stream filters. FlateDecode is the only one we produce ourselves.</summary>
internal static class PdfFilters
{
    /// <summary>
    /// Compresses with zlib. PDF's /FlateDecode expects a zlib wrapper (RFC 1950) around the
    /// deflate data, not a bare deflate stream, which is exactly what ZLibStream produces.
    /// </summary>
    public static byte[] FlateEncode(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    public static byte[] FlateDecode(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }
}
