using System.Globalization;
using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>
/// What a document says about itself: the properties Word keeps in its docProps parts, which the
/// AUTHOR, TITLE, CREATEDATE and DOCPROPERTY fields read.
/// </summary>
/// <remarks>
/// These live outside the document part, in <c>docProps/core.xml</c> for the standard ones and
/// <c>docProps/custom.xml</c> for whatever else a document chooses to carry. A document that has
/// neither — which is what a file written by hand usually is — leaves every field that reads them
/// showing what Word last computed.
/// </remarks>
internal sealed class DocumentProperties
{
    private static readonly XNamespace CoreNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";

    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    private static readonly XNamespace DublinCoreTerms = "http://purl.org/dc/terms/";

    private static readonly XNamespace CustomNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";

    private static readonly XNamespace VariantTypes =
        "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

    public string? Title { get; set; }

    public string? Subject { get; set; }

    /// <summary>Who wrote it, which is what AUTHOR shows.</summary>
    public string? Creator { get; set; }

    public string? Keywords { get; set; }

    /// <summary>The description, which the COMMENTS field shows.</summary>
    public string? Description { get; set; }

    public string? LastModifiedBy { get; set; }

    public DateTimeOffset? Created { get; set; }

    public DateTimeOffset? Modified { get; set; }

    public DateTimeOffset? LastPrinted { get; set; }

    /// <summary>Properties the document names itself, which DOCPROPERTY reads by name.</summary>
    public Dictionary<string, string> Custom { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static DocumentProperties Parse(XDocument? core, XDocument? custom)
    {
        var properties = new DocumentProperties();

        if (core?.Root is { } root)
        {
            properties.Title = Value(root, DublinCore + "title");
            properties.Subject = Value(root, DublinCore + "subject");
            properties.Creator = Value(root, DublinCore + "creator");
            properties.Description = Value(root, DublinCore + "description");
            properties.Keywords = Value(root, CoreNamespace + "keywords");
            properties.LastModifiedBy = Value(root, CoreNamespace + "lastModifiedBy");

            properties.Created = Timestamp(root, DublinCoreTerms + "created");
            properties.Modified = Timestamp(root, DublinCoreTerms + "modified");
            properties.LastPrinted = Timestamp(root, CoreNamespace + "lastPrinted");
        }

        if (custom?.Root is not { } customRoot) return properties;

        foreach (var property in customRoot.Elements(CustomNamespace + "property"))
        {
            if (property.Attribute("name")?.Value is not { Length: > 0 } name) continue;

            // The value is wrapped in an element naming its type — text, a number, a date. Only
            // the text of it matters here, whichever type it claims to be.
            if (property.Elements().FirstOrDefault(e => e.Name.Namespace == VariantTypes) is { } value)
                properties.Custom[name] = value.Value;
        }

        return properties;
    }

    private static string? Value(XElement root, XName name) =>
        root.Element(name)?.Value is { Length: > 0 } text ? text : null;

    /// <summary>
    /// Reads a timestamp. Word writes these in UTC and shows them in the reader's own zone, so a
    /// value with no zone of its own is read as UTC rather than as local time.
    /// </summary>
    private static DateTimeOffset? Timestamp(XElement root, XName name)
    {
        if (root.Element(name)?.Value is not { Length: > 0 } text) return null;

        return DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
