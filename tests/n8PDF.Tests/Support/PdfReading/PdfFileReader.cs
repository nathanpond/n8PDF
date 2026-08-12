using System.Text;
using n8PDF.Pdf;

namespace n8PDF.Tests.Support.PdfReading;

/// <summary>A page found in the page tree, with everything needed to render its text.</summary>
public sealed record PdfPageInfo(
    int Index,
    double Width,
    double Height,
    PdfDictValue Dictionary,
    PdfDictValue? Resources);

/// <summary>
/// Reads a PDF file: cross-reference table, indirect objects, stream decoding and the page tree.
/// </summary>
/// <remarks>
/// Scoped to what the documents under test actually use. Word for Mac and n8PDF both write a
/// classic cross-reference table, so cross-reference streams and object streams are not
/// implemented — but rather than mis-parsing such a file silently, the reader falls back to
/// scanning the whole file for "N G obj" headers, which handles any layout at the cost of speed.
/// </remarks>
public sealed class PdfFileReader
{
    private readonly byte[] _data;
    private readonly Dictionary<int, int> _objectOffsets = [];
    private readonly Dictionary<int, PdfValue> _cache = [];
    private PdfDictValue? _trailer;

    public PdfFileReader(byte[] data)
    {
        _data = data;
        LoadCrossReferences();
    }

    public static PdfFileReader FromFile(string path) => new(File.ReadAllBytes(path));

    public PdfDictValue? Trailer => _trailer;

    public int KnownObjectCount => _objectOffsets.Count;

    /// <summary>Resolves indirect references until a direct value is reached.</summary>
    public PdfValue Resolve(PdfValue? value)
    {
        var guard = 0;

        while (value is PdfRefValue reference && guard++ < 64)
            value = GetObject(reference.ObjectNumber);

        return value ?? PdfNullValue.Instance;
    }

    public PdfValue GetObject(int objectNumber)
    {
        if (_cache.TryGetValue(objectNumber, out var cached)) return cached;
        if (!_objectOffsets.TryGetValue(objectNumber, out var offset)) return PdfNullValue.Instance;

        var parser = new PdfParser(_data, offset);

        // The object header is "N G obj"; skip the two numbers and the keyword.
        parser.ReadValue();
        parser.ReadValue();
        parser.SkipWhitespaceAndComments();
        if (parser.Matches("obj")) parser.Position += 3;

        var value = parser.ReadValue() ?? PdfNullValue.Instance;
        _cache[objectNumber] = value;
        return value;
    }

    /// <summary>Looks up a dictionary entry, resolving it if it is a reference.</summary>
    public PdfValue? GetEntry(PdfDictValue? dictionary, string key)
    {
        var value = dictionary?.Get(key);
        return value is null ? null : Resolve(value);
    }

    /// <summary>Decodes a stream's data, applying the filters it declares.</summary>
    public byte[] DecodeStream(PdfStreamValue stream)
    {
        var data = stream.RawData;
        var filters = new List<string>();

        switch (Resolve(stream.Dictionary.Get("Filter")))
        {
            case PdfNameValue name:
                filters.Add(name.Name);
                break;
            case PdfArrayValue array:
                foreach (var item in array.Items)
                {
                    if (Resolve(item) is PdfNameValue filterName) filters.Add(filterName.Name);
                }

                break;
        }

        foreach (var filter in filters)
        {
            switch (filter)
            {
                case "FlateDecode":
                case "Fl":
                    data = FlateDecodeTolerant(data);
                    break;

                case "ASCIIHexDecode":
                case "AHx":
                    data = AsciiHexDecode(data);
                    break;

                // Image filters carry no text, so leaving the data encoded is harmless here.
                case "DCTDecode":
                case "JPXDecode":
                case "CCITTFaxDecode":
                case "JBIG2Decode":
                    return [];

                default:
                    throw new PdfParseException($"Unsupported stream filter '{filter}'.");
            }
        }

        // A predictor is applied after decompression, and is common on cross-reference streams.
        if (Resolve(stream.Dictionary.Get("DecodeParms")) is PdfDictValue parms &&
            Resolve(parms.Get("Predictor")) is PdfNumberValue predictor && predictor.AsInt >= 10)
        {
            var columns = Resolve(parms.Get("Columns")) is PdfNumberValue c ? c.AsInt : 1;
            var colors = Resolve(parms.Get("Colors")) is PdfNumberValue co ? co.AsInt : 1;
            var bits = Resolve(parms.Get("BitsPerComponent")) is PdfNumberValue b ? b.AsInt : 8;
            data = UndoPngPredictor(data, columns, colors, bits);
        }

        return data;
    }

    /// <summary>Concatenates a page's content streams, which may be a single stream or an array.</summary>
    public byte[] GetPageContent(PdfPageInfo page)
    {
        var contents = GetEntry(page.Dictionary, "Contents");
        var buffer = new MemoryStream();

        switch (contents)
        {
            case PdfStreamValue stream:
                var single = DecodeStream(stream);
                buffer.Write(single, 0, single.Length);
                break;

            case PdfArrayValue array:
                foreach (var item in array.Items)
                {
                    if (Resolve(item) is not PdfStreamValue part) continue;

                    var bytes = DecodeStream(part);
                    buffer.Write(bytes, 0, bytes.Length);

                    // Streams are concatenated as if separated by whitespace, so a token cannot
                    // straddle the boundary.
                    buffer.WriteByte((byte)'\n');
                }

                break;
        }

        return buffer.ToArray();
    }

    /// <summary>Walks the page tree in order.</summary>
    public List<PdfPageInfo> GetPages()
    {
        var pages = new List<PdfPageInfo>();

        var root = GetEntry(_trailer, "Root") as PdfDictValue;
        var pagesNode = GetEntry(root, "Pages") as PdfDictValue;

        if (pagesNode is null)
        {
            // No usable catalog: fall back to every object that calls itself a page.
            foreach (var objectNumber in _objectOffsets.Keys.OrderBy(n => n))
            {
                if (GetObject(objectNumber) is PdfDictValue dictionary &&
                    Resolve(dictionary.Get("Type")) is PdfNameValue { Name: "Page" })
                {
                    AddPage(pages, dictionary, null, null);
                }
            }

            return pages;
        }

        Walk(pagesNode, null, null, 0);
        return pages;

        void Walk(PdfDictValue node, PdfValue? inheritedResources, PdfValue? inheritedMediaBox, int depth)
        {
            if (depth > 64) return; // guard against a malformed, cyclic tree

            // MediaBox and Resources are inheritable, so a page may declare neither.
            var resources = node.Get("Resources") ?? inheritedResources;
            var mediaBox = node.Get("MediaBox") ?? inheritedMediaBox;

            var type = Resolve(node.Get("Type")) as PdfNameValue;

            if (type?.Name == "Page")
            {
                AddPage(pages, node, resources, mediaBox);
                return;
            }

            if (GetEntry(node, "Kids") is not PdfArrayValue kids)
            {
                // Untyped leaf that has content: treat it as a page.
                if (node.Has("Contents")) AddPage(pages, node, resources, mediaBox);
                return;
            }

            foreach (var kid in kids.Items)
            {
                if (Resolve(kid) is PdfDictValue child)
                    Walk(child, resources, mediaBox, depth + 1);
            }
        }
    }

    private void AddPage(List<PdfPageInfo> pages, PdfDictValue dictionary, PdfValue? resources, PdfValue? mediaBox)
    {
        var box = Resolve(mediaBox ?? dictionary.Get("MediaBox")) as PdfArrayValue;

        var width = 612.0;
        var height = 792.0;

        if (box is { Count: >= 4 })
        {
            var x0 = ToDouble(Resolve(box[0]));
            var y0 = ToDouble(Resolve(box[1]));
            var x1 = ToDouble(Resolve(box[2]));
            var y1 = ToDouble(Resolve(box[3]));

            width = Math.Abs(x1 - x0);
            height = Math.Abs(y1 - y0);
        }

        pages.Add(new PdfPageInfo(
            pages.Count, width, height, dictionary,
            Resolve(resources ?? dictionary.Get("Resources")) as PdfDictValue));
    }

    public static double ToDouble(PdfValue? value) =>
        value is PdfNumberValue number ? number.Value : 0;

    // ----- cross references -----

    private void LoadCrossReferences()
    {
        try
        {
            var parser = new PdfParser(_data);
            var startxref = parser.LastIndexOf("startxref");

            if (startxref >= 0)
            {
                parser.Position = startxref + "startxref".Length;
                if (parser.ReadValue() is PdfNumberValue offset)
                    ReadCrossReferenceSection(offset.AsInt, 0);
            }
        }
        catch (Exception e) when (e is PdfParseException or IndexOutOfRangeException or ArgumentException)
        {
            // Fall through to the scan, which copes with anything.
        }

        // Always scan as well. It is cheap relative to the rest of the comparison and repairs a
        // truncated or unusual table without the caller ever knowing.
        ScanForObjects();

        _trailer ??= FindTrailerByScanning();
    }

    private void ReadCrossReferenceSection(int offset, int depth)
    {
        if (depth > 16 || offset <= 0 || offset >= _data.Length) return;

        var parser = new PdfParser(_data, offset);
        parser.SkipWhitespaceAndComments();

        if (!parser.Matches("xref"))
            return; // A cross-reference stream; the scan below handles it.

        parser.Position += "xref".Length;

        while (true)
        {
            parser.SkipWhitespaceAndComments();
            if (parser.Matches("trailer")) break;

            if (parser.ReadValue() is not PdfNumberValue first) break;
            if (parser.ReadValue() is not PdfNumberValue count) break;

            var start = first.AsInt;
            for (var i = 0; i < count.AsInt; i++)
            {
                parser.SkipWhitespaceAndComments();

                if (parser.ReadValue() is not PdfNumberValue entryOffset) return;
                if (parser.ReadValue() is not PdfNumberValue) return;

                parser.SkipWhitespaceAndComments();
                var isInUse = !parser.AtEnd && _data[parser.Position] == 'n';
                parser.Position++;

                // Offset 0 marks a free entry; Word also emits it for some in-use objects, which
                // is exactly the warning qpdf reports on its output. The scan repairs those.
                if (isInUse && entryOffset.AsInt > 0)
                    _objectOffsets.TryAdd(start + i, entryOffset.AsInt);
            }
        }

        parser.SkipWhitespaceAndComments();
        if (!parser.Matches("trailer")) return;

        parser.Position += "trailer".Length;
        if (parser.ReadValue() is not PdfDictValue trailer) return;

        _trailer ??= trailer;

        // Follow an incremental update chain back to the original table.
        if (trailer.Get("Prev") is PdfNumberValue previous)
            ReadCrossReferenceSection(previous.AsInt, depth + 1);
    }

    /// <summary>
    /// Scans the whole file for "N G obj" headers. This is what makes the reader robust against
    /// the offset-zero entries Word writes, and against cross-reference streams we do not parse.
    /// </summary>
    private void ScanForObjects()
    {
        for (var i = 0; i < _data.Length - 3; i++)
        {
            if (_data[i] != 'o' || _data[i + 1] != 'b' || _data[i + 2] != 'j') continue;
            if (i + 3 < _data.Length && !PdfParser.IsWhitespace(_data[i + 3]) && !PdfParser.IsDelimiter(_data[i + 3]))
                continue;

            // Walk backwards over "N G " to find the object number.
            var j = i - 1;
            while (j >= 0 && PdfParser.IsWhitespace(_data[j])) j--;

            var generationEnd = j + 1;
            while (j >= 0 && _data[j] >= '0' && _data[j] <= '9') j--;
            var generationStart = j + 1;
            if (generationStart == generationEnd) continue;

            while (j >= 0 && PdfParser.IsWhitespace(_data[j])) j--;

            var numberEnd = j + 1;
            while (j >= 0 && _data[j] >= '0' && _data[j] <= '9') j--;
            var numberStart = j + 1;
            if (numberStart == numberEnd) continue;

            // Must be preceded by whitespace or start of file, or it is part of something else.
            if (numberStart > 0 && !PdfParser.IsWhitespace(_data[numberStart - 1])) continue;

            var text = Encoding.ASCII.GetString(_data, numberStart, numberEnd - numberStart);
            if (!int.TryParse(text, out var objectNumber)) continue;

            // A later definition wins, matching how incremental updates override earlier ones.
            _objectOffsets[objectNumber] = numberStart;
        }

        _cache.Clear();
    }

    private PdfDictValue? FindTrailerByScanning()
    {
        var parser = new PdfParser(_data);
        var index = parser.LastIndexOf("trailer");

        if (index >= 0)
        {
            parser.Position = index + "trailer".Length;
            if (parser.ReadValue() is PdfDictValue trailer) return trailer;
        }

        // No trailer keyword: look for the catalog directly.
        foreach (var objectNumber in _objectOffsets.Keys.OrderBy(n => n))
        {
            if (GetObject(objectNumber) is PdfDictValue dictionary &&
                Resolve(dictionary.Get("Type")) is PdfNameValue { Name: "Catalog" })
            {
                return new PdfDictValue(new Dictionary<string, PdfValue>(StringComparer.Ordinal)
                {
                    ["Root"] = new PdfRefValue(objectNumber, 0)
                });
            }
        }

        return null;
    }

    // ----- filters -----

    /// <summary>
    /// Inflates, tolerating a truncated or over-long tail. Real files sometimes carry a stream
    /// whose declared length includes trailing bytes, and refusing to decode those would lose the
    /// whole page.
    /// </summary>
    private static byte[] FlateDecodeTolerant(byte[] data)
    {
        try
        {
            return PdfFilters.FlateDecode(data);
        }
        catch (InvalidDataException)
        {
        }

        // Retry with progressively shorter tails.
        for (var trim = 1; trim <= 4 && trim < data.Length; trim++)
        {
            try
            {
                return PdfFilters.FlateDecode(data[..^trim]);
            }
            catch (InvalidDataException)
            {
            }
        }

        // Some producers include leading whitespace before the zlib header.
        var start = 0;
        while (start < data.Length && PdfParser.IsWhitespace(data[start])) start++;

        if (start > 0)
        {
            try
            {
                return PdfFilters.FlateDecode(data[start..]);
            }
            catch (InvalidDataException)
            {
            }
        }

        return [];
    }

    private static byte[] AsciiHexDecode(byte[] data)
    {
        var bytes = new List<byte>();
        var digits = new List<char>();

        foreach (var b in data)
        {
            if (b == '>') break;
            if (PdfParser.IsWhitespace(b)) continue;
            digits.Add((char)b);
        }

        if (digits.Count % 2 != 0) digits.Add('0');

        for (var i = 0; i < digits.Count; i += 2)
        {
            if (int.TryParse($"{digits[i]}{digits[i + 1]}", System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                bytes.Add((byte)value);
            }
        }

        return [.. bytes];
    }

    /// <summary>Reverses the PNG predictor filters used by predictor values 10 and above.</summary>
    private static byte[] UndoPngPredictor(byte[] data, int columns, int colors, int bitsPerComponent)
    {
        var bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
        var rowLength = columns * colors * bitsPerComponent / 8;
        if (rowLength <= 0) return data;

        var output = new List<byte>(data.Length);
        var previous = new byte[rowLength];
        var position = 0;

        while (position + 1 + rowLength <= data.Length)
        {
            var filter = data[position++];
            var row = new byte[rowLength];
            Array.Copy(data, position, row, 0, rowLength);
            position += rowLength;

            for (var i = 0; i < rowLength; i++)
            {
                int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                int up = previous[i];
                int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;

                row[i] = filter switch
                {
                    0 => row[i],
                    1 => (byte)(row[i] + left),
                    2 => (byte)(row[i] + up),
                    3 => (byte)(row[i] + (left + up) / 2),
                    4 => (byte)(row[i] + Paeth(left, up, upLeft)),
                    _ => row[i]
                };
            }

            output.AddRange(row);
            previous = row;
        }

        return [.. output];
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
