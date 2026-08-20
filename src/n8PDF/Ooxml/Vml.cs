using System.Globalization;
using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>
/// Reads a shape in the older spelling: the <c>w:pict</c> Word wrote before 2007, and still writes
/// for a watermark and inside the fallback of every shape it writes in the newer one.
/// </summary>
/// <remarks>
/// VML says in one attribute what DrawingML says in a dozen elements. A shape's size, where it
/// sits and what it is anchored to are all in a <c>style</c> borrowed from CSS; its colours are
/// attributes on the shape; and its geometry is the element's own name rather than anything
/// declared. What comes out is the same <see cref="ShapeFrame"/> the newer spelling produces, so
/// everything downstream — laying the text out inside it, drawing the outline, flowing text round
/// it — is the code that was already there.
/// </remarks>
public static class Vml
{
    /// <summary>The VML namespace itself.</summary>
    public static readonly XNamespace Main = "urn:schemas-microsoft-com:vml";

    /// <summary>Word's own additions to it, which is where the text wrapping is declared.</summary>
    public static readonly XNamespace Word = "urn:schemas-microsoft-com:office:word";

    /// <summary>
    /// The shape elements that carry their own geometry, and what each one is drawn as. A
    /// <c>v:shape</c> is the general case, whose geometry is a path over a grid of its own; it is
    /// drawn as a rectangle, which is what the shape type Word uses for a text box describes.
    /// </summary>
    private static readonly Dictionary<string, string> Geometries = new()
    {
        ["rect"] = "rect",
        ["roundrect"] = "roundRect",
        ["oval"] = "ellipse",
        ["shape"] = "rect"
    };

    /// <summary>
    /// Word's default inset, which is the same tenth of an inch at the sides and half of that
    /// above and below that the newer spelling has.
    /// </summary>
    private static readonly double[] DefaultInset = [7.2, 3.6, 7.2, 3.6];

    /// <summary>
    /// How much clearance a floating shape keeps beside it where it asks for none: an eighth of
    /// an inch, which is the format's own default and what Word's export shows — the text beside
    /// the fixture's box begins 9.12pt past its right edge, and the shape says nothing about it.
    /// Above and below there is none.
    /// </summary>
    private const double DefaultSideWrapPoints = 9;

    /// <summary>Reads the drawing a <c>w:pict</c> holds, or null where it holds nothing drawable.</summary>
    public static InlineElement? ParsePicture(XElement pict)
    {
        var element = pict.Elements()
            .FirstOrDefault(child => child.Name.Namespace == Main &&
                                     Geometries.ContainsKey(child.Name.LocalName));

        if (element is null) return null;

        var style = ReadStyle(element.Attribute("style")?.Value);

        var width = Length(style.GetValueOrDefault("width"));
        var height = Length(style.GetValueOrDefault("height"));
        if (width is not { } wide || height is not { } tall || wide <= 0 || tall <= 0) return null;

        var shape = ReadShape(element);

        var widthEmu = (long)Math.Round(Units.PointsToEmu(wide));
        var heightEmu = (long)Math.Round(Units.PointsToEmu(tall));

        // A shape positioned absolutely floats; one that says nothing about its position sits in
        // the line like a picture.
        if (style.GetValueOrDefault("position") != "absolute")
            return new DrawingInline(widthEmu, heightEmu, null) { Shape = shape };

        return new AnchoredDrawing
        {
            Shape = shape,
            WidthEmu = widthEmu,
            HeightEmu = heightEmu,
            Wrap = ReadWrap(element),
            BehindText = style.GetValueOrDefault("z-index")?.StartsWith('-') ?? false,
            HorizontalFrom = Relative(style.GetValueOrDefault("mso-position-horizontal-relative")),
            HorizontalAlign = style.GetValueOrDefault("mso-position-horizontal"),
            HorizontalOffsetEmu = Offset(style, "margin-left", "left"),
            VerticalFrom = VerticalRelative(style.GetValueOrDefault("mso-position-vertical-relative")),
            VerticalAlign = style.GetValueOrDefault("mso-position-vertical"),
            VerticalOffsetEmu = Offset(style, "margin-top", "top"),
            DistanceLeftEmu = WrapDistance(style, "mso-wrap-distance-left", DefaultSideWrapPoints),
            DistanceRightEmu = WrapDistance(style, "mso-wrap-distance-right", DefaultSideWrapPoints),
            DistanceTopEmu = WrapDistance(style, "mso-wrap-distance-top", 0),
            DistanceBottomEmu = WrapDistance(style, "mso-wrap-distance-bottom", 0)
        };
    }

    private static ShapeFrame ReadShape(XElement element)
    {
        var shape = new ShapeFrame
        {
            Geometry = Geometries.GetValueOrDefault(element.Name.LocalName, "rect")
        };

        // VML fills and strokes a shape unless told not to, and the colours it does that in are
        // white and black. Both are the format's own defaults rather than something measured;
        // every shape Word writes states both outright.
        shape.Fill = element.Attribute("filled")?.Value is "f" or "false"
            ? null
            : Color(element.Attribute("fillcolor")?.Value) ?? new DrawingColorReference("FFFFFF", null);

        if (element.Attribute("stroked")?.Value is "f" or "false")
        {
            shape.Line = null;
        }
        else
        {
            shape.Line = Color(element.Attribute("strokecolor")?.Value)
                         ?? new DrawingColorReference("000000", null);

            if (Length(element.Attribute("strokeweight")?.Value) is { } weight && weight > 0)
                shape.LineWidthPoints = weight;

            shape.DrawnOffsetPoints = DrawnOffset(shape.LineWidthPoints);
        }

        var textbox = element.Element(Main + "textbox");
        if (textbox is null) return shape;

        var insets = ReadInset(textbox.Attribute("inset")?.Value);
        shape.InsetLeftPoints = insets[0];
        shape.InsetTopPoints = insets[1];
        shape.InsetRightPoints = insets[2];
        shape.InsetBottomPoints = insets[3];

        shape.Anchor = ReadStyle(textbox.Attribute("style")?.Value)
            .GetValueOrDefault("v-text-anchor") switch
        {
            "middle" or "middle-center" => ShapeTextAnchor.Center,
            "bottom" or "bottom-center" => ShapeTextAnchor.Bottom,
            _ => ShapeTextAnchor.Top
        };

        foreach (var child in textbox.Element(W.Main + "txbxContent")?.Elements() ?? [])
        {
            if (child.Name == W.Main + "p") shape.Content.Add(DocumentParser.ParseParagraph(child));
            else if (child.Name == W.Main + "tbl") shape.Content.Add(DocumentParser.ParseTable(child));
        }

        return shape;
    }

    /// <summary>
    /// How far down and to the right of its own box Word draws an old-style shape.
    /// </summary>
    /// <remarks>
    /// It does, and by an amount that steps rather than grows: <c>vml-stroke-probe</c> holds the
    /// same rectangle ten times over, varying nothing but the weight of its outline, and Word's
    /// export puts it at
    ///
    ///   none, ¼pt, ½pt, ¾pt, 1pt  -> no offset at all
    ///   1½pt, 2pt, 3pt            -> two points
    ///   4½pt                      -> four points
    ///   6pt                       -> six points
    ///
    /// which is to say the offset is the smallest even number of points that reaches a point
    /// short of the outline's weight. Why it steps in twos, and why it starts a whole point in,
    /// is not explained here: this is the rule the measurements fit, not one derived from
    /// anything. The text inside moves by half as much again, which is what
    /// <c>vml-inset-probe</c> shows — its six point page sets its text at the very edge of the
    /// box, where the inset alone would put it three points inside.
    ///
    /// None of it shows in an ordinary document. Word draws a text box with a ¾pt outline and a
    /// table rule with a ½pt one, and everything at a point or less is offset by nothing.
    /// </remarks>
    private static double DrawnOffset(double strokeWeightPoints) =>
        strokeWeightPoints <= 1 ? 0 : 2 * Math.Ceiling((strokeWeightPoints - 1) / 2);

    /// <summary>
    /// How the text goes round it, from the <c>w10:wrap</c> beside the shape. A shape that
    /// declares none does not part the text at all: it sits over it, or behind it.
    /// </summary>
    private static TextWrapMode ReadWrap(XElement element) =>
        element.Element(Word + "wrap")?.Attribute("type")?.Value switch
        {
            "square" or "tight" or "through" => TextWrapMode.Square,
            "topAndBottom" => TextWrapMode.TopAndBottom,
            _ => TextWrapMode.None
        };

    private static long WrapDistance(Dictionary<string, string> style, string name, double fallback) =>
        (long)Math.Round(Units.PointsToEmu(Length(style.GetValueOrDefault(name)) ?? fallback));

    private static long? Offset(Dictionary<string, string> style, params string[] names)
    {
        foreach (var name in names)
        {
            if (Length(style.GetValueOrDefault(name)) is { } value)
                return (long)Math.Round(Units.PointsToEmu(value));
        }

        return null;
    }

    private static HorizontalAnchor Relative(string? value) => value switch
    {
        "margin" => HorizontalAnchor.Margin,
        "page" => HorizontalAnchor.Page,
        "char" => HorizontalAnchor.Character,
        _ => HorizontalAnchor.Column
    };

    private static VerticalAnchor VerticalRelative(string? value) => value switch
    {
        "margin" => VerticalAnchor.Margin,
        "page" => VerticalAnchor.Page,
        "line" => VerticalAnchor.Line,
        _ => VerticalAnchor.Paragraph
    };

    /// <summary>Splits a CSS-like style attribute into the properties it declares.</summary>
    private static Dictionary<string, string> ReadStyle(string? style)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (style is null) return properties;

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;

            properties[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
        }

        return properties;
    }

    /// <summary>
    /// The four insets a text box declares, as points, in the order VML writes them: left, top,
    /// right, bottom. Any it leaves out keeps its default, which is what an empty one means too.
    /// </summary>
    private static double[] ReadInset(string? inset)
    {
        var values = (double[])DefaultInset.Clone();
        if (inset is null) return values;

        var parts = inset.Split(',');
        for (var i = 0; i < Math.Min(4, parts.Length); i++)
        {
            if (parts[i].Trim().Length > 0 && Length(parts[i]) is { } value) values[i] = value;
        }

        return values;
    }

    /// <summary>
    /// A CSS length in points. A number with no unit at all is in pixels, which is what CSS says
    /// and what a shape written by hand rather than by Word tends to carry.
    /// </summary>
    public static double? Length(string? value)
    {
        if (value is null) return null;

        var text = value.Trim();
        if (text.Length == 0) return null;

        var digits = text.Length;
        while (digits > 0 && !char.IsAsciiDigit(text[digits - 1]) && text[digits - 1] != '.') digits--;

        var unit = text[digits..].Trim().ToLowerInvariant();

        if (!double.TryParse(text[..digits], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return null;

        return unit switch
        {
            "pt" => number,
            "in" => number * 72,
            "cm" => number * 72 / 2.54,
            "mm" => number * 72 / 25.4,
            "pc" => number * 12,
            "em" => number * 12,
            "px" or "" => number * 0.75,
            _ => number
        };
    }

    /// <summary>
    /// A VML colour: a hexadecimal one, or one of the names the format defines.
    /// </summary>
    /// <remarks>
    /// Only the handful of names Word actually writes are here — it writes hexadecimal for
    /// everything a user chose, and a name only for the ends of the scale.
    /// </remarks>
    private static DrawingColorReference? Color(string? value)
    {
        if (value is null) return null;

        var text = value.Trim();

        // A colour may carry the theme slot it came from after the value itself, which is the
        // value it resolved to and is what to draw with.
        var space = text.IndexOf(' ');
        if (space > 0) text = text[..space];

        if (text.StartsWith('#')) text = text[1..];

        if (text.Length == 3 && text.All(Uri.IsHexDigit))
            text = string.Concat(text.Select(c => new string(c, 2)));

        if (text.Length == 6 && text.All(Uri.IsHexDigit))
            return new DrawingColorReference(text.ToUpperInvariant(), null);

        return text.ToLowerInvariant() switch
        {
            "white" or "window" => new DrawingColorReference("FFFFFF", null),
            "black" or "windowtext" => new DrawingColorReference("000000", null),
            "red" => new DrawingColorReference("FF0000", null),
            "green" => new DrawingColorReference("008000", null),
            "blue" => new DrawingColorReference("0000FF", null),
            "yellow" => new DrawingColorReference("FFFF00", null),
            "gray" or "grey" => new DrawingColorReference("808080", null),
            "silver" => new DrawingColorReference("C0C0C0", null),
            "none" => null,
            _ => null
        };
    }
}
