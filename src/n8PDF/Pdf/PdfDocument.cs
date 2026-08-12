using System.Globalization;

namespace n8PDF.Pdf;

/// <summary>
/// An in-memory PDF file: a flat pool of indirect objects plus the catalog and page tree that
/// give them structure. Build it up, then <see cref="Save"/> it.
/// </summary>
public sealed class PdfDocument
{
    private readonly List<PdfObject?> _objects = [];
    private readonly PdfArray _pageRefs = new();
    private readonly PdfDictionary _catalog = new();
    private readonly PdfDictionary _pagesNode = new();
    private readonly PdfReference _catalogRef;
    private readonly PdfReference _pagesRef;

    public PdfDocument()
    {
        _pagesNode.Set("Type", "Pages").Set("Kids", _pageRefs).Set("Count", 0);
        _pagesRef = Add(_pagesNode);

        _catalog.Set("Type", "Catalog").Set("Pages", _pagesRef);
        _catalogRef = Add(_catalog);
    }

    /// <summary>Optional document information dictionary values.</summary>
    public string? Title { get; set; }

    public string? Author { get; set; }

    public string Producer { get; set; } = "n8PDF";

    /// <summary>
    /// Fixed creation timestamp. Left null by default so output is byte-reproducible; tests and
    /// golden comparisons depend on that.
    /// </summary>
    public DateTimeOffset? CreationDate { get; set; }

    public int PageCount => _pageRefs.Count;

    /// <summary>Adds an object to the file body and returns a reference to it.</summary>
    public PdfReference Add(PdfObject value)
    {
        _objects.Add(value);
        return new PdfReference(_objects.Count);
    }

    /// <summary>
    /// Reserves an object number before the object itself exists, for the circular references
    /// the format requires (a page points at its parent, which points back at the page).
    /// </summary>
    public PdfReference Reserve()
    {
        _objects.Add(null);
        return new PdfReference(_objects.Count);
    }

    public void Assign(PdfReference reference, PdfObject value) =>
        _objects[reference.ObjectNumber - 1] = value;

    /// <summary>Appends a page of the given size in points and returns its dictionary.</summary>
    public PdfDictionary AddPage(double widthPoints, double heightPoints, out PdfReference pageRef)
    {
        var page = new PdfDictionary()
            .Set("Type", "Page")
            .Set("Parent", _pagesRef)
            .Set("MediaBox", new PdfArray().Add(0).Add(0).Add(widthPoints).Add(heightPoints));

        pageRef = Add(page);
        _pageRefs.Add(pageRef);
        _pagesNode.Set("Count", _pageRefs.Count);
        return page;
    }

    public void Save(Stream stream)
    {
        // Saving must not mutate the document, so the info dictionary is appended to a local
        // copy of the object pool rather than to the pool itself.
        var objects = new List<PdfObject?>(_objects);
        var info = BuildInfo();
        PdfReference? infoRef = null;
        if (info is not null)
        {
            objects.Add(info);
            infoRef = new PdfReference(objects.Count);
        }

        var writer = new PdfWriter(stream);

        // Header. The binary comment line marks the file as containing binary data so that
        // transfer tools do not mangle it.
        writer.WriteLine("%PDF-1.7");
        writer.WriteByte((byte)'%');
        writer.WriteBytes([0xe2, 0xe3, 0xcf, 0xd3]);
        writer.WriteByte((byte)'\n');

        var offsets = new long[objects.Count + 1];
        for (var i = 0; i < objects.Count; i++)
        {
            var value = objects[i] ?? PdfNull.Instance;
            offsets[i + 1] = writer.Position;

            writer.WriteAscii($"{i + 1} 0 obj\n");
            value.Write(writer);
            writer.WriteAscii("\nendobj\n");
        }

        var xrefOffset = writer.Position;
        WriteXref(writer, offsets);

        var trailer = new PdfDictionary()
            .Set("Size", objects.Count + 1)
            .Set("Root", _catalogRef);
        if (infoRef is not null) trailer.Set("Info", infoRef);

        writer.WriteAscii("trailer\n");
        trailer.Write(writer);
        writer.WriteAscii("\nstartxref\n");
        writer.WriteLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        writer.WriteAscii("%%EOF\n");

        stream.Flush();
    }

    private PdfDictionary? BuildInfo()
    {
        var info = new PdfDictionary();
        if (Title is not null) info.Set("Title", PdfString.FromText(Title));
        if (Author is not null) info.Set("Author", PdfString.FromText(Author));
        if (!string.IsNullOrEmpty(Producer)) info.Set("Producer", PdfString.FromText(Producer));
        if (CreationDate is { } created)
            info.Set("CreationDate", new PdfString(System.Text.Encoding.ASCII.GetBytes(FormatDate(created))));

        return info.Count == 0 ? null : info;
    }

    private static void WriteXref(PdfWriter writer, long[] offsets)
    {
        var count = offsets.Length;

        writer.WriteLine("xref");
        writer.WriteLine($"0 {count}");

        // Entries are fixed at exactly 20 bytes each, including the two-byte line ending.
        writer.WriteAscii("0000000000 65535 f \n");
        for (var i = 1; i < count; i++)
        {
            var offset = offsets[i].ToString("D10", CultureInfo.InvariantCulture);
            writer.WriteAscii($"{offset} 00000 n \n");
        }
    }

    private static string FormatDate(DateTimeOffset value)
    {
        var offset = value.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var abs = offset.Duration();
        return string.Format(
            CultureInfo.InvariantCulture,
            "D:{0:yyyyMMddHHmmss}{1}{2:00}'{3:00}'",
            value.DateTime, sign, abs.Hours, abs.Minutes);
    }
}
