using System.IO.Compression;
using System.Xml.Linq;

namespace n8PDF.Packaging;

/// <summary>A relationship between two package parts.</summary>
/// <param name="Id">The r:id referenced from document markup.</param>
/// <param name="Type">The relationship type URI.</param>
/// <param name="Target">The target as written in the .rels file, which may be relative.</param>
/// <param name="IsExternal">True when the target is a URL rather than a part in the package.</param>
public sealed record OpcRelationship(string Id, string Type, string Target, bool IsExternal);

/// <summary>
/// Reads an Open Packaging Conventions container — the ZIP-of-XML-parts that a <c>.docx</c>
/// actually is.
/// </summary>
/// <remarks>
/// This layer deliberately knows nothing about Word. It resolves part names, content types and
/// relationships; interpreting what is inside a part is the job of the Ooxml layer.
/// </remarks>
public sealed class OpcPackage : IDisposable
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

    public const string HyperlinkRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";

    public const string HeaderRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";

    public const string FooterRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";

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

    private OpcPackage(ZipArchive archive, Stream? stream, bool ownsStream)
    {
        _archive = archive;
        _stream = stream;
        _ownsStream = ownsStream;

        foreach (var entry in archive.Entries)
            _entries[Normalize(entry.FullName)] = entry;

        ReadContentTypes();
    }

    public static OpcPackage Open(Stream stream, bool leaveOpen = true)
    {
        // A non-seekable stream cannot back a ZipArchive, and callers legitimately hand us
        // network and pipe streams, so buffer when needed.
        if (!stream.CanSeek)
        {
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return new OpcPackage(new ZipArchive(buffer, ZipArchiveMode.Read), buffer, ownsStream: true);
        }

        return new OpcPackage(new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen), null, ownsStream: false);
    }

    public static OpcPackage Open(string path) => Open(File.OpenRead(path), leaveOpen: false);

    public IEnumerable<string> PartNames => _entries.Keys;

    public bool HasPart(string partName) => _entries.ContainsKey(Normalize(partName));

    /// <summary>Reads a part's bytes. Part names are absolute and slash-separated.</summary>
    public byte[] ReadPart(string partName)
    {
        if (!_entries.TryGetValue(Normalize(partName), out var entry))
            throw new FileNotFoundException($"The package has no part named '{partName}'.");

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public XDocument ReadPartAsXml(string partName)
    {
        if (!_entries.TryGetValue(Normalize(partName), out var entry))
            throw new FileNotFoundException($"The package has no part named '{partName}'.");

        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
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
