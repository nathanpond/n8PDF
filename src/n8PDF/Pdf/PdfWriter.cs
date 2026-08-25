using System.Text;

namespace n8PDF.Pdf;

/// <summary>
/// Byte-level emitter for the PDF file body. Tracks its own offset because the cross-reference
/// table records the byte position of every indirect object and the destination stream may not
/// be seekable.
/// </summary>
internal sealed class PdfWriter(Stream stream)
{
    private readonly Stream _stream = stream;

    /// <summary>Number of bytes written so far — the value the xref table is built from.</summary>
    public long Position { get; private set; }

    /// <summary>
    /// When set, every byte written is also fed here — how the PDF/A file identifier is derived
    /// from the body itself (#68), keeping output byte-reproducible. Cleared before the trailer,
    /// which is where the identifier lands.
    /// </summary>
    public System.Security.Cryptography.IncrementalHash? Hash { get; set; }

    public void WriteByte(byte value)
    {
        _stream.WriteByte(value);
        Hash?.AppendData([value]);
        Position++;
    }

    public void WriteBytes(byte[] value)
    {
        _stream.Write(value, 0, value.Length);
        Hash?.AppendData(value);
        Position += value.Length;
    }

    /// <summary>
    /// Writes text that is known to be ASCII. PDF syntax outside of string and stream objects is
    /// entirely ASCII, so this is the right encoding for every structural token.
    /// </summary>
    public void WriteAscii(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteBytes(bytes);
    }

    public void WriteLine(string value = "")
    {
        WriteAscii(value);
        WriteByte((byte)'\n');
    }
}
