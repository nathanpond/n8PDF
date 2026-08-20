using System.Globalization;
using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>
/// Reads a diagram — SmartArt — from the arrangement a document keeps of it.
/// </summary>
/// <remarks>
/// A diagram is described twice over. There is what it means: a list of points, the connections
/// between them, and a layout definition saying how points of that shape are to be arranged. And
/// there is the arrangement it last came to, kept in a drawing part beside the others as a flat
/// list of shapes at absolute positions, each with its geometry, its colours and its text.
///
/// The first is a language — a system of constraints and algorithms with a hundred layouts written
/// in it — and Word runs it afresh every time it opens a document. This reads the second, which is
/// what every reader that is not Word does, and what the part is there for. The two agree exactly
/// where the document was last saved by Word, since then the cache is Word's own answer; they
/// differ where something else wrote the file and got the arrangement wrong.
/// </remarks>
public static class Diagram
{
    /// <summary>The namespace of the drawing part, which is Word's own rather than the format's.</summary>
    public static readonly XNamespace Drawing =
        "http://schemas.microsoft.com/office/drawing/2008/diagram";

    /// <summary>The relationship a diagram's data part uses to reach that drawing.</summary>
    public const string DrawingRelationship =
        "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing";

    /// <summary>Reads the shapes of a cached arrangement, in the order they are drawn.</summary>
    public static List<DiagramShape> Parse(XDocument? drawing)
    {
        var shapes = new List<DiagramShape>();
        if (drawing?.Root is null) return shapes;

        foreach (var element in drawing.Root.Descendants(Drawing + "sp"))
        {
            if (ReadShape(element) is { } shape) shapes.Add(shape);
        }

        return shapes;
    }

    private static DiagramShape? ReadShape(XElement element)
    {
        var properties = element.Element(Drawing + "spPr");
        if (Frame(properties?.Element(W.Drawing + "xfrm")) is not { } box) return null;

        var shape = new ShapeFrame
        {
            Geometry = properties?.Element(W.Drawing + "prstGeom")?.Attribute("prst")?.Value ?? "rect",
            Fill = DrawingText.ReadFill(properties),
            Line = DrawingText.ReadFill(properties?.Element(W.Drawing + "ln"))
        };

        if (properties?.Element(W.Drawing + "ln")?.Attribute("w")?.Value is { } width &&
            long.TryParse(width, out var emu) && emu > 0)
        {
            shape.LineWidthPoints = Units.EmuToPoints(emu);
        }

        var body = element.Element(Drawing + "txBody");
        foreach (var paragraph in DrawingText.Parse(body)) shape.Content.Add(paragraph);

        shape.Anchor = body?.Element(W.Drawing + "bodyPr")?.Attribute("anchor")?.Value switch
        {
            "ctr" => ShapeTextAnchor.Center,
            "b" => ShapeTextAnchor.Bottom,
            _ => ShapeTextAnchor.Top
        };

        // Where the text goes is given outright rather than as an inset from the shape, since a
        // diagram works out for itself how much of an odd shape its words will fit in. A shape
        // that says nothing gets its whole box. What the body then insets from that is its own
        // affair, and a diagram's is wide — Word's is a fifth of an inch on every side, which is
        // what makes a word wrap that would otherwise fit.
        var text = Frame(element.Element(Drawing + "txXfrm")) ?? box;
        var inset = DrawingText.Insets(body);

        return new DiagramShape(shape, box.X, box.Y, box.Width, box.Height,
            text.X + inset.Left, text.Y + inset.Top,
            Math.Max(0, text.Width - inset.Left - inset.Right),
            Math.Max(0, text.Height - inset.Top - inset.Bottom));
    }

    /// <summary>Where a shape is and how big, in points, from an <c>a:xfrm</c>.</summary>
    private static (double X, double Y, double Width, double Height)? Frame(XElement? xfrm)
    {
        var offset = xfrm?.Element(W.Drawing + "off");
        var extent = xfrm?.Element(W.Drawing + "ext");

        if (offset is null || extent is null) return null;

        var width = Number(extent, "cx");
        var height = Number(extent, "cy");

        return width <= 0 || height <= 0
            ? null
            : (Units.EmuToPoints(Number(offset, "x")), Units.EmuToPoints(Number(offset, "y")),
                Units.EmuToPoints(width), Units.EmuToPoints(height));
    }

    private static double Number(XElement element, string name) =>
        double.TryParse(element.Attribute(name)?.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}

/// <summary>
/// Reads text written in DrawingML rather than in WordprocessingML.
/// </summary>
/// <remarks>
/// The two say the same things in different words. A paragraph is <c>a:p</c> rather than
/// <c>w:p</c>, a run's properties are attributes rather than elements, and a size is in hundredths
/// of a point rather than halves. What comes out is the ordinary paragraph model, so the engine
/// that lays out a page lays out the inside of a diagram's box as well.
/// </remarks>
public static class DrawingText
{
    /// <summary>
    /// What DrawingML counts as one line: six fifths of the type size, whatever the face says
    /// about its own. It is what a percentage of a line is a percentage of.
    /// </summary>
    private const double DrawingLineSpacing = 1.2;

    /// <summary>
    /// How far inside its own rectangle a body sets its text. The defaults are DrawingML's: a
    /// tenth of an inch at the sides and half of that above and below, the same as a text box.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) Insets(XElement? body)
    {
        var properties = body?.Element(W.Drawing + "bodyPr");

        return (Inset("lIns", 7.2), Inset("tIns", 3.6), Inset("rIns", 7.2), Inset("bIns", 3.6));

        double Inset(string name, double fallback) =>
            properties?.Attribute(name)?.Value is { } value && long.TryParse(value, out var emu)
                ? Units.EmuToPoints(emu)
                : fallback;
    }

    /// <summary>The paragraphs of a text body, empty where it holds none.</summary>
    public static List<BlockElement> Parse(XElement? body)
    {
        var blocks = new List<BlockElement>();
        if (body is null) return blocks;

        // A body may ask for its text to be set smaller so that it fits, which is a scale on
        // every size inside it rather than a size of its own.
        var scale = Percentage(
            body.Element(W.Drawing + "bodyPr")?
                .Element(W.Drawing + "normAutofit")?
                .Attribute("fontScale")?.Value) ?? 1;

        foreach (var element in body.Elements(W.Drawing + "p"))
            blocks.Add(ReadParagraph(element, scale));

        return blocks;
    }

    private static Paragraph ReadParagraph(XElement element, double scale)
    {
        var paragraph = new Paragraph();
        var properties = element.Element(W.Drawing + "pPr");

        paragraph.Properties.Justification = properties?.Attribute("algn")?.Value switch
        {
            "ctr" => Justification.Center,
            "r" => Justification.Right,
            "just" => Justification.Both,
            "dist" => Justification.Distribute,
            _ => Justification.Left
        };

        // A diagram's box holds what it holds; the spacing a document's own paragraphs get would
        // push the text out of it. What the paragraph says for itself is read below.
        paragraph.Properties.SpacingBeforeTwips = 0;
        paragraph.Properties.SpacingAfterTwips = 0;
        paragraph.Properties.Line = 240;
        paragraph.Properties.LineRule = LineSpacingRule.Auto;

        // Line spacing here is a percentage of the line rather than a multiple of it, which is
        // the same thing counted in hundreds instead of 240ths.
        if (Percentage(Spacing(properties, "lnSpc")) is { } line)
            paragraph.Properties.Line = (int)Math.Round(240 * line);

        // Space between paragraphs is a percentage too, of the type size rather than of the page,
        // so it can only be worked out once the size is known: see below.
        var before = Percentage(Spacing(properties, "spcBef"));
        var after = Percentage(Spacing(properties, "spcAft"));

        foreach (var child in element.Elements())
        {
            if (child.Name == W.Drawing + "r")
            {
                var run = new Run { Properties = ReadRunProperties(child.Element(W.Drawing + "rPr"), scale) };
                run.Content.Add(new TextInline(child.Element(W.Drawing + "t")?.Value ?? string.Empty));
                paragraph.Runs.Add(run);
            }
            else if (child.Name == W.Drawing + "br")
            {
                var run = new Run { Properties = ReadRunProperties(child.Element(W.Drawing + "rPr"), scale) };
                run.Content.Add(new BreakInline(BreakKind.Line));
                paragraph.Runs.Add(run);
            }
        }

        // A percentage of the line rather than of the type size, and a line in DrawingML is a
        // fixed six fifths of the type — it does not ask the face how tall its own is. Measured
        // from the diagram fixture, whose paragraphs Word sets 15.6pt apart where 35% of the type
        // size would be 12.6pt and 35% of six fifths of it is 15.1pt.
        var size = paragraph.Runs
            .Select(run => run.Properties.SizeHalfPoints ?? 0)
            .DefaultIfEmpty(0)
            .Max();

        var singleLine = size * 10 * DrawingLineSpacing;

        if (before is { } beforeShare)
            paragraph.Properties.SpacingBeforeTwips = (int)Math.Round(beforeShare * singleLine);

        if (after is { } afterShare)
            paragraph.Properties.SpacingAfterTwips = (int)Math.Round(afterShare * singleLine);

        return paragraph;
    }

    /// <summary>
    /// A spacing given as a percentage, which is the only one of the two spellings a diagram
    /// uses. The other, in points, is left alone.
    /// </summary>
    private static string? Spacing(XElement? properties, string name) =>
        properties?.Element(W.Drawing + name)?
            .Element(W.Drawing + "spcPct")?.Attribute("val")?.Value;

    private static RunProperties ReadRunProperties(XElement? rPr, double scale)
    {
        var properties = new RunProperties();
        if (rPr is null) return properties;

        // A hundredth of a point here, a half of one there.
        if (rPr.Attribute("sz")?.Value is { } size &&
            double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var hundredths))
        {
            properties.SizeHalfPoints = (int)Math.Round(hundredths * scale / 50);
        }

        if (rPr.Attribute("b")?.Value is { } bold) properties.Bold = bold is "1" or "true";
        if (rPr.Attribute("i")?.Value is { } italic) properties.Italic = italic is "1" or "true";
        if (rPr.Attribute("u")?.Value is { } underline && underline != "none")
            properties.Underline = UnderlineStyle.Single;

        if (rPr.Element(W.Drawing + "latin")?.Attribute("typeface")?.Value is { Length: > 0 } face)
        {
            properties.AsciiFont = face;
            properties.HighAnsiFont = face;
        }

        if (ReadFill(rPr) is { } color)
        {
            properties.Color = color.Hex;
            properties.ColorThemeSlot = color.ThemeSlot;
        }

        return properties;
    }

    /// <summary>
    /// The colour something is painted in: named outright, or as a slot in the theme.
    /// </summary>
    public static DrawingColorReference? ReadFill(XElement? container)
    {
        var solid = container?.Element(W.Drawing + "solidFill");
        if (solid is null) return null;

        if (solid.Element(W.Drawing + "srgbClr")?.Attribute("val")?.Value is { } hex)
            return new DrawingColorReference(hex, null);

        if (solid.Element(W.Drawing + "schemeClr")?.Attribute("val")?.Value is { } slot)
            return new DrawingColorReference(null, slot);

        // A colour may also be given as three percentages rather than three bytes, which is what
        // a diagram's own arrangement uses throughout: Word writes every colour it worked out
        // that way rather than resolving it back to a hexadecimal one.
        if (solid.Element(W.Drawing + "scrgbClr") is { } scrgb)
        {
            return new DrawingColorReference(
                $"{Channel(scrgb, "r")}{Channel(scrgb, "g")}{Channel(scrgb, "b")}", null);
        }

        // And a system colour carries what it last came to alongside the name of what it stands
        // for, which is the part worth having.
        if (solid.Element(W.Drawing + "sysClr")?.Attribute("lastClr")?.Value is { } system)
            return new DrawingColorReference(system, null);

        return null;
    }

    /// <summary>One channel of a colour written as a percentage, in hundred-thousandths.</summary>
    private static string Channel(XElement color, string name)
    {
        var value = color.Attribute(name)?.Value ?? "0";
        var text = value.TrimEnd('%');

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            number = 0;

        var share = value.EndsWith('%') ? number / 100 : number / 100000;

        return ((int)Math.Round(Math.Clamp(share, 0, 1) * 255)).ToString("X2");
    }

    /// <summary>A percentage in thousandths, which is how DrawingML writes one.</summary>
    private static double? Percentage(string? value)
    {
        if (value is null) return null;

        var text = value.TrimEnd('%');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return null;

        return value.EndsWith('%') ? number / 100 : number / 100000;
    }
}
