using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace n8PDF.Packaging;

/// <summary>A relationship between two package parts.</summary>
/// <param name="Id">The r:id referenced from document markup.</param>
/// <param name="Type">The relationship type URI.</param>
/// <param name="Target">The target as written in the .rels file, which may be relative.</param>
/// <param name="IsExternal">True when the target is a URL rather than a part in the package.</param>
internal sealed record OpcRelationship(string Id, string Type, string Target, bool IsExternal);

/// <summary>
/// Reads an Open Packaging Conventions container — the ZIP-of-XML-parts that a <c>.docx</c>
/// actually is.
/// </summary>
/// <remarks>
/// This layer deliberately knows nothing about Word. It resolves part names, content types and
/// relationships; interpreting what is inside a part is the job of the Ooxml layer.
/// </remarks>
internal sealed class OpcPackage : IDisposable
{
    /// <summary>Relationship type of the main document part.</summary>
    public const string OfficeDocumentRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";

    public const string StylesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";

    public const string NumberingRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";

    public const string ThemeRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

    public const string SettingsRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";

    public const string FontTableRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";

    public const string ImageRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";

    public const string FootnotesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";

    public const string EndnotesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes";

    public const string HyperlinkRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public const string HeaderRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";

    public const string FooterRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";

    /// <summary>
    /// How a part's XML is read.
    /// </summary>
    /// <remarks>
    /// Prohibiting document type definitions is the point of stating these at all. Left to itself
    /// <c>XDocument.Load</c> parses a DTD and expands the entities it declares, without bound —
    /// ten entities each ten of the one below expand a kilobyte into a gigabyte, which is the same
    /// attack as a compressed part one layer up and is not stopped by anything that counts
    /// compressed bytes. Nothing legitimate is lost: the Open Packaging Conventions forbid a DTD
    /// in a part, so a document that has one is malformed before it is hostile.
    ///
    /// The resolver is null as well, which is what stops a part reaching for a file or a URL of
    /// its own choosing. That much is already the framework's default, and saying so here means a
    /// change of default cannot quietly take it away.
    /// </remarks>
    private static readonly XmlReaderSettings PartReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static readonly XNamespace ContentTypesNamespace =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly XNamespace RelationshipsNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _defaultContentTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _overrideContentTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<OpcRelationship>> _relationshipCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _ownsStream;
    private readonly Stream? _stream;
    private readonly PackageLimits _limits;

    /// <summary>What has come out of the decompressor so far, across every part read.</summary>
    private long _decompressed;

    private OpcPackage(ZipArchive archive, Stream? stream, bool ownsStream, PackageLimits limits)
    {
        _archive = archive;
        _stream = stream;
        _ownsStream = ownsStream;
        _limits = limits;

        // Reading the central directory is what enumerating the entries does, so a package with
        // an absurd number of them has already cost something by the time this is checked. What
        // bounds that is the file: a central directory record is at least 46 bytes on disk, so a
        // ten megabyte file cannot declare more than a couple of hundred thousand parts however
        // hostile it is. This is the cheaper thing to say no to first, before any of them are read.
        var count = archive.Entries.Count;
        if (count > limits.MaximumPartCount)
        {
            throw new PackageTooLargeException(
                $"The package declares {count:N0} parts, and the limit is {limits.MaximumPartCount:N0}.");
        }

        foreach (var entry in archive.Entries)
            _entries[Normalize(entry.FullName)] = entry;

        ReadContentTypes();
    }

    public static OpcPackage Open(Stream stream, bool leaveOpen = true, PackageLimits? limits = null)
    {
        limits ??= new PackageLimits();

        // A non-seekable stream cannot back a ZipArchive, and callers legitimately hand us
        // network and pipe streams, so buffer when needed.
        if (!stream.CanSeek)
        {
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return new OpcPackage(new ZipArchive(buffer, ZipArchiveMode.Read), buffer, ownsStream: true, limits);
        }

        return new OpcPackage(
            new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen), null, ownsStream: false, limits);
    }

    public static OpcPackage Open(string path, PackageLimits? limits = null) =>
        Open(File.OpenRead(path), leaveOpen: false, limits);

    public IEnumerable<string> PartNames => _entries.Keys;

    public bool HasPart(string partName) => _entries.ContainsKey(Normalize(partName));

    /// <summary>Reads a part's bytes. Part names are absolute and slash-separated.</summary>
    public byte[] ReadPart(string partName)
    {
        using var stream = OpenPart(partName);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public XDocument ReadPartAsXml(string partName)
    {
        using var stream = OpenPart(partName);
        using var reader = XmlReader.Create(stream, PartReaderSettings);

        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// A part's contents, as a stream that will not hand over more than the limits allow.
    /// </summary>
    /// <remarks>
    /// The declared size is checked first because it is free — a package that says outright it
    /// holds more than is allowed can be refused without decompressing a byte. It is not trusted,
    /// though: the header is written by whoever wrote the file, so what is counted is what comes
    /// out of the decompressor.
    /// </remarks>
    private Stream OpenPart(string partName)
    {
        if (!_entries.TryGetValue(Normalize(partName), out var entry))
            throw new FileNotFoundException($"The package has no part named '{partName}'.");

        if (entry.Length > _limits.MaximumPartBytes)
        {
            throw new PackageTooLargeException(
                $"Part '{partName}' says it holds {entry.Length:N0} bytes, and the limit for one " +
                $"part is {_limits.MaximumPartBytes:N0}.");
        }

        return new LimitedStream(entry.Open(), this, partName);
    }

    /// <summary>Counts what a part has decompressed to, and stops it running away.</summary>
    private sealed class LimitedStream(Stream inner, OpcPackage package, string partName) : Stream
    {
        private long _read;

        public override int Read(byte[] buffer, int offset, int count) =>
            Count(inner.Read(buffer, offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        private int Count(int read)
        {
            if (read <= 0) return read;

            _read += read;
            package._decompressed += read;

            if (_read > package._limits.MaximumPartBytes)
            {
                throw new PackageTooLargeException(
                    $"Part '{partName}' decompressed to more than the {package._limits.MaximumPartBytes:N0} " +
                    "bytes allowed for one part.");
            }

            if (package._decompressed > package._limits.MaximumTotalBytes)
            {
                throw new PackageTooLargeException(
                    $"The package decompressed to more than the {package._limits.MaximumTotalBytes:N0} " +
                    $"bytes allowed in total, reading part '{partName}'.");
            }

            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    public XDocument? TryReadPartAsXml(string partName) =>
        HasPart(partName) ? ReadPartAsXml(partName) : null;

    /// <summary>Returns the content type declared for a part, by override or by extension default.</summary>
    public string? GetContentType(string partName)
    {
        var normalized = Normalize(partName);
        if (_overrideContentTypes.TryGetValue("/" + normalized, out var overridden))
            return overridden;

        var extension = Path.GetExtension(normalized).TrimStart('.');
        return _defaultContentTypes.GetValueOrDefault(extension);
    }

    /// <summary>
    /// Reads the relationships declared for a part. Passing an empty name reads the package-level
    /// relationships in <c>_rels/.rels</c>, which is where the main document part is found.
    /// </summary>
    public IReadOnlyList<OpcRelationship> GetRelationships(string partName)
    {
        var normalized = Normalize(partName);
        if (_relationshipCache.TryGetValue(normalized, out var cached))
            return cached;

        var relsPart = GetRelationshipPartName(normalized);
        var result = new List<OpcRelationship>();

        if (_entries.ContainsKey(relsPart))
        {
            var xml = ReadPartAsXml(relsPart);
            foreach (var element in xml.Root?.Elements(RelationshipsNamespace + "Relationship") ?? [])
            {
                var id = element.Attribute("Id")?.Value;
                var type = element.Attribute("Type")?.Value;
                var target = element.Attribute("Target")?.Value;
                if (id is null || type is null || target is null) continue;

                var isExternal = string.Equals(
                    element.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase);

                result.Add(new OpcRelationship(id, type, target, isExternal));
            }
        }

        _relationshipCache[normalized] = result;
        return result;
    }

    public OpcRelationship? GetRelationshipById(string partName, string id) =>
        GetRelationships(partName).FirstOrDefault(r => r.Id == id);

    public OpcRelationship? GetRelationshipByType(string partName, string type) =>
        GetRelationships(partName).FirstOrDefault(r => r.Type == type);

    /// <summary>
    /// Resolves a relationship target against the part that declared it, producing an absolute
    /// part name. Targets are usually relative ("styles.xml" from "word/document.xml") but may
    /// be absolute ("/word/styles.xml").
    /// </summary>
    public string ResolveTarget(string sourcePartName, string target)
    {
        if (target.StartsWith('/'))
            return Normalize(target);

        var directory = Path.GetDirectoryName(Normalize(sourcePartName))?.Replace('\\', '/') ?? string.Empty;
        var combined = string.IsNullOrEmpty(directory) ? target : directory + "/" + target;

        // Collapse any ".." segments a producer may have written.
        var segments = new List<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>Finds the part name of the main document, following the package relationship.</summary>
    public string GetMainDocumentPartName()
    {
        var relationship = GetRelationshipByType(string.Empty, OfficeDocumentRelationship)
            ?? throw new InvalidDataException(
                "The package declares no main document relationship; it is not a WordprocessingML document.");

        var partName = ResolveTarget(string.Empty, relationship.Target);
        if (!HasPart(partName))
            throw new InvalidDataException($"The main document relationship points at a missing part: '{partName}'.");

        return partName;
    }

    /// <summary>Resolves a part related to another by relationship type, or null when absent.</summary>
    public string? GetRelatedPartName(string sourcePartName, string relationshipType)
    {
        var relationship = GetRelationshipByType(sourcePartName, relationshipType);
        if (relationship is null || relationship.IsExternal) return null;

        var partName = ResolveTarget(sourcePartName, relationship.Target);
        return HasPart(partName) ? partName : null;
    }

    private static string GetRelationshipPartName(string partName)
    {
        if (string.IsNullOrEmpty(partName))
            return "_rels/.rels";

        var directory = Path.GetDirectoryName(partName)?.Replace('\\', '/') ?? string.Empty;
        var file = Path.GetFileName(partName);
        return string.IsNullOrEmpty(directory) ? $"_rels/{file}.rels" : $"{directory}/_rels/{file}.rels";
    }

    private void ReadContentTypes()
    {
        if (!_entries.ContainsKey("[Content_Types].xml")) return;

        var xml = ReadPartAsXml("[Content_Types].xml");
        foreach (var element in xml.Root?.Elements() ?? [])
        {
            if (element.Name == ContentTypesNamespace + "Default")
            {
                var extension = element.Attribute("Extension")?.Value;
                var contentType = element.Attribute("ContentType")?.Value;
                if (extension is not null && contentType is not null)
                    _defaultContentTypes[extension] = contentType;
            }
            else if (element.Name == ContentTypesNamespace + "Override")
            {
                var partName = element.Attribute("PartName")?.Value;
                var contentType = element.Attribute("ContentType")?.Value;
                if (partName is not null && contentType is not null)
                    _overrideContentTypes[partName] = contentType;
            }
        }
    }

    private static string Normalize(string partName) =>
        partName.Replace('\\', '/').TrimStart('/');

    public void Dispose()
    {
        _archive.Dispose();
        if (_ownsStream) _stream?.Dispose();
    }
}
