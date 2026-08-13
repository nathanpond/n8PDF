namespace n8PDF.Tests.Support;

/// <summary>
/// Writes an enhanced metafile: the records of a drawing being made, which is what a metafile is.
/// </summary>
/// <remarks>
/// Only as much of the format as it takes to make a file worth reading — a header, some pens and
/// brushes, the shapes and paths a drawing is made of, a line of text, and a bitmap. Nothing here
/// is a general encoder, and nothing here is Windows: it is the format written out by hand so that
/// what reads it can be tested against a file whose every record is known.
/// </remarks>
public sealed class EmfWriter(int width, int height)
{
    private readonly List<byte[]> _records = [];
    private uint _handles = 1;

    /// <summary>Where the drawing lies, in the metafile's own units — a hundredth of a millimetre.</summary>
    private const double UnitsPerPoint = 2540.0 / 72.0;

    public uint CreatePen(byte red, byte green, byte blue, int lineWidth = 1)
    {
        var handle = _handles++;

        _records.Add(Record(38,   // CREATEPEN
            Int32((int)handle),
            Int32(0),             // solid
            Int32(lineWidth), Int32(0),
            Colour(red, green, blue)));

        return handle;
    }

    public uint CreateBrush(byte red, byte green, byte blue)
    {
        var handle = _handles++;

        _records.Add(Record(39,   // CREATEBRUSHINDIRECT
            Int32((int)handle),
            Int32(0),             // solid
            Colour(red, green, blue),
            Int32(0)));

        return handle;
    }

    /// <summary>A hollow brush, which is how a shape is drawn as an outline and nothing else.</summary>
    public uint CreateHollowBrush()
    {
        var handle = _handles++;

        _records.Add(Record(39, Int32((int)handle), Int32(1), Colour(0, 0, 0), Int32(0)));

        return handle;
    }

    public uint CreateFont(string family, int size, bool bold = false, bool italic = false)
    {
        var handle = _handles++;

        var font = new List<byte>();
        font.AddRange(Int32(-size));            // height, negative for the characters' own
        font.AddRange(Int32(0));                // width
        font.AddRange(Int32(0));                // escapement
        font.AddRange(Int32(0));                // orientation
        font.AddRange(Int32(bold ? 700 : 400)); // weight
        font.Add((byte)(italic ? 1 : 0));
        font.Add(0);                            // underline
        font.Add(0);                            // strike out
        font.Add(0);                            // char set
        font.Add(0);                            // out precision
        font.Add(0);                            // clip precision
        font.Add(0);                            // quality
        font.Add(0);                            // pitch and family

        for (var i = 0; i < 32; i++)
        {
            var character = i < family.Length ? family[i] : '\0';
            font.AddRange(UInt16(character));
        }

        // The record keeps room for the rest of the extended font, which nothing here writes.
        while (font.Count < 320) font.Add(0);

        _records.Add(Record(82, Int32((int)handle), [.. font]));

        return handle;
    }

    public EmfWriter Select(uint handle)
    {
        _records.Add(Record(37, Int32((int)handle)));
        return this;
    }

    /// <summary>Selects one of the objects a metafile need not create, named by number.</summary>
    public EmfWriter SelectStock(int stock)
    {
        _records.Add(Record(37, Int32(unchecked((int)(0x80000000u | (uint)stock)))));
        return this;
    }

    public EmfWriter Rectangle(int left, int top, int right, int bottom)
    {
        _records.Add(Record(43, Int32(left), Int32(top), Int32(right), Int32(bottom)));
        return this;
    }

    public EmfWriter Ellipse(int left, int top, int right, int bottom)
    {
        _records.Add(Record(42, Int32(left), Int32(top), Int32(right), Int32(bottom)));
        return this;
    }

    public EmfWriter MoveTo(int x, int y)
    {
        _records.Add(Record(27, Int32(x), Int32(y)));
        return this;
    }

    public EmfWriter LineTo(int x, int y)
    {
        _records.Add(Record(54, Int32(x), Int32(y)));
        return this;
    }

    public EmfWriter Polygon(params (int X, int Y)[] points)
    {
        var body = new List<byte>();
        body.AddRange(Bounds(points));
        body.AddRange(Int32(points.Length));

        foreach (var (x, y) in points)
        {
            body.AddRange(Int32(x));
            body.AddRange(Int32(y));
        }

        _records.Add(Record(2, [.. body]));
        return this;
    }

    /// <summary>A curve, which takes its points in threes: two controls and where it arrives.</summary>
    public EmfWriter Bezier(params (int X, int Y)[] points)
    {
        var body = new List<byte>();
        body.AddRange(Bounds(points));
        body.AddRange(Int32(points.Length));

        foreach (var (x, y) in points)
        {
            body.AddRange(Int32(x));
            body.AddRange(Int32(y));
        }

        _records.Add(Record(1, [.. body]));
        return this;
    }

    public EmfWriter TextColor(byte red, byte green, byte blue)
    {
        _records.Add(Record(25, Colour(red, green, blue)));
        return this;
    }

    public EmfWriter Text(int x, int y, string text)
    {
        var body = new List<byte>();

        body.AddRange(Int32(0));                      // bounds, which nothing reads
        body.AddRange(Int32(0));
        body.AddRange(Int32(width));
        body.AddRange(Int32(height));
        body.AddRange(Int32(1));                      // graphics mode
        body.AddRange(Single(1));                     // scale
        body.AddRange(Single(1));
        body.AddRange(Int32(x));                      // where the text goes
        body.AddRange(Int32(y));
        body.AddRange(Int32(text.Length));

        // The offset to the characters is measured from the start of the record, so it counts the
        // eight bytes of type and size, everything written so far, and the five fields still to
        // come: the offset itself, the options, the clipping rectangle and the widths.
        const int header = 8;
        var offset = header + body.Count + 4 + 4 + 16 + 4;

        body.AddRange(Int32(offset));
        body.AddRange(Int32(0));                      // options
        body.AddRange(Int32(0));                      // clipping rectangle
        body.AddRange(Int32(0));
        body.AddRange(Int32(0));
        body.AddRange(Int32(0));
        body.AddRange(Int32(0));                      // offset to the character widths

        foreach (var character in text) body.AddRange(UInt16(character));

        while (body.Count % 4 != 0) body.Add(0);

        _records.Add(Record(84, [.. body]));
        return this;
    }

    /// <summary>Draws a bitmap into a rectangle, the way a drawing carries a picture.</summary>
    public EmfWriter Bitmap(int x, int y, int drawWidth, int drawHeight, byte[] bmp)
    {
        // A device-independent bitmap is a bitmap without its file header.
        var headerSize = bmp[14] | (bmp[15] << 8) | (bmp[16] << 16) | (bmp[17] << 24);
        var pixelsAt = bmp[10] | (bmp[11] << 8) | (bmp[12] << 16) | (bmp[13] << 24);

        var info = bmp[14..(14 + headerSize)];
        var bits = bmp[pixelsAt..];

        var body = new List<byte>();

        body.AddRange(Int32(0));                      // bounds
        body.AddRange(Int32(0));
        body.AddRange(Int32(width));
        body.AddRange(Int32(height));
        body.AddRange(Int32(x));                      // where it goes
        body.AddRange(Int32(y));
        body.AddRange(Int32(0));                      // where in the source it comes from
        body.AddRange(Int32(0));
        body.AddRange(Int32(0));
        body.AddRange(Int32(0));

        // The bitmap's own bytes follow the fields, so the offsets to them count the eight bytes
        // of type and size, what is written so far, and the eight fields still to come.
        const int header = 8;
        var infoAt = header + body.Count + 8 * 4;
        var bitsAt = infoAt + info.Length;

        body.AddRange(Int32(infoAt));
        body.AddRange(Int32(info.Length));
        body.AddRange(Int32(bitsAt));
        body.AddRange(Int32(bits.Length));
        body.AddRange(Int32(0));                      // colour usage
        body.AddRange(Int32(0x00CC0020));             // a straight copy
        body.AddRange(Int32(drawWidth));
        body.AddRange(Int32(drawHeight));

        body.AddRange(info);
        body.AddRange(bits);

        while (body.Count % 4 != 0) body.Add(0);

        _records.Add(Record(81, [.. body]));
        return this;
    }

    public EmfWriter BeginPath()
    {
        _records.Add(Record(59));
        return this;
    }

    public EmfWriter EndPath()
    {
        _records.Add(Record(60));
        return this;
    }

    public EmfWriter CloseFigure()
    {
        _records.Add(Record(61));
        return this;
    }

    public EmfWriter FillPath()
    {
        _records.Add(Record(62, Int32(0), Int32(0), Int32(width), Int32(height)));
        return this;
    }

    public EmfWriter StrokeAndFillPath()
    {
        _records.Add(Record(63, Int32(0), Int32(0), Int32(width), Int32(height)));
        return this;
    }

    public byte[] Build()
    {
        var body = new List<byte>();
        foreach (var record in _records) body.AddRange(record);

        var eof = Record(14, Int32(0), Int32(0x10), Int32(0x14));

        var header = new List<byte>();
        header.AddRange(Int32(1));                            // the header record
        header.AddRange(Int32(0));                            // its size, filled in below
        header.AddRange(Int32(0));                            // bounds
        header.AddRange(Int32(0));
        header.AddRange(Int32(width - 1));
        header.AddRange(Int32(height - 1));
        header.AddRange(Int32(0));                            // frame, in hundredths of a mm
        header.AddRange(Int32(0));
        header.AddRange(Int32((int)(width * UnitsPerPoint)));
        header.AddRange(Int32((int)(height * UnitsPerPoint)));
        header.AddRange([0x20, 0x45, 0x4D, 0x46]);            // " EMF"
        header.AddRange(Int32(0x10000));                      // version
        header.AddRange(Int32(0));                            // size of the whole file, below
        header.AddRange(Int32(_records.Count + 2));           // how many records
        header.AddRange(UInt16((int)_handles + 1));           // how many handles
        header.AddRange(UInt16(0));                           // reserved
        header.AddRange(Int32(0));                            // description
        header.AddRange(Int32(0));
        header.AddRange(Int32(0));                            // palette entries
        // The device has to agree with the frame. A metafile says how big it is twice over — the
        // frame in hundredths of a millimetre, and the resolution of the device it was recorded
        // for — and a reader may believe either. Writing the two at odds makes a file that is a
        // different size depending on who reads it, which is a fault in the file rather than in
        // the reader. These are a device of 72 units to the inch, so a unit is a point.
        header.AddRange(Int32(1920));                         // device, in pixels
        header.AddRange(Int32(1080));
        header.AddRange(Int32(677));                          // device, in millimetres
        header.AddRange(Int32(381));

        while (header.Count % 4 != 0) header.Add(0);

        var file = new byte[header.Count + body.Count + eof.Length];

        Copy(Int32(header.Count), header, 4);
        Copy(Int32(file.Length), header, 48);

        header.CopyTo(file, 0);
        body.CopyTo(file, header.Count);
        eof.CopyTo(file, header.Count + body.Count);

        return file;
    }

    private static void Copy(byte[] value, List<byte> target, int at)
    {
        for (var i = 0; i < value.Length; i++) target[at + i] = value[i];
    }

    private static byte[] Record(int type, params byte[][] parts)
    {
        var body = new List<byte>();
        foreach (var part in parts) body.AddRange(part);

        while (body.Count % 4 != 0) body.Add(0);

        var record = new List<byte>();
        record.AddRange(Int32(type));
        record.AddRange(Int32(8 + body.Count));
        record.AddRange(body);

        return [.. record];
    }

    private static byte[] Bounds((int X, int Y)[] points)
    {
        var bounds = new List<byte>();

        bounds.AddRange(Int32(points.Min(p => p.X)));
        bounds.AddRange(Int32(points.Min(p => p.Y)));
        bounds.AddRange(Int32(points.Max(p => p.X)));
        bounds.AddRange(Int32(points.Max(p => p.Y)));

        return [.. bounds];
    }

    /// <summary>A colour, which a metafile writes as red, green and blue in that order.</summary>
    private static byte[] Colour(byte red, byte green, byte blue) => [red, green, blue, 0];

    private static byte[] Int32(int value) =>
        [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static byte[] UInt16(int value) => [(byte)value, (byte)(value >> 8)];

    private static byte[] Single(float value) => BitConverter.GetBytes(value);
}
