using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>XML namespaces used by WordprocessingML documents.</summary>
internal static class W
{
    /// <summary>The main WordprocessingML namespace.</summary>
    public static readonly XNamespace Main =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>DrawingML, for images and shapes.</summary>
    public static readonly XNamespace Drawing =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>DrawingML word-processing extensions (inline and anchored drawing wrappers).</summary>
    public static readonly XNamespace WordDrawing =
        "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    /// <summary>DrawingML diagrams, which is what SmartArt is.</summary>
    public static readonly XNamespace Diagram =
        "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    /// <summary>Word's own extension for a shape drawn in the text, which is where a text box lives.</summary>
    public static readonly XNamespace Shape =
        "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

    /// <summary>
    /// Markup compatibility, which is how a document offers the same thing twice — once in the
    /// terms a newer reader understands and once in terms an older one does.
    /// </summary>
    public static readonly XNamespace Compatibility =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>Relationship references, the source of r:id attributes.</summary>
    public static readonly XNamespace Relationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>The xml: namespace, needed for xml:space on text elements.</summary>
    public static readonly XNamespace Xml = XNamespace.Xml;

    /// <summary>Reads a <c>w:val</c> attribute.</summary>
    public static string? Val(this XElement element) => element.Attribute(Main + "val")?.Value;

    /// <summary>
    /// Reads a <c>w:val</c> that carries an on/off value. An element with no val attribute means
    /// "on" — <c>&lt;w:b/&gt;</c> is bold — while "0", "false" and "off" all mean off.
    /// </summary>
    public static bool OnOff(this XElement element)
    {
        var value = element.Val();
        if (value is null) return true;

        return value switch
        {
            "0" or "false" or "off" => false,
            _ => true
        };
    }

    /// <summary>Reads an integer <c>w:val</c>.</summary>
    public static int? IntVal(this XElement element) =>
        // Invariant culture, like every other reader: a document's numbers are written the same
        // way whatever locale the machine reading it runs in, and parsing them by the ambient
        // culture reads a decimal comma or a thousands separator wrong (#160).
        int.TryParse(element.Val(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Reads a named attribute in the main namespace.</summary>
    public static string? Attr(this XElement element, string name) =>
        element.Attribute(Main + name)?.Value;

    /// <summary>Reads a named attribute as an integer, tolerating the decimals some producers emit.</summary>
    public static int? IntAttr(this XElement element, string name)
    {
        var text = element.Attr(name);
        if (text is null) return null;

        if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;   // invariant culture, as elsewhere (#160)
        }

        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var real))
        {
            return null;
        }

        // An out-of-range measurement cast straight to int becomes int.MinValue and silently
        // corrupts the geometry it feeds; one that will not fit is no measurement at all (#148).
        var rounded = Math.Round(real);
        return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : null;
    }

    /// <summary>Reads a named attribute as an on/off value.</summary>
    public static bool? BoolAttr(this XElement element, string name)
    {
        var text = element.Attr(name);
        return text switch
        {
            null => null,
            "0" or "false" or "off" => false,
            _ => true
        };
    }
}
