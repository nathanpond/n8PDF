using System.Xml.Linq;
using n8PDF.Images;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The four kinds of chart that are not a bar, a line, a pie, an area or a scatter: a doughnut, a
/// bubble chart, a radar and a stock chart.
/// </summary>
/// <remarks>
/// Each is described by the same kind of part as the others — numbers, axes and formatting, and no
/// drawing of anything — so each had to be measured against Word's own export in the same way. The
/// probes are chart-doughnut-bubble, chart-radar-stock, chart-kinds-probe, chart-kinds-probe-two
/// and chart-legend-key-probe; what each of them settles is written where the rule it settled is.
/// </remarks>
public class ChartKindTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    // ---- a doughnut -------------------------------------------------------------------------

    /// <summary>
    /// A doughnut is a pie with a hole through it, and the hole is a percentage of the whole disc.
    /// </summary>
    /// <remarks>
    /// Measured from the first three pages of chart-doughnut-bubble, whose plot is placed by hand
    /// at 216 by 172.8 — so the disc reaches 86.4 — and whose holes are a quarter, a half and
    /// three quarters: Word's rings come back to 21.6, 43.2 and 64.8.
    /// </remarks>
    [Theory]
    [InlineData(25, 21.6)]
    [InlineData(50, 43.2)]
    [InlineData(75, 64.8)]
    public void A_doughnut_s_hole_is_a_share_of_the_whole_disc(int hole, double inner)
    {
        var chart = Doughnut(hole);
        var plan = Arrange(chart);

        var bands = ChartComposer.Bands(chart, plan).ToList();

        var band = Assert.Single(bands);

        Assert.Equal(86.4, band.Outer, 3);
        Assert.Equal(inner, band.Inner, 3);
    }

    /// <summary>
    /// A doughnut of two series is two rings, and what the hole leaves is divided between them.
    /// </summary>
    /// <remarks>
    /// Word's export of the fourth page draws the first series from 43.2 to 64.8 and the second
    /// from 64.8 to 86.4: the first series is the inner ring.
    /// </remarks>
    [Fact]
    public void Rings_divide_what_the_hole_leaves()
    {
        var plan = Arrange(Doughnut(50, series: 2));
        var bands = ChartComposer.Bands(Doughnut(50, series: 2), plan).ToList();

        Assert.Equal(2, bands.Count);

        Assert.Equal(43.2, bands[0].Inner, 3);
        Assert.Equal(64.8, bands[0].Outer, 3);
        Assert.Equal(64.8, bands[1].Inner, 3);
        Assert.Equal(86.4, bands[1].Outer, 3);
    }

    /// <summary>
    /// A share written on a ring sits at the middle of that ring, whatever size it is set in.
    /// </summary>
    /// <remarks>
    /// Measured from the eighth page of chart-kinds-probe and the fifth and sixth of
    /// chart-kinds-probe-two: the labels of a ring running 43.2 to 86.4 land 64.8 from the middle
    /// at ten point, at fourteen and at twenty alike, and on the middle of their own slice.
    /// </remarks>
    [Fact]
    public void A_share_written_on_a_ring_sits_at_the_middle_of_it()
    {
        var chart = Doughnut(50, labels: true);
        var plan = Arrange(chart);

        var centre = plan.Middle;

        var labels = ChartComposer.DataLabels(chart, plan, size => (size * 0.952, size * 0.269))
            .ToList();

        Assert.Equal(4, labels.Count);

        // The first slice is a fifth of the whole and begins at the top, so its middle is at 36°.
        var first = labels[0];

        var distance = Math.Sqrt(Math.Pow(first.X - centre.X, 2) +
                                 Math.Pow(first.Baseline - 3.415 - centre.Y, 2));

        Assert.Equal(64.8, distance, 1);

        var angle = Math.Atan2(first.X - centre.X, centre.Y - (first.Baseline - 3.415)) * 180 /
                    Math.PI;

        Assert.Equal(36, angle, 1);
    }

    /// <summary>A doughnut names its slices in its legend, as a pie does, rather than its series.</summary>
    [Fact]
    public void A_doughnut_names_its_slices()
    {
        var chart = Doughnut(50);
        chart.Legend = new ChartLegend("r", false, 10);

        var entries = ChartComposer.Entries(chart, chart.Legend, (text, size) => text.Length * size * 0.5);

        Assert.Equal(["One", "Two", "Three", "Four"], entries.Select(entry => entry.Text));
    }

    // ---- a bubble chart ---------------------------------------------------------------------

    /// <summary>
    /// How large the largest bubble of a chart comes out: the shorter side of the frame less ten
    /// points, taken at the scale the chart asks for.
    /// </summary>
    /// <remarks>
    /// Every number here is Word's, measured from the bubble pages of chart-doughnut-bubble,
    /// chart-kinds-probe and chart-kinds-probe-two — seven scales on one frame and three frames at
    /// the scale a chart means by saying nothing. It is the frame that decides it and not the
    /// plotting: the page whose plot is 108 by 64.8 draws the same bubbles as the page whose plot
    /// fills the frame.
    /// </remarks>
    [Theory]
    [InlineData(360, 216, 25, 14.372)]
    [InlineData(360, 216, 50, 26.870)]
    [InlineData(360, 216, 75, 37.836)]
    [InlineData(360, 216, 100, 47.538)]
    [InlineData(360, 216, 150, 63.932)]
    [InlineData(360, 216, 200, 77.250)]
    [InlineData(360, 216, 300, 97.578)]
    [InlineData(216, 360, 100, 47.538)]
    [InlineData(288, 288, 100, 64.154)]
    [InlineData(468, 432, 100, 97.385)]
    public void The_largest_bubble_is_the_frame_less_ten_points_taken_at_the_scale(
        double width, double height, int scale, double diameter)
    {
        var chart = Bubble(scale);
        var plan = Arrange(chart, width, height);

        var drawn = Circles(ChartComposer.Draw(chart, plan, width, height, new DocumentTheme()));

        _output.WriteLine($"{width}x{height} at {scale}%: {string.Join(", ", drawn.Select(d => d.ToString("0.###")))}");

        Assert.Equal(diameter, drawn.Max(), 2);
    }

    /// <summary>
    /// A bubble's number is its area unless the chart says it is its width, so the bubbles of a
    /// chart holding 10, 20, 30 and 40 come out in proportion to their square roots.
    /// </summary>
    /// <remarks>
    /// Measured from the seventh and ninth pages of chart-doughnut-bubble, whose sizes are the
    /// same and whose bubbles are not: 23.77, 33.61, 41.17 and 47.54 across for the one, and
    /// 11.88, 23.77, 35.65 and 47.54 for the other.
    /// </remarks>
    [Theory]
    [InlineData(false, new[] { 23.769, 33.614, 41.170, 47.538 })]
    [InlineData(true, new[] { 11.885, 23.769, 35.654, 47.538 })]
    public void A_bubble_is_sized_by_its_area_unless_the_chart_says_width(
        bool byWidth, double[] diameters)
    {
        var chart = Bubble(100);
        chart.SizeIsWidth = byWidth;

        var plan = Arrange(chart);
        var drawn = Circles(ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme()));

        Assert.Equal(diameters.Length, drawn.Count);

        for (var i = 0; i < diameters.Length; i++) Assert.Equal(diameters[i], drawn[i], 2);
    }

    /// <summary>
    /// A bubble chart reaches a step further than its numbers at each end of both axes, so that
    /// the bubbles drawn at them have somewhere to be.
    /// </summary>
    /// <remarks>
    /// Measured from the tenth page of chart-doughnut-bubble, the one page whose bubble chart says
    /// nothing about either scale: its foot, whose numbers run 1 to 7, comes out −2 to 10 by twos
    /// where a scatter of the same numbers gets 0 to 8, and its side, whose numbers run 10 to 55,
    /// comes out 0 to 70 by tens where a scatter gets 0 to 60. The side keeps its nought, since a
    /// value axis of nothing but positives begins there.
    /// </remarks>
    [Fact]
    public void A_bubble_chart_makes_room_for_its_bubbles()
    {
        var chart = ChartReader.Parse(XDocument.Parse(BubblePart(scale: 100, stated: false)))!;

        var plan = ChartComposer.Arrange(chart, 360, 216,
            (text, size) => text.Length * size * 0.5069, size => (size * 0.952, size * 0.269));

        _output.WriteLine($"up the side {plan.Minimum}..{plan.Maximum} by {plan.MajorUnit}, " +
                          $"along the foot {plan.AcrossMinimum}..{plan.AcrossMaximum} by {plan.AcrossUnit}");

        Assert.Equal(0, plan.Minimum);
        Assert.Equal(70, plan.Maximum);
        Assert.Equal(10, plan.MajorUnit);

        Assert.Equal(-2, plan.AcrossMinimum);
        Assert.Equal(10, plan.AcrossMaximum);
        Assert.Equal(2, plan.AcrossUnit);
    }

    // ---- a radar ----------------------------------------------------------------------------

    /// <summary>
    /// A web is drawn on a square: the plot area squared to its shorter side and centred in what
    /// it was given.
    /// </summary>
    /// <remarks>
    /// Word's export of the first page of chart-radar-stock draws the plot 172.8 square inside a
    /// plot area 216 by 172.8, 21.6 in from each side.
    /// </remarks>
    [Fact]
    public void A_web_is_drawn_on_a_square()
    {
        var plan = Arrange(Radar());

        Assert.Equal(172.8, plan.Width, 3);
        Assert.Equal(172.8, plan.Height, 3);
        Assert.Equal(93.6, plan.Left, 3);
        Assert.Equal(21.6, plan.Top, 3);
        Assert.Equal(86.4, plan.Radius, 3);
    }

    /// <summary>
    /// A web is ruled at every mark of its value axis but the middle, and its corners stand on the
    /// categories' own spokes — the first at the top, and the rest clockwise from there.
    /// </summary>
    /// <remarks>
    /// Measured from the first page of chart-radar-stock, whose axis runs to sixty in twenties:
    /// three pentagons at a third, two thirds and the whole of the radius, the first corner of
    /// each straight above the middle.
    /// </remarks>
    [Fact]
    public void A_web_is_ruled_at_every_mark()
    {
        var chart = Radar();
        var plan = Arrange(chart);

        var webs = ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme())
            .Operations.OfType<PathOperation>()
            .Where(path => path.Fill is null && path.Steps.Count == 6 &&
                           path.Stroke is { Red: 0, Green: 0, Blue: 0 })
            .ToList();

        Assert.Equal(3, webs.Count);

        // The middle of the plot is (180, 108) in the chart's own coordinates.
        foreach (var (web, distance) in webs.Zip(new[] { 28.8, 57.6, 86.4 }))
        {
            Assert.Equal(180, web.Steps[0].Points[0].X, 3);
            Assert.Equal(108 - distance, web.Steps[0].Points[0].Y, 3);

            // The second corner is a fifth of the way round, so 72° clockwise from the top.
            Assert.Equal(180 + distance * Math.Sin(72 * Math.PI / 180), web.Steps[1].Points[0].X, 3);
            Assert.Equal(108 - distance * Math.Cos(72 * Math.PI / 180), web.Steps[1].Points[0].Y, 3);
        }
    }

    /// <summary>
    /// A radar's series is the figure through its points: outlined where the chart draws lines,
    /// and filled where it says it is filled.
    /// </summary>
    [Theory]
    [InlineData("standard", false)]
    [InlineData("marker", false)]
    [InlineData("filled", true)]
    public void A_web_s_series_is_a_figure_through_its_points(string style, bool filled)
    {
        var chart = Radar(style);
        var plan = Arrange(chart);

        var operations = ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme())
            .Operations.OfType<PathOperation>().ToList();

        // Six steps: five corners and the close. The webs behind it are drawn the same way, so the
        // series is the one drawn in the series' own colour.
        var series = operations.Single(path =>
            path.Steps.Count == 6 &&
            path.Steps.All(step => step.Kind != PathStepKind.Curve) &&
            (filled ? path.Fill : path.Stroke) is { Red: 0x44, Green: 0x72, Blue: 0xC4 });

        Assert.Equal(filled, series.Fill is not null);
        Assert.Equal(!filled, series.Stroke is not null);

        // The first category holds 30 of a scale reaching 60, so it stands half way out.
        Assert.Equal(180, series.Steps[0].Points[0].X, 3);
        Assert.Equal(108 - 43.2, series.Steps[0].Points[0].Y, 3);
    }

    /// <summary>
    /// How much room a web left to Word to place keeps round itself, which is what decides how
    /// large it comes out.
    /// </summary>
    /// <remarks>
    /// Measured from the two pages of chart-kinds-probe that leave a web to Word, at ten point and
    /// at twenty: a 216 point frame comes out 173.744 across the web at the one and 142.974 at the
    /// other. See ChartComposer.WebMargin.
    /// </remarks>
    [Theory]
    [InlineData(10, 173.744)]
    [InlineData(20, 142.974)]
    public void A_web_left_to_word_keeps_the_room_word_keeps(double labelSize, double side)
    {
        var chart = Radar(stated: false, labelSize: labelSize);
        var plan = ChartComposer.Arrange(chart, 360, 216,
            (text, size) => text.Length * size * 0.5069, size => (size * 0.952, size * 0.269));

        Assert.Equal(side, plan.Width, 2);
        Assert.Equal(side, plan.Height, 2);
    }

    // ---- a stock chart ----------------------------------------------------------------------

    /// <summary>
    /// A day of a stock chart is drawn as the line from its lowest number to its highest, standing
    /// at the middle of the category as a line chart's point does.
    /// </summary>
    /// <remarks>
    /// Measured from the sixth page of chart-radar-stock, whose plot runs 144 to 396 across and
    /// 93.6 to 244.8 down at a scale of nought to sixty: its four lines stand at 175.5, 238.5,
    /// 301.5 and 364.5, and the first runs from 40 down to 20 — 144 to 194.4 on the page.
    /// </remarks>
    [Fact]
    public void A_day_is_the_line_from_its_lowest_to_its_highest()
    {
        var chart = Stock();
        var plan = Arrange(chart, 360, 216);

        var lines = ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme())
            .Operations.OfType<PathOperation>()
            .Where(path => path.Steps.Count == 2 &&
                           Math.Abs(path.Steps[0].Points[0].X - path.Steps[1].Points[0].X) < 0.001 &&
                           path.Steps[0].Points[0].X > 100 &&
                           // Inside the plot, which the marks along the foot are not.
                           path.Steps[1].Points[0].Y < 172.8)
            .ToList();

        Assert.Equal(4, lines.Count);

        // In the chart's own coordinates the plot runs 72 to 324 across and 21.6 to 172.8 down.
        Assert.Equal(103.5, lines[0].Steps[0].Points[0].X, 3);
        Assert.Equal(72, lines[0].Steps[0].Points[0].Y, 3);
        Assert.Equal(122.4, lines[0].Steps[1].Points[0].Y, 3);

        Assert.Equal(166.5, lines[1].Steps[0].Points[0].X, 3);
        Assert.Equal(292.5, lines[3].Steps[0].Points[0].X, 3);
    }

    /// <summary>
    /// A stock chart holding an opening as well as a closing draws a bar between the two, as wide
    /// as one bar of a bar chart holding a single series.
    /// </summary>
    /// <remarks>
    /// Measured from the seventh and ninth pages of chart-radar-stock, whose categories are 63
    /// points wide: at the gap of 150 a chart means by saying nothing the bars come out 25.2
    /// across, and at a gap of 50 they come out 42. A day that closed higher than it opened is
    /// drawn white and one that closed lower black, both outlined, which is what Word draws where
    /// the chart says nothing about either.
    /// </remarks>
    [Theory]
    [InlineData(150, 25.2)]
    [InlineData(50, 42)]
    public void A_bar_runs_from_the_opening_to_the_closing(int gap, double width)
    {
        var chart = Stock(series: 4, gapWidth: gap);
        var plan = Arrange(chart, 360, 216);

        var bars = ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme())
            .Operations.OfType<PathOperation>()
            .Where(path => path.Steps.Count == 5 && path.Stroke is { Red: 0, Green: 0, Blue: 0 })
            .ToList();

        Assert.Equal(4, bars.Count);

        var first = Box(bars[0]);

        Assert.Equal(width, first.Width, 3);
        Assert.Equal(103.5 - width / 2, first.Left, 3);

        // The first day opened at 28 and closed at 35, so its bar runs between them and is white.
        Assert.Equal(172.8 - 151.2 * 35 / 60, first.Top, 3);
        Assert.Equal(151.2 * 7 / 60, first.Height, 3);

        Assert.Equal(new DrawingColor(255, 255, 255), bars[0].Fill);
        Assert.Equal(new DrawingColor(0, 0, 0), bars[1].Fill);
    }

    /// <summary>Nothing is drawn along a stock chart's own series, only between them.</summary>
    [Fact]
    public void Nothing_is_drawn_along_a_stock_chart_s_series()
    {
        var chart = Stock();
        var plan = Arrange(chart, 360, 216);

        var curves = ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme())
            .Operations.OfType<PathOperation>()
            // The chart's own frame is drawn with curved corners, and is the one path here that
            // is allowed to hold any.
            .Count(path => path.Steps.Any(step => step.Kind == PathStepKind.Curve) &&
                           path.Fill != new DrawingColor(255, 255, 255));

        Assert.Equal(0, curves);
    }

    // ---- what all four share -----------------------------------------------------------------

    /// <summary>
    /// An axis left to itself reaches a twentieth past its highest value before it is rounded up
    /// to a step.
    /// </summary>
    /// <remarks>
    /// Measured from the sixth and seventh pages of chart-legend-key-probe, whose bars and whose
    /// area both reach 58 and whose axes both stop at 70 rather than the 60 the value alone would
    /// ask for.
    /// </remarks>
    [Theory]
    [InlineData(55, 60)]
    [InlineData(58, 70)]
    public void An_axis_left_to_itself_reaches_a_twentieth_past_its_numbers(
        double highest, double maximum)
    {
        var chart = ChartReader.Parse(XDocument.Parse($"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart><c:plotArea><c:layout/>
                <c:barChart><c:barDir val="col"/>
                  <c:ser><c:idx val="0"/>
                    <c:cat><c:strRef><c:strCache><c:ptCount val="2"/>
                      <c:pt idx="0"><c:v>One</c:v></c:pt><c:pt idx="1"><c:v>Two</c:v></c:pt>
                    </c:strCache></c:strRef></c:cat>
                    <c:val><c:numRef><c:numCache><c:ptCount val="2"/>
                      <c:pt idx="0"><c:v>20</c:v></c:pt>
                      <c:pt idx="1"><c:v>{highest.ToString(System.Globalization.CultureInfo.InvariantCulture)}</c:v></c:pt>
                    </c:numCache></c:numRef></c:val>
                  </c:ser>
                </c:barChart>
                <c:valAx><c:axId val="2"/><c:axPos val="l"/></c:valAx>
                <c:catAx><c:axId val="1"/><c:axPos val="b"/></c:catAx>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """))!;

        var plan = ChartComposer.Arrange(chart, 360, 216,
            (text, size) => text.Length * size * 0.5069, size => (size * 0.952, size * 0.269));

        Assert.Equal(maximum, plan.Maximum);
    }

    /// <summary>
    /// A legend draws a line beside a series that is a line, and a swatch beside one that is a
    /// shape — and nothing at all beside one that is neither, which is what a stock chart's
    /// series are.
    /// </summary>
    /// <remarks>
    /// Measured from chart-legend-key-probe: the key is 19.2pt long and the words begin 21.225pt
    /// past where it starts, at ten point and at twenty alike.
    /// </remarks>
    [Fact]
    public void A_legend_draws_a_line_for_a_series_that_is_a_line()
    {
        var chart = Radar();
        chart.Legend = new ChartLegend("b", false, 10);

        var placed = ChartComposer.Legend(chart, 360, 216,
            (text, size) => text.Length * size * 0.5069, size => (size * 0.952, size * 0.269));

        var entry = Assert.Single(placed);

        Assert.True(entry.Line);
        Assert.Equal(19.2, entry.Swatch, 3);
        Assert.Equal(21.225, entry.TextX - entry.SwatchX, 3);
    }

    /// <summary>
    /// The four kinds drawn against Word's own drawing of them, page by page.
    /// </summary>
    /// <remarks>
    /// Compared as ink, since all four are curves or lines rather than rectangles: a doughnut is
    /// arcs, a bubble is circles, a web is a many-sided figure and a stock chart is the lines
    /// between its series. None can be set against Word operator for operator, and all can be set
    /// against it pixel for pixel.
    /// </remarks>
    [Theory]
    [InlineData("chart-doughnut-bubble", 0, "a doughnut")]
    [InlineData("chart-doughnut-bubble", 1, "a doughnut with a quarter hole")]
    [InlineData("chart-doughnut-bubble", 3, "two rings")]
    [InlineData("chart-doughnut-bubble", 4, "a doughnut begun a quarter turn round")]
    [InlineData("chart-doughnut-bubble", 6, "bubbles")]
    [InlineData("chart-doughnut-bubble", 8, "bubbles sized by width")]
    [InlineData("chart-doughnut-bubble", 10, "bubbles at twice the scale")]
    [InlineData("chart-radar-stock", 0, "a web")]
    [InlineData("chart-radar-stock", 1, "a web with markers")]
    [InlineData("chart-radar-stock", 2, "a filled web")]
    [InlineData("chart-radar-stock", 5, "high, low and close")]
    [InlineData("chart-radar-stock", 6, "open, high, low and close")]
    [InlineData("chart-radar-stock", 8, "open to close at half the gap")]
    public void The_four_kinds_cover_what_word_covers(string fixtureName, int page, string what)
    {
        if (TestFonts.SkipForMissingFonts(fixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(fixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var theirs = File.ReadAllBytes(reference);

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (agreed, covered, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        // Inside the frame rather than over it: Word clips a chart to its own frame and nothing
        // here does, which leaves a quarter of a point of halo round the outside of every chart.
        for (var y = 74.0; y < 286; y++)
        for (var x = 74.0; x < 430; x++)
        {
            var a = mine.At(x, y, scale);
            var b = word.At(x, y, scale);

            var ink = a.R < 200 || a.G < 200 || a.B < 200;
            var theirInk = b.R < 200 || b.G < 200 || b.B < 200;

            if (ink) inkOfMine++;
            if (theirInk) inkOfTheirs++;
            if (ink == theirInk) agreed++;

            covered++;
        }

        var agreement = 100.0 * agreed / covered;

        _output.WriteLine(
            $"{what}: ink {inkOfMine} here, {inkOfTheirs} in Word's; agreeing on {agreement:0.00}%");

        Assert.True(agreement > 98.5, $"{what} agrees with Word on only {agreement:0.0}% of its ink");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    // ---- the parts these are drawn from -------------------------------------------------------

    private static ChartComposer.Plan Arrange(
        ChartDefinition chart, double width = 360, double height = 216) =>
        ChartComposer.Arrange(chart, width, height,
            (text, size) => text.Length * size * 0.5069, size => (size * 0.952, size * 0.269));

    /// <summary>How wide every circle a drawing holds is, in the order they are drawn.</summary>
    private static List<double> Circles(VectorDrawing drawing) =>
    [
        .. drawing.Operations.OfType<PathOperation>()
            .Where(path => path.Steps.Count(step => step.Kind == PathStepKind.Curve) == 4 &&
                           path.Steps.Count <= 6)
            .Select(path => Box(path).Width)
    ];

    private static (double Left, double Top, double Width, double Height) Box(PathOperation path)
    {
        var points = path.Steps.SelectMany(step => step.Points).ToList();

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);

        return (left, top, points.Max(point => point.X) - left,
            points.Max(point => point.Y) - top);
    }

    private static ChartDefinition Doughnut(int hole, int series = 1, bool labels = false) =>
        ChartReader.Parse(XDocument.Parse($"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart><c:plotArea>
                <c:layout><c:manualLayout>
                  <c:layoutTarget val="inner"/>
                  <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.8"/>
                </c:manualLayout></c:layout>
                <c:doughnutChart>
                  {Ring(0, [30, 45, 20, 55], labels)}
                  {(series > 1 ? Ring(1, [10, 25, 50, 15], labels) : string.Empty)}
                  <c:firstSliceAng val="0"/>
                  <c:holeSize val="{hole}"/>
                </c:doughnutChart>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """))!;

    private static string Ring(int index, double[] values, bool labels) => $"""
        <c:ser>
          <c:idx val="{index}"/><c:order val="{index}"/>
          <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
          {(labels
              ? """<c:dLbls><c:showPercent val="1"/></c:dLbls>"""
              : string.Empty)}
          <c:cat><c:strRef><c:strCache><c:ptCount val="4"/>
            <c:pt idx="0"><c:v>One</c:v></c:pt><c:pt idx="1"><c:v>Two</c:v></c:pt>
            <c:pt idx="2"><c:v>Three</c:v></c:pt><c:pt idx="3"><c:v>Four</c:v></c:pt>
          </c:strCache></c:strRef></c:cat>
          <c:val><c:numRef><c:numCache><c:ptCount val="4"/>
            {string.Concat(values.Select((value, i) =>
                $"<c:pt idx=\"{i}\"><c:v>{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</c:v></c:pt>"))}
          </c:numCache></c:numRef></c:val>
        </c:ser>
        """;

    private static ChartDefinition Bubble(int scale) =>
        ChartReader.Parse(XDocument.Parse(BubblePart(scale)))!;

    private static string BubblePart(int scale, bool stated = true) => $"""
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart><c:plotArea>
            <c:layout><c:manualLayout>
              <c:layoutTarget val="inner"/>
              <c:x val="0.25"/><c:y val="0.1"/><c:w val="0.65"/><c:h val="0.7"/>
            </c:manualLayout></c:layout>
            <c:bubbleChart>
              <c:ser>
                <c:idx val="0"/><c:order val="0"/>
                <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
                <c:xVal><c:numRef><c:numCache><c:ptCount val="4"/>
                  <c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>2</c:v></c:pt>
                  <c:pt idx="2"><c:v>4</c:v></c:pt><c:pt idx="3"><c:v>7</c:v></c:pt>
                </c:numCache></c:numRef></c:xVal>
                <c:yVal><c:numRef><c:numCache><c:ptCount val="4"/>
                  <c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>45</c:v></c:pt>
                  <c:pt idx="2"><c:v>20</c:v></c:pt><c:pt idx="3"><c:v>55</c:v></c:pt>
                </c:numCache></c:numRef></c:yVal>
                <c:bubbleSize><c:numRef><c:numCache><c:ptCount val="4"/>
                  <c:pt idx="0"><c:v>10</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt>
                  <c:pt idx="2"><c:v>30</c:v></c:pt><c:pt idx="3"><c:v>40</c:v></c:pt>
                </c:numCache></c:numRef></c:bubbleSize>
              </c:ser>
              <c:bubbleScale val="{scale}"/>
              <c:sizeRepresents val="area"/>
              <c:axId val="1"/><c:axId val="2"/>
            </c:bubbleChart>
            <c:valAx><c:axId val="1"/><c:axPos val="b"/>
              {(stated
                  ? """<c:scaling><c:max val="8"/><c:min val="0"/></c:scaling><c:majorUnit val="2"/>"""
                  : string.Empty)}
            </c:valAx>
            <c:valAx><c:axId val="2"/><c:axPos val="l"/><c:majorGridlines/>
              {(stated
                  ? """<c:scaling><c:max val="60"/><c:min val="0"/></c:scaling><c:majorUnit val="20"/>"""
                  : string.Empty)}
            </c:valAx>
          </c:plotArea></c:chart>
        </c:chartSpace>
        """;

    private static ChartDefinition Radar(
        string style = "standard", bool stated = true, double labelSize = 10) =>
        ChartReader.Parse(XDocument.Parse($"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart><c:plotArea>
                <c:layout>{(stated
                    ? """
                      <c:manualLayout>
                        <c:layoutTarget val="inner"/>
                        <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.8"/>
                      </c:manualLayout>
                      """
                    : string.Empty)}</c:layout>
                <c:radarChart>
                  <c:radarStyle val="{style}"/>
                  <c:ser>
                    <c:idx val="0"/><c:order val="0"/>
                    <c:tx><c:strRef><c:strCache><c:ptCount val="1"/>
                      <c:pt idx="0"><c:v>Units</c:v></c:pt></c:strCache></c:strRef></c:tx>
                    <c:spPr>
                      {(style == "filled"
                          ? """<a:solidFill><a:srgbClr val="4472C4"/></a:solidFill>"""
                          : """<a:ln w="28575"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln>""")}
                    </c:spPr>
                    <c:marker><c:symbol val="{(style == "marker" ? "circle" : "none")}"/></c:marker>
                    <c:cat><c:strRef><c:strCache><c:ptCount val="5"/>
                      <c:pt idx="0"><c:v>One</c:v></c:pt><c:pt idx="1"><c:v>Two</c:v></c:pt>
                      <c:pt idx="2"><c:v>Three</c:v></c:pt><c:pt idx="3"><c:v>Four</c:v></c:pt>
                      <c:pt idx="4"><c:v>Five</c:v></c:pt>
                    </c:strCache></c:strRef></c:cat>
                    <c:val><c:numRef><c:numCache><c:ptCount val="5"/>
                      <c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>45</c:v></c:pt>
                      <c:pt idx="2"><c:v>20</c:v></c:pt><c:pt idx="3"><c:v>55</c:v></c:pt>
                      <c:pt idx="4"><c:v>35</c:v></c:pt>
                    </c:numCache></c:numRef></c:val>
                  </c:ser>
                  <c:axId val="1"/><c:axId val="2"/>
                </c:radarChart>
                <c:catAx><c:axId val="1"/><c:axPos val="b"/>
                  <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr>
                    <a:defRPr sz="{(int)(labelSize * 100)}"/></a:pPr></a:p></c:txPr>
                </c:catAx>
                <c:valAx><c:axId val="2"/><c:axPos val="l"/><c:majorGridlines/>
                  <c:scaling><c:max val="60"/><c:min val="0"/></c:scaling>
                  <c:majorUnit val="20"/>
                  <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr>
                    <a:defRPr sz="{(int)(labelSize * 100)}"/></a:pPr></a:p></c:txPr>
                </c:valAx>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """))!;

    private static ChartDefinition Stock(int series = 3, int gapWidth = 150) =>
        ChartReader.Parse(XDocument.Parse($"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart><c:plotArea>
                <c:layout><c:manualLayout>
                  <c:layoutTarget val="inner"/>
                  <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.7"/><c:h val="0.7"/>
                </c:manualLayout></c:layout>
                <c:stockChart>
                  {(series > 3 ? Day(0, "Open", [28, 40, 22, 50]) : string.Empty)}
                  {Day(series > 3 ? 1 : 0, "High", [40, 52, 33, 58])}
                  {Day(series > 3 ? 2 : 1, "Low", [20, 30, 15, 35])}
                  {Day(series > 3 ? 3 : 2, "Close", [35, 33, 28, 45])}
                  <c:hiLowLines/>
                  {(series > 3 ? $"<c:upDownBars><c:gapWidth val=\"{gapWidth}\"/></c:upDownBars>" : string.Empty)}
                  <c:axId val="1"/><c:axId val="2"/>
                </c:stockChart>
                <c:catAx><c:axId val="1"/><c:axPos val="b"/></c:catAx>
                <c:valAx><c:axId val="2"/><c:axPos val="l"/><c:majorGridlines/>
                  <c:scaling><c:max val="60"/><c:min val="0"/></c:scaling>
                  <c:majorUnit val="20"/>
                </c:valAx>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """))!;

    private static string Day(int index, string name, double[] values) => $"""
        <c:ser>
          <c:idx val="{index}"/><c:order val="{index}"/>
          <c:tx><c:strRef><c:strCache><c:ptCount val="1"/>
            <c:pt idx="0"><c:v>{name}</c:v></c:pt></c:strCache></c:strRef></c:tx>
          <c:spPr><a:ln w="28575"><a:noFill/></a:ln></c:spPr>
          <c:marker><c:symbol val="none"/></c:marker>
          <c:cat><c:strRef><c:strCache><c:ptCount val="4"/>
            <c:pt idx="0"><c:v>One</c:v></c:pt><c:pt idx="1"><c:v>Two</c:v></c:pt>
            <c:pt idx="2"><c:v>Three</c:v></c:pt><c:pt idx="3"><c:v>Four</c:v></c:pt>
          </c:strCache></c:strRef></c:cat>
          <c:val><c:numRef><c:numCache><c:ptCount val="4"/>
            {string.Concat(values.Select((value, i) =>
                $"<c:pt idx=\"{i}\"><c:v>{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</c:v></c:pt>"))}
          </c:numCache></c:numRef></c:val>
        </c:ser>
        """;
}
