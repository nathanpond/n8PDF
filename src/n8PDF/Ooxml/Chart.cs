using System.Globalization;
using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>What a chart is drawn as.</summary>
public enum ChartKind
{
    /// <summary>Bars standing up from the category axis.</summary>
    Column,

    /// <summary>Bars lying along it.</summary>
    Bar,

    Line,

    Pie,

    /// <summary>A line filled down to the axis.</summary>
    Area,

    /// <summary>Pairs of numbers rather than a value against a category.</summary>
    Scatter
}

/// <summary>
/// What a series draws at each of its points, where it draws anything.
/// </summary>
/// <param name="Symbol">
/// Its shape: "circle", "square", "diamond", "triangle", "x", "star", "plus", "dot", "dash",
/// or "none" where the series asks for nothing at all.
/// </param>
/// <param name="SizePoints">How large, across the whole of it.</param>
public sealed record ChartMarker(
    string Symbol,
    double SizePoints,
    DrawingColorReference? Fill,
    DrawingColorReference? Line,
    double LineWidthPoints);

/// <summary>How the bars of one category stand against each other.</summary>
public enum ChartGrouping
{
    /// <summary>Side by side, each from the axis.</summary>
    Clustered,

    /// <summary>One on top of the next, each from where the last ended.</summary>
    Stacked,

    /// <summary>The same, but each category filled out to the whole.</summary>
    PercentStacked,

    /// <summary>What a line chart says instead, where nothing is stacked at all.</summary>
    Standard
}

/// <summary>One series: its name, and what it holds against each category.</summary>
/// <param name="Values">
/// One for each category, or null where the series has nothing for that one — a gap in the data
/// is not a nought, and is not drawn as one.
/// </param>
public sealed record ChartSeries(
    string Name,
    IReadOnlyList<string> Categories,
    IReadOnlyList<double?> Values,
    DrawingColorReference? Fill)
{
    /// <summary>
    /// What each point is painted in, where the series says so point by point. A pie says it that
    /// way, since its points are its slices and one colour would make it a disc.
    /// </summary>
    public IReadOnlyDictionary<int, DrawingColorReference?> PointFills { get; init; } =
        new Dictionary<int, DrawingColorReference?>();

    /// <summary>What a line is drawn in, and how thick.</summary>
    public DrawingColorReference? Line { get; init; }

    public double LineWidthPoints { get; init; } = 2.25;

    /// <summary>
    /// True where the series asks for no line at all, which is not the same as saying nothing
    /// about one: a scatter of markers alone says it outright.
    /// </summary>
    public bool NoLine { get; init; }

    /// <summary>
    /// Whether the line curves through its points rather than going straight between them. It
    /// does unless told not to, which is the format's own default and not an obvious one.
    /// </summary>
    public bool Smooth { get; init; } = true;

    /// <summary>
    /// Whether a bar that hangs below nought is drawn the other way about, which is what the
    /// format asks for unless the series says otherwise.
    /// </summary>
    public bool InvertIfNegative { get; init; } = true;

    /// <summary>
    /// What it holds along the other axis, where the series is a set of pairs rather than a value
    /// against a category. Empty for everything but a scatter.
    /// </summary>
    public IReadOnlyList<double?> XValues { get; init; } = [];

    /// <summary>What it draws at each point, or null where the series says nothing about it.</summary>
    public ChartMarker? Marker { get; init; }
}

/// <summary>An axis, and what it says about the scale it draws.</summary>
public sealed class ChartAxis
{
    public long Id { get; set; }

    /// <summary>Where it runs: "l", "r", "t" or "b".</summary>
    public string Position { get; set; } = "l";

    /// <summary>True where the axis is not drawn at all, which is what <c>c:delete</c> asks.</summary>
    public bool Deleted { get; set; }

    public bool MajorGridlines { get; set; }

    public double? Minimum { get; set; }

    public double? Maximum { get; set; }

    public double? MajorUnit { get; set; }

    /// <summary>True for a value axis, false for one that runs over the categories.</summary>
    public bool IsValueAxis { get; set; }

    /// <summary>Whether the marks across it are drawn, and on which side.</summary>
    public string MajorTickMark { get; set; } = "out";

    /// <summary>Where the labels go, or "none" where there are none.</summary>
    public string TickLabelPosition { get; set; } = "nextTo";

    /// <summary>
    /// The type its labels are set in. Ten point is what Word uses where the axis says nothing,
    /// which is what its export of a chart carrying no text properties at all comes out at.
    /// </summary>
    public double LabelSizePoints { get; set; } = 10;

    /// <summary>
    /// How far the labels sit from the axis, as a percentage of a step the format does not name.
    /// A hundred is the usual, and what an axis saying nothing means.
    /// </summary>
    public int LabelOffset { get; set; } = 100;

    /// <summary>
    /// How its numbers are written, as a spreadsheet's format code. Null where the axis says
    /// nothing, which means whole numbers written plainly.
    /// </summary>
    public string? NumberFormat { get; set; }

    /// <summary>
    /// Whether the other axis crosses this one between its categories or at the middle of one.
    /// It is what decides where a line's points and an area's corners go: "between" puts them at
    /// the middles of the categories, "midCat" at the marks, so that the first and last touch the
    /// ends of the plot.
    /// </summary>
    public string CrossBetween { get; set; } = "between";
}

/// <summary>
/// Where a chart puts something as a fraction of the whole: the layout a chart states by hand
/// rather than leaving to be worked out.
/// </summary>
public sealed record ChartLayout(double X, double Y, double Width, double Height);

/// <summary>A chart, as its part describes it.</summary>
public sealed class ChartDefinition
{
    public ChartKind Kind { get; set; } = ChartKind.Column;

    /// <summary>Whether the bars stand beside each other or on top of one another.</summary>
    public ChartGrouping Grouping { get; set; } = ChartGrouping.Clustered;

    /// <summary>True where the value axis runs along the foot rather than up the side.</summary>
    public bool Lying => Kind == ChartKind.Bar;

    /// <summary>
    /// True where the chart holds pairs of numbers, so that both of its axes are value axes and
    /// both have to be scaled.
    /// </summary>
    public bool Paired => Kind == ChartKind.Scatter;

    /// <summary>
    /// How a scatter is drawn: "none", "line", "lineMarker", "marker", "smooth" or "smoothMarker".
    /// </summary>
    public string ScatterStyle { get; set; } = "lineMarker";

    public List<ChartSeries> Series { get; } = [];

    public ChartAxis? CategoryAxis { get; set; }

    public ChartAxis? ValueAxis { get; set; }

    /// <summary>Where the plotting itself goes, where the chart says so outright.</summary>
    public ChartLayout? PlotArea { get; set; }

    /// <summary>
    /// How wide the gap between one category's bars and the next is, as a percentage of one bar.
    /// </summary>
    public int GapWidth { get; set; } = 150;

    /// <summary>
    /// How far the bars of one category overlap each other, as a percentage of a bar; negative
    /// parts them.
    /// </summary>
    public int Overlap { get; set; }

    /// <summary>
    /// Where a pie begins, clockwise from the top, in degrees.
    /// </summary>
    public int FirstSliceAngle { get; set; }

    /// <summary>The categories, taken from the first series that names any.</summary>
    public IReadOnlyList<string> Categories =>
        Series.FirstOrDefault(series => series.Categories.Count > 0)?.Categories ?? [];
}

/// <summary>
/// Reads a chart part.
/// </summary>
/// <remarks>
/// A chart is the one thing a document describes only as data. There is no drawing of it anywhere:
/// what the part holds is the numbers, the axes and the formatting, and every reader works out the
/// picture for itself. The numbers are written twice — as a formula naming cells in a workbook
/// stored alongside, and as a cache of what those cells last held — and it is the cache that is
/// read here, since the workbook is a spreadsheet and answering it would mean being one.
/// </remarks>
public static class ChartReader
{
    public static readonly XNamespace Main = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public static ChartDefinition? Parse(XDocument? part)
    {
        var chart = part?.Root?.Element(Main + "chart");
        var plotArea = chart?.Element(Main + "plotArea");
        if (plotArea is null) return null;

        var definition = new ChartDefinition
        {
            PlotArea = ReadLayout(plotArea.Element(Main + "layout"))
        };

        var plot = plotArea.Element(Main + "barChart")
                   ?? plotArea.Element(Main + "lineChart")
                   ?? plotArea.Element(Main + "pieChart")
                   ?? plotArea.Element(Main + "areaChart")
                   ?? plotArea.Element(Main + "scatterChart");

        if (plot is null) return null;

        definition.Kind = plot.Name.LocalName switch
        {
            "lineChart" => ChartKind.Line,
            "pieChart" => ChartKind.Pie,
            "areaChart" => ChartKind.Area,
            "scatterChart" => ChartKind.Scatter,
            _ => plot.Element(Main + "barDir")?.Attribute("val")?.Value == "bar"
                ? ChartKind.Bar
                : ChartKind.Column
        };

        definition.ScatterStyle =
            plot.Element(Main + "scatterStyle")?.Attribute("val")?.Value ?? "lineMarker";

        definition.Grouping = plot.Element(Main + "grouping")?.Attribute("val")?.Value switch
        {
            "stacked" => ChartGrouping.Stacked,
            "percentStacked" => ChartGrouping.PercentStacked,
            "standard" => ChartGrouping.Standard,
            _ => ChartGrouping.Clustered
        };

        definition.GapWidth = Integer(plot.Element(Main + "gapWidth")) ?? 150;
        definition.Overlap = Integer(plot.Element(Main + "overlap")) ?? 0;
        definition.FirstSliceAngle = Integer(plot.Element(Main + "firstSliceAng")) ?? 0;

        foreach (var series in plot.Elements(Main + "ser"))
            definition.Series.Add(ReadSeries(series));

        foreach (var axis in plotArea.Elements())
        {
            if (axis.Name == Main + "catAx" || axis.Name == Main + "dateAx")
            {
                definition.CategoryAxis = ReadAxis(axis, isValue: false);
            }
            else if (axis.Name == Main + "valAx")
            {
                var read = ReadAxis(axis, isValue: true);

                // A scatter has two of them, and which is which is only said by where each runs:
                // the one along the foot stands where a chart of categories keeps its categories.
                if (definition.Paired && read.Position is "b" or "t") definition.CategoryAxis = read;
                else definition.ValueAxis = read;
            }
        }

        return definition;
    }

    private static ChartSeries ReadSeries(XElement element)
    {
        var name = element.Element(Main + "tx") is { } tx
            ? Strings(tx).FirstOrDefault() ?? string.Empty
            : string.Empty;

        var categories = element.Element(Main + "cat") is { } cat ? Strings(cat) : [];

        var values = element.Element(Main + "val") is { } val
            ? Numbers(val)
            : element.Element(Main + "yVal") is { } y ? Numbers(y) : [];

        var properties = element.Element(Main + "spPr");
        var line = properties?.Element(W.Drawing + "ln");

        var points = new Dictionary<int, DrawingColorReference?>();
        foreach (var point in element.Elements(Main + "dPt"))
        {
            if (Integer(point.Element(Main + "idx")) is not { } index) continue;

            points[index] = DrawingText.ReadFill(point.Element(Main + "spPr"));
        }

        return new ChartSeries(name, categories, values, DrawingText.ReadFill(properties))
        {
            PointFills = points,
            Line = DrawingText.ReadFill(line),
            NoLine = line?.Element(W.Drawing + "noFill") is not null,
            LineWidthPoints = line?.Attribute("w")?.Value is { } width && long.TryParse(width, out var emu)
                ? Units.EmuToPoints(emu)
                : 2.25,

            // A line curves through its points unless the series says otherwise. Word writes the
            // element on every line chart it makes; one that leaves it out gets the curve.
            Smooth = element.Element(Main + "smooth")?.Attribute("val")?.Value is not ("0" or "false"),

            InvertIfNegative =
                element.Element(Main + "invertIfNegative")?.Attribute("val")?.Value
                    is not ("0" or "false"),

            XValues = element.Element(Main + "xVal") is { } x ? Numbers(x) : [],
            Marker = ReadMarker(element.Element(Main + "marker"))
        };
    }

    /// <summary>
    /// What a series draws at its points. Five points of a Word default, and everything about it
    /// stated: a series that says nothing gets null and is drawn the way Word draws one.
    /// </summary>
    private static ChartMarker? ReadMarker(XElement? element)
    {
        if (element is null) return null;

        var properties = element.Element(Main + "spPr");
        var line = properties?.Element(W.Drawing + "ln");

        return new ChartMarker(
            element.Element(Main + "symbol")?.Attribute("val")?.Value ?? "auto",
            Integer(element.Element(Main + "size")) ?? 5,
            DrawingText.ReadFill(properties),
            DrawingText.ReadFill(line),
            line?.Attribute("w")?.Value is { } width && long.TryParse(width, out var emu)
                ? Units.EmuToPoints(emu)
                : 0.75);
    }

    private static ChartAxis ReadAxis(XElement element, bool isValue)
    {
        var scaling = element.Element(Main + "scaling");

        return new ChartAxis
        {
            Id = long.TryParse(element.Element(Main + "axId")?.Attribute("val")?.Value, out var id) ? id : 0,
            Position = element.Element(Main + "axPos")?.Attribute("val")?.Value ?? (isValue ? "l" : "b"),
            Deleted = element.Element(Main + "delete")?.Attribute("val")?.Value is "1" or "true",
            MajorGridlines = element.Element(Main + "majorGridlines") is not null,
            Minimum = Number(scaling?.Element(Main + "min")),
            Maximum = Number(scaling?.Element(Main + "max")),
            MajorUnit = Number(element.Element(Main + "majorUnit")),
            IsValueAxis = isValue,
            MajorTickMark = element.Element(Main + "majorTickMark")?.Attribute("val")?.Value ?? "out",
            TickLabelPosition = element.Element(Main + "tickLblPos")?.Attribute("val")?.Value ?? "nextTo",
            LabelSizePoints = LabelSize(element) ?? 10,
            LabelOffset = Integer(element.Element(Main + "lblOffset")) ?? 100,
            NumberFormat = element.Element(Main + "numFmt")?.Attribute("formatCode")?.Value,
            CrossBetween = element.Element(Main + "crossBetween")?.Attribute("val")?.Value
                           ?? "between"
        };
    }

    /// <summary>
    /// The type an axis sets its labels in, from the text properties it carries. Hundredths of a
    /// point, as everything in DrawingML is.
    /// </summary>
    private static double? LabelSize(XElement axis)
    {
        var size = axis.Element(Main + "txPr")?
            .Descendants(W.Drawing + "defRPr").FirstOrDefault()?
            .Attribute("sz")?.Value;

        return double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value / 100
            : null;
    }

    /// <summary>
    /// A layout stated by hand, as fractions of the chart. Only the inner one is read: it is the
    /// plotting itself, without the axis labels around it, and is the only one that says where the
    /// bars go rather than where everything does.
    /// </summary>
    private static ChartLayout? ReadLayout(XElement? layout)
    {
        var manual = layout?.Element(Main + "manualLayout");
        if (manual is null) return null;

        if (manual.Element(Main + "layoutTarget")?.Attribute("val")?.Value != "inner") return null;

        var x = Number(manual.Element(Main + "x"));
        var y = Number(manual.Element(Main + "y"));
        var width = Number(manual.Element(Main + "w"));
        var height = Number(manual.Element(Main + "h"));

        return x is null || y is null || width is null || height is null
            ? null
            : new ChartLayout(x.Value, y.Value, width.Value, height.Value);
    }

    /// <summary>The strings a reference caches, in the order their indices give them.</summary>
    private static List<string> Strings(XElement container)
    {
        var cache = container.Descendants(Main + "strCache").FirstOrDefault()
                    ?? container.Descendants(Main + "numCache").FirstOrDefault();

        return cache is null ? [] : [.. Points(cache).Select(value => value ?? string.Empty)];
    }

    private static List<double?> Numbers(XElement container)
    {
        var cache = container.Descendants(Main + "numCache").FirstOrDefault();

        return cache is null
            ? []
            : [.. Points(cache).Select(value =>
                value is not null &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : (double?)null)];
    }

    /// <summary>
    /// The cached points of a reference, filled out to the count it declares. They are numbered
    /// rather than listed, and a series with a hole in it leaves one out.
    /// </summary>
    private static List<string?> Points(XElement cache)
    {
        var count = Integer(cache.Element(Main + "ptCount")) ?? 0;
        var values = new List<string?>(new string?[Math.Max(0, count)]);

        foreach (var point in cache.Elements(Main + "pt"))
        {
            if (!int.TryParse(point.Attribute("idx")?.Value, out var index)) continue;
            if (index < 0) continue;

            while (values.Count <= index) values.Add(null);

            values[index] = point.Element(Main + "v")?.Value;
        }

        return values;
    }

    private static int? Integer(XElement? element) =>
        int.TryParse(element?.Attribute("val")?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? Number(XElement? element) =>
        double.TryParse(element?.Attribute("val")?.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
