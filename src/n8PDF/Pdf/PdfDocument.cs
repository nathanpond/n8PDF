using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace n8PDF.Pdf;

/// <summary>
/// An in-memory PDF file: a flat pool of indirect objects plus the catalog and page tree that
/// give them structure. Build it up, then <see cref="Save"/> it.
/// </summary>
internal sealed class PdfDocument
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

    /// <summary>
    /// Claim and honour PDF/A-2b (#68): an XMP metadata packet agreeing with the information
    /// dictionary, an sRGB output intent, and a file identifier derived from the body itself.
    /// </summary>
    public bool PdfA { get; set; }

    private PdfReference? _metadataRef;
    private PdfReference? _intentProfileRef;

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

    /// <summary>A reference to the page at the given index, for links that point at it.</summary>
    public PdfReference GetPageReference(int index) => (PdfReference)_pageRefs[index];

    /// <summary>Hangs the document outline off the catalogue (#66).</summary>
    public void SetOutlines(PdfReference outlines) => _catalog.Set("Outlines", outlines);

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
        // PDF/A's extra objects are reserved once, so the catalogue can point at them and a
        // second save lands on the same numbers; their contents are built per save into the
        // local copy below, so saving still does not change what a later save writes (#68).
        if (PdfA && _metadataRef is null)
        {
            _metadataRef = Reserve();
            _intentProfileRef = Reserve();

            _catalog.Set("Metadata", _metadataRef);
            _catalog.Set("OutputIntents", new PdfArray().Add(new PdfDictionary()
                .Set("Type", "OutputIntent")
                .Set("S", "GTS_PDFA1")
                .Set("OutputConditionIdentifier", PdfString.FromText("sRGB"))
                .Set("Info", PdfString.FromText("sRGB IEC61966-2.1"))
                .Set("DestOutputProfile", _intentProfileRef)));
        }

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

        if (PdfA)
        {
            // The metadata packet must be readable by something that does not decompress (#68).
            var metadata = new PdfStream(BuildXmp()) { Compress = false };
            metadata.Set("Type", "Metadata");
            metadata.Set("Subtype", "XML");
            objects[_metadataRef!.ObjectNumber - 1] = metadata;

            var profile = new PdfStream(SrgbIccProfile.Bytes);
            profile.Set("N", new PdfNumber(3));
            objects[_intentProfileRef!.ObjectNumber - 1] = profile;
        }

        var writer = new PdfWriter(stream);
        using var bodyHash = PdfA ? IncrementalHash.CreateHash(HashAlgorithmName.MD5) : null;
        writer.Hash = bodyHash;

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

        writer.Hash = null;

        var xrefOffset = writer.Position;
        WriteXref(writer, offsets);

        var trailer = new PdfDictionary()
            .Set("Size", objects.Count + 1)
            .Set("Root", _catalogRef);
        if (infoRef is not null) trailer.Set("Info", infoRef);

        // The identifier PDF/A requires, derived from the body just written so that identical
        // input keeps giving byte-identical output (#68).
        if (bodyHash is not null)
        {
            var id = new PdfString(bodyHash.GetHashAndReset());
            trailer.Set("ID", new PdfArray().Add(id).Add(id));
        }

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

    /// <summary>
    /// The XMP packet PDF/A carries (#68), agreeing with the information dictionary: the same
    /// title, author, producer and date, plus the conformance claim itself.
    /// </summary>
    private byte[] BuildXmp()
    {
        static string Escape(string value) => value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        var builder = new StringBuilder();
        builder.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        builder.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        builder.Append("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        builder.Append("<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" " +
                       "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                       "xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
        builder.Append("<pdfaid:part>2</pdfaid:part>\n<pdfaid:conformance>B</pdfaid:conformance>\n");

        if (!string.IsNullOrEmpty(Producer))
            builder.Append("<pdf:Producer>").Append(Escape(Producer)).Append("</pdf:Producer>\n");

        if (Title is not null)
        {
            builder.Append("<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">")
                .Append(Escape(Title)).Append("</rdf:li></rdf:Alt></dc:title>\n");
        }

        if (Author is not null)
        {
            builder.Append("<dc:creator><rdf:Seq><rdf:li>").Append(Escape(Author))
                .Append("</rdf:li></rdf:Seq></dc:creator>\n");
        }

        if (CreationDate is { } created)
        {
            builder.Append("<xmp:CreateDate>")
                .Append(created.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))
                .Append("</xmp:CreateDate>\n");
        }

        builder.Append("</rdf:Description>\n</rdf:RDF>\n</x:xmpmeta>\n");

        // The padding the specification asks writers to leave, so a tool can edit in place.
        builder.Append(' ', 2048);
        builder.Append("<?xpacket end=\"w\"?>");

        return Encoding.UTF8.GetBytes(builder.ToString());
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
