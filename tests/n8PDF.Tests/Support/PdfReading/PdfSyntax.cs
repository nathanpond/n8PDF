using System.Globalization;
using System.Text;

namespace n8PDF.Tests.Support.PdfReading;

/// <summary>Base of the value model produced when reading a PDF.</summary>
/// <remarks>
/// This is a reader, deliberately separate from the writer's model in <c>n8PDF.Pdf</c>. Sharing
/// one model between the two would let a misunderstanding in the writer be mirrored by the same
/// misunderstanding in the reader, and the comparison would agree with itself. This half is also
/// test-only: the library converts DOCX to PDF and has no reason to read one.
/// </remarks>
public abstract record PdfValue;

public sealed record PdfNullValue : PdfValue
{
    public static readonly PdfNullValue Instance = new();
}

public sealed record PdfBoolValue(bool Value) : PdfValue;

public sealed record PdfNumberValue(double Value) : PdfValue
{
    public int AsInt => (int)Math.Round(Value);
}

public sealed record PdfNameValue(string Name) : PdfValue;

/// <summary>A string object. Raw bytes are kept because text strings are not always ASCII.</summary>
public sealed record PdfStringValue(byte[] Bytes) : PdfValue
{
    public string AsLatin1 => Encoding.Latin1.GetString(Bytes);
}

public sealed record PdfArrayValue(IReadOnlyList<PdfValue> Items) : PdfValue
{
    public PdfValue this[int index] => Items[index];

    public int Count => Items.Count;
}

public sealed record PdfDictValue(IReadOnlyDictionary<string, PdfValue> Entries) : PdfValue
{
    public PdfValue? Get(string key) => Entries.GetValueOrDefault(key);

    public bool Has(string key) => Entries.ContainsKey(key);
}

/// <summary>A stream: its dictionary plus the still-encoded bytes.</summary>
public sealed record PdfStreamValue(PdfDictValue Dictionary, byte[] RawData) : PdfValue;

/// <summary>An indirect reference, resolved through the file's object table.</summary>
public sealed record PdfRefValue(int ObjectNumber, int Generation) : PdfValue;

/// <summary>An operator token, which only appears inside content streams.</summary>
public sealed record PdfOperatorValue(string Operator) : PdfValue;

/// <summary>Raised when a PDF cannot be parsed.</summary>
public sealed class PdfParseException(string message) : Exception(message);

/// <summary>
/// Tokenises and parses PDF syntax. The same grammar serves both file bodies and content
/// streams; the only difference is that content streams also carry bare operator tokens.
/// </summary>
public sealed class PdfParser(byte[] data, int position = 0)
{
    private readonly byte[] _data = data;

    public int Position { get; set; } = position;

    public int Length => _data.Length;

    public bool AtEnd => Position >= _data.Length;

    /// <summary>
    /// Reads the next value. Operators are returned as <see cref="PdfOperatorValue"/> so that a
    /// content-stream interpreter can drive off the same parser.
    /// </summary>
    public PdfValue? ReadValue()
    {
        SkipWhitespaceAndComments();
        if (AtEnd) return null;

        var b = _data[Position];

        switch (b)
        {
            case (byte)'/':
                return ReadName();
            case (byte)'(':
                return ReadLiteralString();
            case (byte)'[':
                return ReadArray();
            case (byte)']':
                Position++;
                return new PdfOperatorValue("]");
            case (byte)'<':
                // "<<" opens a dictionary; a single "<" opens a hex string.
                return Position + 1 < _data.Length && _data[Position + 1] == '<'
                    ? ReadDictionaryOrStream()
                    : ReadHexString();
            case (byte)'>':
                // A stray ">>" terminates a dictionary being scanned by a caller.
                Position += Position + 1 < _data.Length && _data[Position + 1] == '>' ? 2 : 1;
                return new PdfOperatorValue(">>");
            case (byte)'{':
            case (byte)'}':
                Position++;
                return new PdfOperatorValue(((char)b).ToString());
        }

        if (b == '+' || b == '-' || b == '.' || (b >= '0' && b <= '9'))
            return ReadNumberOrReference();

        return ReadKeyword();
    }

    /// <summary>Reads every remaining value, for content-stream interpretation.</summary>
    public List<PdfValue> ReadAll()
    {
        var values = new List<PdfValue>();
        while (ReadValue() is { } value) values.Add(value);
        return values;
    }

    private PdfValue ReadName()
    {
        Position++; // '/'
        var sb = new StringBuilder();

        while (!AtEnd)
        {
            var b = _data[Position];
            if (IsDelimiter(b) || IsWhitespace(b)) break;

            if (b == '#' && Position + 2 < _data.Length)
            {
                // Irregular characters are escaped as #xx.
                var hex = Encoding.ASCII.GetString(_data, Position + 1, 2);
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    sb.Append((char)code);
                    Position += 3;
                    continue;
                }
            }

            sb.Append((char)b);
            Position++;
        }

        return new PdfNameValue(sb.ToString());
    }

    private PdfValue ReadLiteralString()
    {
        Position++; // '('
        var bytes = new List<byte>();
        var depth = 1;

        while (!AtEnd)
        {
            var b = _data[Position++];

            if (b == '\\')
            {
                if (AtEnd) break;
                var escape = _data[Position++];

                switch (escape)
                {
                    case (byte)'n': bytes.Add((byte)'\n'); break;
                    case (byte)'r': bytes.Add((byte)'\r'); break;
                    case (byte)'t': bytes.Add((byte)'\t'); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case (byte)'(': bytes.Add((byte)'('); break;
                    case (byte)')': bytes.Add((byte)')'); break;
                    case (byte)'\\': bytes.Add((byte)'\\'); break;
                    case (byte)'\n': break;            // line continuation
                    case (byte)'\r':
                        if (!AtEnd && _data[Position] == '\n') Position++;
                        break;
                    default:
                        if (escape >= '0' && escape <= '7')
                        {
                            // Up to three octal digits.
                            var value = escape - '0';
                            for (var i = 0; i < 2 && !AtEnd && _data[Position] >= '0' && _data[Position] <= '7'; i++)
                                value = value * 8 + (_data[Position++] - '0');

                            bytes.Add((byte)value);
                        }
                        else
                        {
                            bytes.Add(escape);
                        }

                        break;
                }

                continue;
            }

            if (b == '(') depth++;
            if (b == ')')
            {
                depth--;
                if (depth == 0) break;
            }

            bytes.Add(b);
        }

        return new PdfStringValue([.. bytes]);
    }

    private PdfValue ReadHexString()
    {
        Position++; // '<'
        var bytes = new List<byte>();
        var digits = new List<char>();

        while (!AtEnd)
        {
            var b = _data[Position++];
            if (b == '>') break;
            if (IsWhitespace(b)) continue;

            digits.Add((char)b);
        }

        // An odd number of digits is padded with a trailing zero.
        if (digits.Count % 2 != 0) digits.Add('0');

        for (var i = 0; i < digits.Count; i += 2)
        {
            if (int.TryParse($"{digits[i]}{digits[i + 1]}", NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var value))
            {
                bytes.Add((byte)value);
            }
        }

        return new PdfStringValue([.. bytes]);
    }

    private PdfValue ReadArray()
    {
        Position++; // '['
        var items = new List<PdfValue>();

        while (true)
        {
            SkipWhitespaceAndComments();
            if (AtEnd) break;
            if (_data[Position] == ']')
            {
                Position++;
                break;
            }

            var value = ReadValue();
            if (value is null) break;
            if (value is PdfOperatorValue { Operator: "]" }) break;

            items.Add(value);
        }

        return new PdfArrayValue(items);
    }

    private PdfValue ReadDictionaryOrStream()
    {
        Position += 2; // '<<'
        var entries = new Dictionary<string, PdfValue>(StringComparer.Ordinal);

        while (true)
        {
            SkipWhitespaceAndComments();
            if (AtEnd) break;

            if (_data[Position] == '>')
            {
                Position += Position + 1 < _data.Length && _data[Position + 1] == '>' ? 2 : 1;
                break;
            }

            if (_data[Position] != '/')
            {
                // Malformed key; skip a token so we cannot loop forever.
                if (ReadValue() is null) break;
                continue;
            }

            var key = ((PdfNameValue)ReadName()).Name;
            var value = ReadValue();
            if (value is null) break;

            entries[key] = value;
        }

        var dictionary = new PdfDictValue(entries);

        // A dictionary followed by the "stream" keyword introduces stream data.
        var save = Position;
        SkipWhitespaceAndComments();

        if (Matches("stream"))
        {
            Position += "stream".Length;

            // The keyword is followed by CRLF or LF, but never by CR alone.
            if (!AtEnd && _data[Position] == '\r') Position++;
            if (!AtEnd && _data[Position] == '\n') Position++;

            var start = Position;
            var length = ResolveStreamLength(dictionary, start);

            var raw = new byte[length];
            Array.Copy(_data, start, raw, 0, length);
            Position = start + length;

            SkipWhitespaceAndComments();
            if (Matches("endstream")) Position += "endstream".Length;

            return new PdfStreamValue(dictionary, raw);
        }

        Position = save;
        return dictionary;
    }

    /// <summary>
    /// Determines a stream's length. The /Length entry is authoritative when it is a direct
    /// number, but it is often an indirect reference that cannot be resolved from here, and some
    /// producers write it wrongly — so the data is bounded by searching for "endstream" when
    /// necessary.
    /// </summary>
    private int ResolveStreamLength(PdfDictValue dictionary, int start)
    {
        if (dictionary.Get("Length") is PdfNumberValue number)
        {
            var declared = number.AsInt;
            if (declared >= 0 && start + declared <= _data.Length)
            {
                // Trust it only if "endstream" really follows.
                var after = start + declared;
                var probe = new PdfParser(_data, after);
                probe.SkipWhitespaceAndComments();
                if (probe.Matches("endstream")) return declared;
            }
        }

        var index = IndexOf("endstream", start);
        if (index < 0) return Math.Max(0, _data.Length - start);

        var end = index;

        // Back off the end-of-line that precedes the keyword.
        if (end > start && _data[end - 1] == '\n') end--;
        if (end > start && _data[end - 1] == '\r') end--;

        return end - start;
    }

    private PdfValue ReadNumberOrReference()
    {
        var first = ReadNumber();

        // "n g R" is an indirect reference. Both numbers must be non-negative integers, so a
        // negative or fractional value rules it out without any lookahead.
        if (first.Value >= 0 && first.Value == Math.Floor(first.Value))
        {
            var save = Position;
            SkipWhitespaceAndComments();

            if (!AtEnd && _data[Position] >= '0' && _data[Position] <= '9')
            {
                var second = ReadNumber();
                SkipWhitespaceAndComments();

                if (!AtEnd && _data[Position] == 'R' &&
                    (Position + 1 >= _data.Length || IsWhitespace(_data[Position + 1]) || IsDelimiter(_data[Position + 1])))
                {
                    Position++;
                    return new PdfRefValue((int)first.Value, (int)second.Value);
                }
            }

            Position = save;
        }

        return first;
    }

    private PdfNumberValue ReadNumber()
    {
        var start = Position;
        if (!AtEnd && (_data[Position] == '+' || _data[Position] == '-')) Position++;

        while (!AtEnd && ((_data[Position] >= '0' && _data[Position] <= '9') || _data[Position] == '.' ||
                          _data[Position] == '-' || _data[Position] == '+'))
        {
            Position++;
        }

        var text = Encoding.ASCII.GetString(_data, start, Position - start);
        return new PdfNumberValue(
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0);
    }

    private PdfValue ReadKeyword()
    {
        var start = Position;
        while (!AtEnd && !IsWhitespace(_data[Position]) && !IsDelimiter(_data[Position])) Position++;

        if (Position == start)
        {
            // Not a legal token start; consume one byte so parsing always advances.
            Position++;
            return new PdfOperatorValue(((char)_data[start]).ToString());
        }

        var keyword = Encoding.ASCII.GetString(_data, start, Position - start);
        return keyword switch
        {
            "true" => new PdfBoolValue(true),
            "false" => new PdfBoolValue(false),
            "null" => PdfNullValue.Instance,
            _ => new PdfOperatorValue(keyword)
        };
    }

    public void SkipWhitespaceAndComments()
    {
        while (!AtEnd)
        {
            var b = _data[Position];

            if (IsWhitespace(b))
            {
                Position++;
                continue;
            }

            if (b == '%')
            {
                while (!AtEnd && _data[Position] != '\n' && _data[Position] != '\r') Position++;
                continue;
            }

            break;
        }
    }

    public bool Matches(string keyword)
    {
        if (Position + keyword.Length > _data.Length) return false;

        for (var i = 0; i < keyword.Length; i++)
        {
            if (_data[Position + i] != keyword[i]) return false;
        }

        return true;
    }

    public int IndexOf(string needle, int from)
    {
        var bytes = Encoding.ASCII.GetBytes(needle);

        for (var i = Math.Max(0, from); i <= _data.Length - bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < bytes.Length; j++)
            {
                if (_data[i + j] == bytes[j]) continue;
                match = false;
                break;
            }

            if (match) return i;
        }

        return -1;
    }

    public int LastIndexOf(string needle)
    {
        var bytes = Encoding.ASCII.GetBytes(needle);

        for (var i = _data.Length - bytes.Length; i >= 0; i--)
        {
            var match = true;
            for (var j = 0; j < bytes.Length; j++)
            {
                if (_data[i + j] == bytes[j]) continue;
                match = false;
                break;
            }

            if (match) return i;
        }

        return -1;
    }

    public static bool IsWhitespace(byte b) =>
        b is 0 or 9 or 10 or 12 or 13 or 32;

    public static bool IsDelimiter(byte b) =>
        b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
            or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
}
