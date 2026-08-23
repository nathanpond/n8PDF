using System.Globalization;
using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>What a chart is drawn as.</summary>
internal enum ChartKind
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
    Scatter,

    /// <summary>A pie with a hole through the middle, and one ring for every series.</summary>
    Doughnut,

    /// <summary>Pairs of numbers again, with a third saying how large a bubble to draw.</summary>
    Bubble,

    /// <summary>The categories set round a circle, and the values measured out from its middle.</summary>
    Radar,

    /// <summary>
    /// Three or four series read together as one day's trading, drawn as the lines between them
    /// rather than as lines along them.
    /// </summary>
    Stock
}

/// <summary>
/// The bar a stock chart draws between the price a day opened at and the price it closed at.
/// </summary>
/// <param name="Up">What it is drawn in where the day closed higher than it opened.</param>
/// <param name="Down">And where it closed lower.</param>
internal sealed record ChartUpDownBars(
    int GapWidth,
    DrawingColorReference? Up, DrawingColorReference? UpLine,
    DrawingColorReference? Down, DrawingColorReference? DownLine);

/// <summary>
/// What a series draws at each of its points, where it draws anything.
/// </summary>
/// <param name="Symbol">
/// Its shape: "circle", "square", "diamond", "triangle", "x", "star", "plus", "dot", "dash",
/// or "none" where the series asks for nothing at all.
/// </param>
/// <param name="SizePoints">How large, across the whole of it.</param>
internal sealed record ChartMarker(
    string Symbol,
    double SizePoints,
    DrawingColorReference? Fill,
    DrawingColorReference? Line,
    double LineWidthPoints);

/// <summary>How the bars of one category stand against each other.</summary>
internal enum ChartGrouping
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

/// <summary>How a trendline follows the points it is drawn through.</summary>
internal enum ChartTrendlineKind
{
    Linear,
    Polynomial,
    Exponential,
    Logarithmic,
    Power,

    /// <summary>Not a fit at all, but the mean of each run of points as it goes.</summary>
    MovingAverage
}

/// <summary>
/// A line drawn through a series' points saying what they tend towards.
/// </summary>
/// <remarks>
/// A series may carry more than one — the format allows it, and a chart comparing a straight fit
/// against a curved one is the reason to.
/// </remarks>
/// <param name="Order">The degree of a polynomial, which the format bounds to 2..6.</param>
/// <param name="Period">How many points a moving average takes the mean of.</param>
/// <param name="Forward">How far past the last point it runs, in categories.</param>
/// <param name="Backward">How far before the first it runs.</param>
/// <param name="Intercept">Where it is forced to cross, or null where it is free.</param>
internal sealed record ChartTrendline(
    ChartTrendlineKind Kind,
    int Order,
    int Period,
    double Forward,
    double Backward,
    double? Intercept,
    DrawingColorReference? Line,
    double LineWidthPoints);

/// <summary>Which way an error bar reaches from its point.</summary>
internal enum ChartErrorDirection
{
    Value,
    Category
}

/// <summary>Which ends of the point an error bar reaches from.</summary>
internal enum ChartErrorSides
{
    Both,
    Plus,
    Minus
}

/// <summary>What decides how far an error bar reaches.</summary>
internal enum ChartErrorAmount
{
    /// <summary>The same distance at every point.</summary>
    Fixed,

    /// <summary>A share of the point's own value, so it grows with the point.</summary>
    Percentage,

    /// <summary>A multiple of the series' standard deviation — one distance for all of them.</summary>
    StandardDeviation,

    /// <summary>The series' standard error, which the stated value has no part in.</summary>
    StandardError,

    /// <summary>A distance stated for each point, and separately for each side of it.</summary>
    Custom
}

/// <summary>
/// The bars reaching either side of a series' points, saying how far the numbers might be out.
/// </summary>
/// <param name="Value">
/// What <see cref="Amount"/> is a measure of, where it takes one: the distance itself for a fixed
/// bar, the share for a percentage, the multiple for a standard deviation. Nothing for the other
/// two.
/// </param>
/// <param name="Plus">The distance above each point, where the bars are stated point by point.</param>
/// <param name="Minus">The distance below.</param>
internal sealed record ChartErrorBars(
    ChartErrorDirection Direction,
    ChartErrorSides Sides,
    ChartErrorAmount Amount,
    double Value,
    IReadOnlyList<double?> Plus,
    IReadOnlyList<double?> Minus,
    bool Capped,
    DrawingColorReference? Line,
    double LineWidthPoints);

/// <summary>One series: its name, and what it holds against each category.</summary>
/// <param name="Values">
/// One for each category, or null where the series has nothing for that one — a gap in the data
/// is not a nought, and is not drawn as one.
/// </param>
internal sealed record ChartSeries(
    string Name,
    IReadOnlyList<string> Categories,
    IReadOnlyList<double?> Values,
    DrawingColorReference? Fill)
{
    /// <summary>What is written at this series' points, where the series says so itself.</summary>
    public ChartLabels? Labels { get; init; }

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

    /// <summary>
    /// How large a bubble is drawn at each pair, where the series is a set of bubbles. Empty for
    /// everything else.
    /// </summary>
    public IReadOnlyList<double?> BubbleSizes { get; init; } = [];

    /// <summary>The lines drawn through these points saying what they tend towards.</summary>
    public IReadOnlyList<ChartTrendline> Trendlines { get; init; } = [];

    /// <summary>
    /// The bars reaching either side of these points. A series may carry two — one each way — so
    /// this is a list rather than the one the common case has.
    /// </summary>
    public IReadOnlyList<ChartErrorBars> ErrorBars { get; init; } = [];
}

/// <summary>
/// A title: a chart's own, or one of its axes'. What it holds is ordinary text, laid out by the
/// engine that lays out everything else.
/// </summary>
internal sealed class ChartTitle
{
    public IReadOnlyList<BlockElement> Paragraphs { get; init; } = [];

    /// <summary>Whether it is drawn over the plotting rather than given room of its own.</summary>
    public bool Overlay { get; init; }

    /// <summary>Where it is put by hand, as fractions of the chart, or null where it is not.</summary>
    public ChartLayout? Layout { get; init; }
}

/// <summary>Where a legend goes, and how its entries are set.</summary>
/// <param name="Position">"b", "t", "l", "r" or "tr".</param>
internal sealed record ChartLegend(string Position, bool Overlay, double LabelSizePoints)
{
    /// <summary>Where it is put by hand, as fractions of the chart, or null where it is not.</summary>
    public ChartLayout? Layout { get; init; }
}

/// <summary>What is written at each point, and where.</summary>
/// <param name="Position">
/// "outEnd", "inEnd", "ctr", "inBase", "bestFit", "l", "r", "t", "b", or empty where the chart
/// does not say and the kind of chart decides.
/// </param>
internal sealed record ChartLabels(
    bool Value, bool Percent, bool Category, bool SeriesName,
    string Position, string? NumberFormat, double SizePoints)
{
    /// <summary>True where there is anything at all to write.</summary>
    public bool Any => Value || Percent || Category || SeriesName;
}

/// <summary>An axis, and what it says about the scale it draws.</summary>
internal sealed class ChartAxis
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

    /// <summary>What is written alongside it, where it carries a title at all.</summary>
    public ChartTitle? Title { get; set; }

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
internal sealed record ChartLayout(double X, double Y, double Width, double Height);

/// <summary>A chart, as its part describes it.</summary>
/// <summary>
/// Where the eye is, for a chart drawn in three dimensions.
/// </summary>
/// <param name="RotationX">
/// How far the scene is tilted towards the viewer, in degrees. Word calls this the X rotation and
/// it raises the floor into view.
/// </param>
/// <param name="RotationY">How far it is turned about the upright, in degrees.</param>
/// <param name="DepthPercent">
/// How deep the scene runs, as a percentage of its width.
/// </param>
/// <param name="RightAngleAxes">
/// True where the axes are held at right angles to one another, which flattens the scene into an
/// oblique projection and makes <paramref name="Perspective"/> count for nothing.
/// </param>
/// <param name="Perspective">
/// How strongly the far side of the scene is foreshortened, in degrees, where the axes are not
/// held at right angles.
/// </param>
/// <remarks>
/// **The defaults are measured, and there are two sets of them.** Which set applies turns on
/// whether the document carries a <c>c:view3D</c> element at all, not on which child of it is
/// missing — so a chart stating nothing and a chart stating an empty <c>c:view3D</c> are drawn
/// differently by Word, and by this.
///
/// | | <c>c:view3D</c> absent | present, the child absent |
/// |---|---|---|
/// | <c>rotX</c> | 15 | 0 |
/// | <c>rotY</c> | 20 | 0 |
/// | <c>rAngAx</c> | false | true |
/// | <c>perspective</c> | 30 | 30 |
/// | <c>depthPercent</c> | 100 | 100 |
///
/// Measured rather than read off the schema, over four rounds against Word: a document whose pages
/// state nothing beside pages stating candidates, exported by Word and compared as rendered
/// pictures, since Word rasterises a three-dimensional plot and there is no geometry to read. The
/// page stating nothing came out pixel for pixel identical to the one stating 15, 20 and false, and
/// differed from 20 for <c>rotX</c> by 38% of its ink, from 30 for <c>rotY</c> by 62%, from 0 for
/// <c>perspective</c> by 41% and from 50 for <c>depthPercent</c> by 35%. An empty <c>c:view3D</c>
/// came out identical to one stating 0, 0 and **true**, and differed from 0, 0 and false by 37%.
///
/// Two things fell out of the same measurement that the issue this was built for had wrong. The
/// split is not by chart kind — a three-dimensional pie takes the same absent-element scene as a
/// bar, not a scene of its own. And a pie **ignores** <see cref="RotationY"/> and
/// <see cref="RightAngleAxes"/> altogether: turning it a further seventy degrees, or holding its
/// axes square, changed not one pixel. Its <see cref="RotationX"/> and <see cref="Perspective"/>
/// both count.
///
/// <see cref="Perspective"/> counts for nothing when <see cref="RightAngleAxes"/> is true, which is
/// measured too: 30 and 0 draw the same picture there.
/// </remarks>
/// <param name="HeightPercent">
/// How tall the box is as a percentage of its width, or **null** where the document does not say —
/// which is not the same as any number.
/// </param>
internal sealed record ChartScene(
    double RotationX,
    double RotationY,
    int DepthPercent,
    bool RightAngleAxes,
    double Perspective,
    double? HeightPercent = null)
{
    /// <summary>The scene of a chart carrying no <c>c:view3D</c> at all.</summary>
    public static ChartScene Unstated { get; } = new(15, 20, 100, false, 30);

    /// <summary>
    /// How tall the box is against its width, given the plot area it is drawn in.
    /// </summary>
    /// <param name="plotWidth">How wide the plot area is.</param>
    /// <param name="plotHeight">And how tall.</param>
    /// <remarks>
    /// **The absent element has no numeric default**, which is why <see cref="HeightPercent"/> is
    /// nullable rather than defaulted to a hundred. Where the document says nothing, Word makes the
    /// box as tall relative to its width as the **plot area** is — so what it falls back to depends
    /// on the chart and cannot be written as a constant. A hundred is the schema's default and the
    /// obvious guess, and it is a third out: it draws a box 114pt wide where Word draws one 172pt
    /// wide on <c>chart-3d-height-probe</c>'s baseline.
    ///
    /// Measured by solving for the ratio from Word's own drawing: 0.507, 1.027 and 2.012 where the
    /// element states 50, 100 and 200, and 2.007 for 200 in a plot area half again as tall — which
    /// is what says it replaces the plot area's shape rather than scaling it.
    /// </remarks>
    public double HeightOverWidth(double plotWidth, double plotHeight) =>
        HeightPercent is { } stated ? stated / 100
        : plotWidth > 0 ? plotHeight / plotWidth
        : 1;
}

internal sealed class ChartDefinition
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
    public bool Paired => Kind is ChartKind.Scatter or ChartKind.Bubble;

    /// <summary>True where the chart is a disc rather than a plot with axes round it.</summary>
    public bool Round => Kind is ChartKind.Pie or ChartKind.Doughnut;

    /// <summary>
    /// How much of a doughnut is hole, as a percentage of the whole of it. Half is what the
    /// format means by saying nothing, and what Word's own doughnuts are.
    /// </summary>
    public int HoleSize { get; set; } = 50;

    /// <summary>
    /// How large the bubbles of a bubble chart are drawn, as a percentage of what they would
    /// otherwise be.
    /// </summary>
    public int BubbleScale { get; set; } = 100;

    /// <summary>
    /// Whether a bubble's number is its width rather than its area, which is what the format
    /// means by saying nothing.
    /// </summary>
    public bool SizeIsWidth { get; set; }

    /// <summary>
    /// How a radar is drawn: "standard" for lines between its points, "marker" for lines with a
    /// mark at each, "filled" for the shape coloured in.
    /// </summary>
    public string RadarStyle { get; set; } = "standard";

    /// <summary>
    /// Whether a stock chart draws the line from the lowest of a day's series to the highest, and
    /// what it draws it in.
    /// </summary>
    public bool HighLowLines { get; set; }

    public DrawingColorReference? HighLowLine { get; set; }

    public double HighLowLineWidthPoints { get; set; } = LineWidthDefault;

    /// <summary>
    /// Whether the chart hangs a line from each point down to the category axis, and what it
    /// draws it in.
    /// </summary>
    public bool DropLines { get; set; }

    public DrawingColorReference? DropLine { get; set; }

    public double DropLineWidthPoints { get; set; } = LineWidthDefault;

    /// <summary>What it draws between the opening and the closing, where it draws one at all.</summary>
    public ChartUpDownBars? UpDownBars { get; set; }

    /// <summary>What a chart draws a line with where it says nothing about it.</summary>
    public const double LineWidthDefault = 0.5;

    /// <summary>
    /// How a scatter is drawn: "none", "line", "lineMarker", "marker", "smooth" or "smoothMarker".
    /// </summary>
    public string ScatterStyle { get; set; } = "lineMarker";

    /// <summary>What is written over the whole of it, where it says anything.</summary>
    public ChartTitle? Title { get; set; }

    /// <summary>Where the series are named, where they are named at all.</summary>
    public ChartLegend? Legend { get; set; }

    /// <summary>What is written at every point, where the chart says so for all of them.</summary>
    public ChartLabels? Labels { get; set; }

    public List<ChartSeries> Series { get; } = [];

    public ChartAxis? CategoryAxis { get; set; }

    public ChartAxis? ValueAxis { get; set; }

    /// <summary>Where the plotting itself goes, where the chart says so outright.</summary>
    public ChartLayout? PlotArea { get; set; }

    /// <summary>
    /// Where the eye is, for a chart drawn in three dimensions, and null for one that is not.
    /// </summary>
    /// <remarks>
    /// This doubles as what says the chart *is* three-dimensional: <see cref="Kind"/> carries the
    /// nearest flat equivalent so that everything reading it goes on working, and a scene beside it
    /// is what distinguishes a <c>c:bar3DChart</c> from a <c>c:barChart</c>.
    /// </remarks>
    public ChartScene? Scene { get; set; }

    /// <summary>The axis the series are arranged along, which only a three-dimensional chart has.</summary>
    public ChartAxis? DepthAxis { get; set; }

    /// <summary>
    /// How deep the gap between one series' row and the next is, as a percentage of one row.
    /// </summary>
    public int GapDepth { get; set; } = 150;

    /// <summary>
    /// What a three-dimensional bar is shaped like: "box", "cylinder", "cone", "pyramid", or the
    /// truncated forms "coneToMax" and "pyramidToMax".
    /// </summary>
    public string Shape { get; set; } = "box";

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
internal static class ChartReader
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

        // A title the chart has been told to forget is not drawn, whatever it still holds.
        if (chart!.Element(Main + "autoTitleDeleted")?.Attribute("val")?.Value is not ("1" or "true"))
            definition.Title = ReadTitle(chart.Element(Main + "title"));

        if (chart.Element(Main + "legend") is { } legend)
        {
            definition.Legend = new ChartLegend(
                legend.Element(Main + "legendPos")?.Attribute("val")?.Value ?? "r",
                legend.Element(Main + "overlay")?.Attribute("val")?.Value is "1" or "true",
                LabelSize(legend) ?? 10)
            {
                Layout = ReadPlacement(legend.Element(Main + "layout"))
            };
        }

        var plot = plotArea.Element(Main + "barChart")
                   ?? plotArea.Element(Main + "lineChart")
                   ?? plotArea.Element(Main + "pieChart")
                   ?? plotArea.Element(Main + "areaChart")
                   ?? plotArea.Element(Main + "scatterChart")
                   ?? plotArea.Element(Main + "doughnutChart")
                   ?? plotArea.Element(Main + "bubbleChart")
                   ?? plotArea.Element(Main + "radarChart")
                   ?? plotArea.Element(Main + "stockChart")

                   // The three-dimensional plots. Their children are the same as the flat ones' —
                   // c:ser, c:cat, c:val, c:barDir, c:grouping — so everything below reads them
                   // without knowing the difference, and the scene read beside them is what says
                   // there is one.
                   ?? plotArea.Element(Main + "bar3DChart")
                   ?? plotArea.Element(Main + "line3DChart")
                   ?? plotArea.Element(Main + "pie3DChart")
                   ?? plotArea.Element(Main + "area3DChart")
                   ?? plotArea.Element(Main + "surface3DChart")
                   ?? plotArea.Element(Main + "surfaceChart");

        if (plot is null) return null;

        definition.Kind = plot.Name.LocalName switch
        {
            "lineChart" => ChartKind.Line,
            "pieChart" => ChartKind.Pie,
            "areaChart" => ChartKind.Area,
            "scatterChart" => ChartKind.Scatter,
            "doughnutChart" => ChartKind.Doughnut,
            "bubbleChart" => ChartKind.Bubble,
            "radarChart" => ChartKind.Radar,
            "stockChart" => ChartKind.Stock,

            // The nearest flat equivalent, which is what the rest of the reader and everything
            // downstream works in. A surface has none — it is a mesh over a grid rather than a
            // series of points — so it takes Line to keep its series readable and is told apart by
            // the scene, not by this.
            "line3DChart" => ChartKind.Line,
            "pie3DChart" => ChartKind.Pie,
            "area3DChart" => ChartKind.Area,
            "surface3DChart" or "surfaceChart" => ChartKind.Line,
            _ => plot.Element(Main + "barDir")?.Attribute("val")?.Value == "bar"
                ? ChartKind.Bar
                : ChartKind.Column
        };

        // Three-dimensional or not, and where the eye is if it is. The scene is read from the
        // chart rather than the plot, since c:view3D is a child of c:chart.
        if (plot.Name.LocalName.Contains("3D", StringComparison.Ordinal) ||
            plot.Name.LocalName == "surfaceChart")
        {
            definition.Scene = ReadScene(chart.Element(Main + "view3D"));
            definition.GapDepth = Integer(plot.Element(Main + "gapDepth")) ?? 150;
            definition.Shape = plot.Element(Main + "shape")?.Attribute("val")?.Value ?? "box";
        }

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

        // A doughnut's hole, and how large its bubbles are, both as percentages: of the whole of
        // the disc for the one, and of what a bubble would be for the other.
        definition.HoleSize = Integer(plot.Element(Main + "holeSize")) ?? 50;
        definition.BubbleScale = Integer(plot.Element(Main + "bubbleScale")) ?? 100;
        definition.SizeIsWidth =
            plot.Element(Main + "sizeRepresents")?.Attribute("val")?.Value == "w";

        definition.RadarStyle =
            plot.Element(Main + "radarStyle")?.Attribute("val")?.Value ?? "standard";

        if (plot.Element(Main + "hiLowLines") is { } highLow)
        {
            // c:spPr, not a:spPr — the element belongs to the chart namespace and only the
            // a:ln inside it belongs to the drawing one. Looking for the wrong one finds
            // nothing and silently falls back to the default colour.
            var line = highLow.Element(Main + "spPr")?.Element(W.Drawing + "ln");

            definition.HighLowLines = true;
            definition.HighLowLine = DrawingText.ReadFill(line);
            definition.HighLowLineWidthPoints = Width(line) ?? ChartDefinition.LineWidthDefault;
        }

        if (plot.Element(Main + "dropLines") is { } drop)
        {
            var line = drop.Element(Main + "spPr")?.Element(W.Drawing + "ln");

            definition.DropLines = true;
            definition.DropLine = DrawingText.ReadFill(line);
            definition.DropLineWidthPoints = Width(line) ?? ChartDefinition.LineWidthDefault;
        }

        if (plot.Element(Main + "upDownBars") is { } bars)
        {
            // c:spPr, not a:spPr. The element belongs to the chart namespace and only the a:ln
            // inside it belongs to the drawing one — the same mistake this file made for
            // hiLowLines and dropLines, fixed in #75. Looking for the wrong one finds nothing and
            // falls silently back to the default colours.
            var up = bars.Element(Main + "upBars")?.Element(Main + "spPr");
            var down = bars.Element(Main + "downBars")?.Element(Main + "spPr");

            definition.UpDownBars = new ChartUpDownBars(
                Integer(bars.Element(Main + "gapWidth")) ?? 150,
                DrawingText.ReadFill(up), DrawingText.ReadFill(up?.Element(W.Drawing + "ln")),
                DrawingText.ReadFill(down), DrawingText.ReadFill(down?.Element(W.Drawing + "ln")));
        }
        definition.Overlap = Integer(plot.Element(Main + "overlap")) ?? 0;
        definition.FirstSliceAngle = Integer(plot.Element(Main + "firstSliceAng")) ?? 0;

        definition.Labels = ReadLabels(plot.Element(Main + "dLbls"), null);

        foreach (var series in plot.Elements(Main + "ser"))
            definition.Series.Add(ReadSeries(series, definition.Labels));

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
            else if (axis.Name == Main + "serAx")
            {
                // The axis the series are arranged along, which only a three-dimensional chart
                // has. It is a category axis in everything but where it points.
                definition.DepthAxis = ReadAxis(axis, isValue: false);
            }
        }

        return definition;
    }

    /// <summary>A title's text, or null where there is none to draw.</summary>
    /// <summary>
    /// Gives a title's runs the weight Word gives them where the part states none.
    /// </summary>
    /// <remarks>
    /// A chart title is **bold** unless it says otherwise, and so is an axis title — both measured
    /// from <c>chart-title-weight-probe</c>, whose first page states no weight at all: Word's own
    /// title of fourteen characters comes out 86.26pt wide against the 84.35 the same text
    /// measures regular, and its axis title 28.65 against 27.93. Nothing in the part carries that;
    /// it comes from Word's own styling of a chart, which a document does not ship.
    ///
    /// It is a default and not an override, which the probe's third page settles: a title stating
    /// <c>b="0"</c> is left regular by Word, and the axis title there agrees with ours to five
    /// hundredths of a point. So this fills in only what was never said — which is why
    /// <see cref="RunProperties.Bold"/> being nullable matters, and why this could not be done by
    /// setting the weight unconditionally.
    ///
    /// Applied here rather than in <c>DrawingText.Parse</c>, which also serves diagrams, shapes
    /// and text boxes — none of which wants a chart title's default.
    /// </remarks>
    private static void Embolden(IEnumerable<BlockElement> paragraphs)
    {
        foreach (var block in paragraphs)
        {
            if (block is not Paragraph paragraph) continue;

            foreach (var run in paragraph.Runs) run.Properties.Bold ??= true;
        }
    }

    private static ChartTitle? ReadTitle(XElement? element)
    {
        var rich = element?.Element(Main + "tx")?.Element(Main + "rich");
        if (rich is null) return null;

        var paragraphs = DrawingText.Parse(rich);
        if (paragraphs.Count == 0) return null;

        Embolden(paragraphs);

        return new ChartTitle
        {
            Paragraphs = paragraphs,
            Overlay = element!.Element(Main + "overlay")?.Attribute("val")?.Value is "1" or "true",
            Layout = ReadPlacement(element.Element(Main + "layout"))
        };
    }

    /// <summary>What is written at a set of points, or null where nothing is.</summary>
    private static ChartLabels? ReadLabels(XElement? element, ChartLabels? inherited)
    {
        if (element is null) return inherited;
        if (element.Element(Main + "delete")?.Attribute("val")?.Value is "1" or "true") return null;

        static bool Shown(XElement element, string name) =>
            element.Element(Main + name)?.Attribute("val")?.Value is "1" or "true";

        var labels = new ChartLabels(
            Shown(element, "showVal"),
            Shown(element, "showPercent"),
            Shown(element, "showCatName"),
            Shown(element, "showSerName"),
            element.Element(Main + "dLblPos")?.Attribute("val")?.Value ?? inherited?.Position ?? string.Empty,
            element.Element(Main + "numFmt")?.Attribute("formatCode")?.Value ?? inherited?.NumberFormat,
            LabelSize(element) ?? inherited?.SizePoints ?? 10);

        return labels.Any ? labels : inherited;
    }

    /// <summary>
    /// Reads a trendline: which curve it follows, how far it runs, and what it is drawn in.
    /// </summary>
    /// <remarks>
    /// The kind is the one thing here with no sensible default — a trendline that says nothing
    /// about its shape is a straight one, which is both the format's default and the only reading
    /// that draws anything at all.
    /// </remarks>
    private static ChartTrendline ReadTrendline(XElement element)
    {
        var kind = element.Element(Main + "trendlineType")?.Attribute("val")?.Value switch
        {
            "poly" => ChartTrendlineKind.Polynomial,
            "exp" => ChartTrendlineKind.Exponential,
            "log" => ChartTrendlineKind.Logarithmic,
            "power" => ChartTrendlineKind.Power,
            "movingAvg" => ChartTrendlineKind.MovingAverage,
            _ => ChartTrendlineKind.Linear
        };

        var line = element.Element(Main + "spPr")?.Element(W.Drawing + "ln");

        return new ChartTrendline(
            kind,

            // The format bounds a polynomial to 2..6 and a period to 2 or more. A file saying
            // otherwise is clamped rather than refused: the rest of the chart is still worth
            // drawing, and an order of nought is a horizontal line through nothing.
            Math.Clamp(Integer(element.Element(Main + "order")) ?? 2, 2, 6),
            Math.Max(2, Integer(element.Element(Main + "period")) ?? 2),
            Number(element.Element(Main + "forward")) ?? 0,
            Number(element.Element(Main + "backward")) ?? 0,
            Number(element.Element(Main + "intercept")),
            DrawingText.ReadFill(line),
            Width(line) ?? 2.25);
    }

    /// <summary>
    /// Reads a set of error bars: which way they reach, how far, and what they are drawn in.
    /// </summary>
    /// <remarks>
    /// Every default here is the format's own. Bars reach both ways and carry end caps unless the
    /// chart says otherwise, and a chart that names no kind of amount means a fixed one — which is
    /// also the only reading under which a stated value means anything.
    /// </remarks>
    private static ChartErrorBars ReadErrorBars(XElement element)
    {
        var line = element.Element(Main + "spPr")?.Element(W.Drawing + "ln");

        return new ChartErrorBars(
            element.Element(Main + "errDir")?.Attribute("val")?.Value == "x"
                ? ChartErrorDirection.Category
                : ChartErrorDirection.Value,

            element.Element(Main + "errBarType")?.Attribute("val")?.Value switch
            {
                "plus" => ChartErrorSides.Plus,
                "minus" => ChartErrorSides.Minus,
                _ => ChartErrorSides.Both
            },

            element.Element(Main + "errValType")?.Attribute("val")?.Value switch
            {
                "percentage" => ChartErrorAmount.Percentage,
                "stdDev" => ChartErrorAmount.StandardDeviation,
                "stdErr" => ChartErrorAmount.StandardError,
                "cust" => ChartErrorAmount.Custom,
                _ => ChartErrorAmount.Fixed
            },

            Number(element.Element(Main + "val")) ?? 0,
            element.Element(Main + "plus") is { } plus ? Numbers(plus) : [],
            element.Element(Main + "minus") is { } minus ? Numbers(minus) : [],

            // The element says there is *no* end cap, so its absence leaves them on.
            element.Element(Main + "noEndCap")?.Attribute("val")?.Value is not ("1" or "true"),

            DrawingText.ReadFill(line),
            Width(line) ?? 1);
    }

    private static ChartSeries ReadSeries(XElement element, ChartLabels? inherited = null)
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

        var trendlines = new List<ChartTrendline>();
        foreach (var trend in element.Elements(Main + "trendline"))
            trendlines.Add(ReadTrendline(trend));

        var errorBars = new List<ChartErrorBars>();
        foreach (var bars in element.Elements(Main + "errBars"))
            errorBars.Add(ReadErrorBars(bars));

        return new ChartSeries(name, categories, values, DrawingText.ReadFill(properties))
        {
            PointFills = points,
            Trendlines = trendlines,
            ErrorBars = errorBars,
            Line = DrawingText.ReadFill(line),
            NoLine = line?.Element(W.Drawing + "noFill") is not null,
            LineWidthPoints = Width(line) ?? 2.25,

            // A line curves through its points unless the series says otherwise. Word writes the
            // element on every line chart it makes; one that leaves it out gets the curve.
            Smooth = element.Element(Main + "smooth")?.Attribute("val")?.Value is not ("0" or "false"),

            InvertIfNegative =
                element.Element(Main + "invertIfNegative")?.Attribute("val")?.Value
                    is not ("0" or "false"),

            XValues = element.Element(Main + "xVal") is { } x ? Numbers(x) : [],
            BubbleSizes = element.Element(Main + "bubbleSize") is { } sizes ? Numbers(sizes) : [],
            Marker = ReadMarker(element.Element(Main + "marker")),
            Labels = ReadLabels(element.Element(Main + "dLbls"), inherited)
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

    /// <summary>
    /// Where the eye is, from a <c>c:view3D</c> that may not be there.
    /// </summary>
    /// <remarks>
    /// The two sets of defaults are the whole of why this is a method rather than five null
    /// coalescings at the call site: an absent element is not the same as an element whose children
    /// are absent, and Word draws the two differently. See <see cref="ChartScene"/> for the numbers
    /// and how they were measured.
    /// </remarks>
    private static ChartScene ReadScene(XElement? view)
    {
        if (view is null) return ChartScene.Unstated;

        return new ChartScene(
            Number(view.Element(Main + "rotX")) ?? 0,
            Number(view.Element(Main + "rotY")) ?? 0,
            Integer(view.Element(Main + "depthPercent")) ?? 100,

            // True where the element says nothing, which is the opposite of what an absent
            // c:view3D gives. Measured: an empty c:view3D draws the same picture as one stating
            // right-angle axes, and a picture 37% different from one refusing them.
            view.Element(Main + "rAngAx")?.Attribute("val")?.Value is not ("0" or "false"),

            Number(view.Element(Main + "perspective")) ?? 30,

            // Null where it is absent, and null is not a hundred: see ChartScene.HeightOverWidth.
            Number(view.Element(Main + "hPercent")));
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
            Title = ReadTitle(element.Element(Main + "title")),
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
    /// <summary>
    /// Where a title or a legend is put by hand, as fractions of the chart's own width and height.
    /// </summary>
    /// <remarks>
    /// Not <see cref="ReadLayout"/>, which serves the plot area and cannot serve these. That one
    /// insists on <c>layoutTarget="inner"</c>, an element only a plot area has, and on all four of
    /// x, y, w and h; a hand-placed title or legend states x and y and commonly lets its own size
    /// stand. Asking the plot area's reader for one of these returns null every time.
    /// </remarks>
    private static ChartLayout? ReadPlacement(XElement? layout)
    {
        var manual = layout?.Element(Main + "manualLayout");
        if (manual is null) return null;

        var x = Number(manual.Element(Main + "x"));
        var y = Number(manual.Element(Main + "y"));

        // Without both there is nowhere to put it, and the automatic placement is the better
        // answer than a corner.
        if (x is null || y is null) return null;

        return new ChartLayout(
            x.Value, y.Value,
            Number(manual.Element(Main + "w")) ?? 0,
            Number(manual.Element(Main + "h")) ?? 0);
    }

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

    /// <summary>How thick a line is, where it says: the format writes it in EMU.</summary>
    private static double? Width(XElement? line) =>
        line?.Attribute("w")?.Value is { } width && long.TryParse(width, out var emu)
            ? Units.EmuToPoints(emu)
            : null;

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
