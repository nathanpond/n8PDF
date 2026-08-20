using System.Globalization;
using System.Text;

namespace n8PDF.Pdf;

/// <summary>
/// Base of the PDF object model (ISO 32000-1 §7.3). Every value that can appear in a PDF
/// file body derives from this and knows how to serialise itself.
/// </summary>
internal abstract class PdfObject
{
    internal abstract void Write(PdfWriter writer);
}

/// <summary>The <c>null</c> object.</summary>
internal sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();

    private PdfNull() { }

    internal override void Write(PdfWriter writer) => writer.WriteAscii("null");
}

internal sealed class PdfBoolean : PdfObject
{
    public static readonly PdfBoolean True = new(true);
    public static readonly PdfBoolean False = new(false);

    public bool Value { get; }

    private PdfBoolean(bool value) => Value = value;

    public static PdfBoolean Of(bool value) => value ? True : False;

    internal override void Write(PdfWriter writer) => writer.WriteAscii(Value ? "true" : "false");
}

/// <summary>
/// A numeric object. PDF has no exponent notation, so reals are always written in plain
/// decimal form with the invariant culture's '.' separator.
/// </summary>
internal sealed class PdfNumber : PdfObject
{
    public double Value { get; }
    private readonly bool _isInteger;

    public PdfNumber(double value)
    {
        Value = value;
        _isInteger = false;
    }

    public PdfNumber(int value)
    {
        Value = value;
        _isInteger = true;
    }

    internal override void Write(PdfWriter writer) => writer.WriteAscii(Format(Value, _isInteger));

    internal static string Format(double value, bool isInteger = false)
    {
        if (isInteger || value == Math.Floor(value) && Math.Abs(value) < 1e15)
            return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

        // Five decimals is well past the precision any consumer resolves, and keeps
        // content streams from bloating with meaningless digits.
        var text = value.ToString("0.#####", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
}

/// <summary>A name object such as <c>/Type</c>. Irregular characters are #-escaped.</summary>
internal sealed class PdfName : PdfObject
{
    public string Value { get; }

    public PdfName(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    internal override void Write(PdfWriter writer)
    {
        var sb = new StringBuilder("/");
        foreach (var ch in Value)
        {
            // Regular characters are the printable ASCII range minus delimiters and '#'.
            if (ch is > (char)0x20 and < (char)0x7f && ch != '#' && !IsDelimiter(ch))
                sb.Append(ch);
            else
                sb.Append('#').Append(((int)ch & 0xff).ToString("X2", CultureInfo.InvariantCulture));
        }

        writer.WriteAscii(sb.ToString());
    }

    private static bool IsDelimiter(char ch) =>
        ch is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';
}

/// <summary>
/// A string object. Written as a hex string when it contains bytes that would need heavy
/// escaping, and as a literal string otherwise.
/// </summary>
internal sealed class PdfString : PdfObject
{
    public byte[] Bytes { get; }
    public bool ForceHex { get; }

    public PdfString(byte[] bytes, bool forceHex = false)
    {
        Bytes = bytes;
        ForceHex = forceHex;
    }

    /// <summary>Creates a text string, using UTF-16BE with a byte-order mark when non-ASCII.</summary>
    public static PdfString FromText(string text)
    {
        var ascii = true;
        foreach (var ch in text)
        {
            if (ch > 0x7e || ch < 0x20)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
            return new PdfString(Encoding.ASCII.GetBytes(text));

        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var withBom = new byte[utf16.Length + 2];
        withBom[0] = 0xfe;
        withBom[1] = 0xff;
        Array.Copy(utf16, 0, withBom, 2, utf16.Length);
        return new PdfString(withBom, forceHex: true);
    }

    internal override void Write(PdfWriter writer)
    {
        if (ForceHex || NeedsHex())
        {
            var sb = new StringBuilder(Bytes.Length * 2 + 2);
            sb.Append('<');
            foreach (var b in Bytes)
                sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            sb.Append('>');
            writer.WriteAscii(sb.ToString());
            return;
        }

        writer.WriteByte((byte)'(');
        foreach (var b in Bytes)
        {
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    writer.WriteByte((byte)'\\');
                    writer.WriteByte(b);
                    break;
                case (byte)'\n':
                    writer.WriteAscii("\\n");
                    break;
                case (byte)'\r':
                    writer.WriteAscii("\\r");
                    break;
                case (byte)'\t':
                    writer.WriteAscii("\\t");
                    break;
                default:
                    writer.WriteByte(b);
                    break;
            }
        }

        writer.WriteByte((byte)')');
    }

    private bool NeedsHex()
    {
        foreach (var b in Bytes)
        {
            if (b < 0x20 && b is not ((byte)'\n' or (byte)'\r' or (byte)'\t') || b > 0x7e)
                return true;
        }

        return false;
    }
}

internal sealed class PdfArray : PdfObject
{
    private readonly List<PdfObject> _items = [];

    public PdfArray() { }

    public PdfArray(IEnumerable<PdfObject> items) => _items.AddRange(items);

    public int Count => _items.Count;

    public PdfObject this[int index] => _items[index];

    public PdfArray Add(PdfObject item)
    {
        _items.Add(item);
        return this;
    }

    public PdfArray Add(double value) => Add(new PdfNumber(value));

    public PdfArray Add(int value) => Add(new PdfNumber(value));

    internal override void Write(PdfWriter writer)
    {
        writer.WriteByte((byte)'[');
        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0) writer.WriteByte((byte)' ');
            _items[i].Write(writer);
        }

        writer.WriteByte((byte)']');
    }
}

internal class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries = [];
    private readonly List<string> _order = [];

    public int Count => _entries.Count;

    public PdfObject? this[string key]
    {
        get => _entries.GetValueOrDefault(key);
        set
        {
            if (value is null)
            {
                if (_entries.Remove(key)) _order.Remove(key);
                return;
            }

            if (!_entries.ContainsKey(key)) _order.Add(key);
            _entries[key] = value;
        }
    }

    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public PdfDictionary Set(string key, PdfObject value)
    {
        this[key] = value;
        return this;
    }

    public PdfDictionary Set(string key, string name) => Set(key, new PdfName(name));

    public PdfDictionary Set(string key, double value) => Set(key, new PdfNumber(value));

    public PdfDictionary Set(string key, int value) => Set(key, new PdfNumber(value));

    internal override void Write(PdfWriter writer)
    {
        writer.WriteAscii("<<");
        foreach (var key in _order)
        {
            new PdfName(key).Write(writer);
            writer.WriteByte((byte)' ');
            _entries[key].Write(writer);
        }

        writer.WriteAscii(">>");
    }
}

/// <summary>
/// A stream object: a dictionary followed by raw data. Data is Flate-compressed on write
/// unless suppressed, which matters for already-compressed payloads such as JPEG images.
/// </summary>
internal sealed class PdfStream : PdfDictionary
{
    public byte[] Data { get; set; }

    /// <summary>When false the data is written verbatim and no /Filter is added by us.</summary>
    public bool Compress { get; set; } = true;

    public PdfStream(byte[] data) => Data = data;

    public PdfStream() => Data = [];

    internal void WriteStream(PdfWriter writer)
    {
        var payload = Data;
        if (Compress && Data.Length > 0)
        {
            var compressed = PdfFilters.FlateEncode(Data);
            // Tiny payloads can grow under deflate; only take the win when there is one.
            if (compressed.Length < Data.Length)
            {
                payload = compressed;
                AppendFilter("FlateDecode");
            }
        }

        this["Length"] = new PdfNumber(payload.Length);

        base.Write(writer);
        writer.WriteAscii("\nstream\n");
        writer.WriteBytes(payload);
        writer.WriteAscii("\nendstream");
    }

    private void AppendFilter(string filterName)
    {
        switch (this["Filter"])
        {
            case null:
                this["Filter"] = new PdfName(filterName);
                break;
            case PdfName existing:
                // Filters apply in array order, and ours runs last over the existing bytes.
                this["Filter"] = new PdfArray().Add(existing).Add(new PdfName(filterName));
                break;
            case PdfArray array:
                array.Add(new PdfName(filterName));
                break;
        }
    }

    internal override void Write(PdfWriter writer) => WriteStream(writer);
}

/// <summary>An indirect reference, written as <c>n g R</c>.</summary>
internal sealed class PdfReference : PdfObject
{
    public int ObjectNumber { get; }
    public int Generation { get; }

    internal PdfReference(int objectNumber, int generation = 0)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    internal override void Write(PdfWriter writer) =>
        writer.WriteAscii($"{ObjectNumber} {Generation} R");
}
