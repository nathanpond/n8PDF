using System.Xml.Linq;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Charts, which are the one thing a document describes only as data.
/// </summary>
/// <remarks>
/// There is no drawing of a chart anywhere in a document — not even the cache a diagram carries.
/// What the part holds is the numbers, the axes and the formatting, and every reader works out the
/// picture for itself. So everything here had to be measured: where a bar of a given value lands,
/// how wide it is, where the gridlines fall, and where each label sits against the axis it names.
///
/// Most of the fixtures place their plot area by hand and state what their axes run between, which
/// pins everything that can be measured against; the probes that do neither are what Word's own
/// placing and scaling were measured from. See the README for what a chart still leaves out.
/// </remarks>
public class ChartTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Where a chart puts its plotting, and where in it a bar of a given value lands.
    /// </summary>
    /// <remarks>
    /// The fixture's plot area is a fifth of the way across and a tenth down, seven tenths of the
    /// frame each way, and its value axis runs from nought to sixty. So a bar of 45 reaches three
    /// quarters of the way up a 151.2pt plot, and Word's export says 113.4pt exactly.
    /// </remarks>
    [Fact]
    public void A_bar_reaches_where_word_draws_it()
    {
        var (ours, theirs) = BothWays("chart-column");

        // The filled rectangles only: the plot area and the four bars. The gridlines, the axes
        // and the marks are strokes, and Word writes all of one axis's worth as a single path
        // where this writes one apiece — the same ink, counted differently, so they are compared
        // as ink instead.
        var mine = Fills(ours, page: 0);
        var word = Fills(theirs, page: 0);

        Assert.Equal(5, word.Count);
        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine($"{mine[i]} against Word's {word[i]}");

            Assert.Equal(word[i].ColorHex, mine[i].ColorHex);

            Assert.True(Math.Abs(mine[i].Left - word[i].Left) < 0.25 &&
                        Math.Abs(mine[i].Top - word[i].Top) < 0.25 &&
                        Math.Abs(mine[i].Width - word[i].Width) < 0.25 &&
                        Math.Abs(mine[i].Height - word[i].Height) < 0.25,
                $"something is drawn at {mine[i]} where Word draws it at {word[i]}.");
        }
    }

    /// <summary>
    /// How wide a bar is falls out of the gap between them, which is stated as a percentage of the
    /// bar itself.
    /// </summary>
    /// <remarks>
    /// One series at a gap of 150 makes each category two and a half bars wide, so the bar is two
    /// fifths of it: four categories across 252pt give 63pt each and a bar of 25.2pt, which is what
    /// Word draws. Two series over a gap of 100 and an overlap of −27 share their category between
    /// them and then stand a little apart, which comes to 117 ÷ 3.27 — and Word's fifth probe page
    /// draws that bar 35.76pt wide against the 35.78 this gives.
    /// </remarks>
    [Theory]
    [InlineData(4, 252, 1, 150, 0, 25.2)]
    [InlineData(4, 252, 1, 0, 0, 63)]
    [InlineData(4, 252, 1, 100, 0, 31.5)]
    [InlineData(4, 252, 2, 150, 0, 18)]
    [InlineData(2, 234, 2, 100, -27, 35.78)]
    public void A_bar_is_as_wide_as_the_gap_leaves_it(
        int categories, double width, int series, int gapWidth, int overlap, double expected)
    {
        var chart = new ChartDefinition { GapWidth = gapWidth, Overlap = overlap };
        var names = Enumerable.Range(0, categories).Select(i => $"C{i}").ToList();

        for (var i = 0; i < series; i++)
        {
            chart.Series.Add(new ChartSeries($"S{i}", names,
                [.. names.Select(_ => (double?)1)], null));
        }

        var plan = new ChartComposer.Plan(0, 0, width, 100, 0, 10, 5);
        var bars = ChartComposer.Bars(chart, plan).ToList();

        Assert.Equal(categories * series, bars.Count);
        Assert.Equal(expected, bars[0].Width, 2);
    }

    /// <summary>And where two series stand beside each other, which is the overlap's doing.</summary>
    [Fact]
    public void Two_series_stand_where_word_stands_them()
    {
        var (ours, theirs) = BothWays("chart-axis-probe");

        // The fifth page holds them: the plot area, and four bars in two colours.
        var mine = Fills(ours, page: 4);
        var word = Fills(theirs, page: 4);

        Assert.Equal(5, word.Count);
        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine($"{mine[i]} against Word's {word[i]}");

            Assert.Equal(word[i].ColorHex, mine[i].ColorHex);
            Assert.True(Math.Abs(mine[i].Left - word[i].Left) < 0.25 &&
                        Math.Abs(mine[i].Width - word[i].Width) < 0.25,
                $"a bar is at {mine[i]} where Word's is at {word[i]}.");
        }
    }

    /// <summary>The whole chart as ink, which is what says the parts nothing else measures agree.</summary>
    [Theory]
    [InlineData("chart-column", 0)]
    [InlineData("chart-axis-probe", 0)]
    [InlineData("chart-axis-probe", 2)]
    public void A_chart_covers_what_word_covers(string name, int page)
    {
        var (ours, theirs) = BothWays(name);

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (agreed, covered, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        for (var y = 60.0; y < 320; y++)
        for (var x = 66.0; x < 440; x++)
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
            $"{name} page {page + 1}: ink {inkOfMine} here, {inkOfTheirs} in Word's; " +
            $"the two agree on {agreement:0.00}%");

        Assert.True(agreement > 98, $"the two pages agree on only {agreement:0.0}% of the chart");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    /// <summary>
    /// Where a chart puts its plotting when it does not say, which is what every chart in a real
    /// document leaves to be worked out.
    /// </summary>
    /// <remarks>
    /// Six charts, varying one thing each: how wide the numbers up the axis are, how large they
    /// are set, how big the frame is, how long the words under the bars are, and whether there are
    /// any labels at all. What comes out of them is that a chart carrying no labels sits eleven
    /// points inside its frame on every side, and one carrying them begins its labels 6.5pt inside
    /// the frame and gives the plotting whatever is left.
    /// </remarks>
    [Theory]
    [InlineData(0, "the plain case")]
    [InlineData(1, "numbers a hundred thousand times larger")]
    [InlineData(2, "the same chart at twenty point")]
    [InlineData(3, "a frame half the size")]
    [InlineData(4, "a long word under the bars")]
    [InlineData(5, "no labels at all")]
    public void A_chart_that_says_nothing_is_laid_out_the_way_word_lays_it_out(int page, string what)
    {
        var (ours, theirs) = BothWays("chart-layout-probe");

        var mine = PlotArea(ours, page);
        var word = PlotArea(theirs, page);

        _output.WriteLine($"page {page + 1} ({what}): {mine} against Word's {word}");

        Assert.True(Math.Abs(mine.Left - word.Left) < 0.3 &&
                    Math.Abs(mine.Top - word.Top) < 0.3 &&
                    Math.Abs(mine.Width - word.Width) < 0.3 &&
                    Math.Abs(mine.Height - word.Height) < 0.3,
            $"page {page + 1} ({what}): the plotting is at {mine} where Word puts it at {word}.");
    }

    /// <summary>
    /// The plot area of a page: the white rectangle inside the frame, which is the second largest
    /// thing a chart fills.
    /// </summary>
    private static ExtractedRectangle PlotArea(byte[] pdf, int page)
    {
        var rectangle = PdfPathExtractor.Extract(pdf)
            .Where(r => r.PageIndex == page && r.ColorHex == "FFFFFF" && r.Width > 20 && r.Height > 20)
            .OrderByDescending(r => r.Width * r.Height)
            .FirstOrDefault();

        Assert.NotNull(rectangle);
        return rectangle;
    }

    /// <summary>
    /// What a value axis runs between where the chart does not say, which is the last thing about
    /// a chart Word decides for itself.
    /// </summary>
    /// <remarks>
    /// Twelve charts differing only in the numbers they hold. One rule accounts for every one: the
    /// step is the smallest of one, two or five times a power of ten for which the axis carries no
    /// more marks than it has room to write, and the top is the smallest multiple of that step
    /// lying strictly above the largest value. The strictness is what puts a chart of exactly 100
    /// at 120 rather than leaving its tallest bar against the frame; the room is what keeps a
    /// chart of 9.5 at ten steps and a chart of 10 at six.
    /// </remarks>
    [Theory]
    [InlineData(0, "0 0.2 0.4 0.6 0.8 1 1.2")]
    [InlineData(1, "0 1 2 3 4 5 6 7 8")]
    [InlineData(2, "0 1 2 3 4 5 6 7 8 9 10")]
    [InlineData(3, "0 2 4 6 8 10 12")]
    [InlineData(4, "0 2 4 6 8 10 12 14")]
    [InlineData(5, "0 5 10 15 20 25 30 35 40 45 50")]
    [InlineData(6, "0 20 40 60 80 100 120")]
    [InlineData(7, "0 20 40 60 80 100 120")]
    [InlineData(8, "0 200 400 600 800 1000 1200")]
    [InlineData(9, "0 0.05 0.1 0.15 0.2 0.25 0.3 0.35 0.4 0.45")]
    [InlineData(10, "-30 -20 -10 0 10 20 30 40 50 60 70")]
    [InlineData(11, "0 10 20 30 40 50 60")]
    public void An_axis_left_to_itself_is_scaled_the_way_word_scales_it(int page, string expected)
    {
        var (ours, theirs) = BothWays("chart-scale-probe");

        var mine = AxisLabels(ours, page);
        _output.WriteLine($"page {page + 1}: {string.Join(" ", mine)}");

        Assert.Equal(expected.Split(' '), mine);

        // And the same numbers come out of Word's own drawing of it, run for run: its labels are
        // written in pieces, so they are compared as the text of the page rather than as lines.
        var word = AxisLabels(theirs, page);
        Assert.Equal(expected.Replace(" ", ""), string.Concat(word));
    }

    /// <summary>
    /// The numbers up the value axis, read off the page from the top of the axis down.
    /// </summary>
    /// <remarks>
    /// Everything that is not a category, which the probe names so they can be told apart. Word
    /// writes a label in as many runs as it likes — "-30" comes out as "-3" and "0" — so ours are
    /// read as lines and Word's as runs, and what is compared is the sequence either way.
    /// </remarks>
    private static List<string> AxisLabels(byte[] pdf, int page, bool lying = false)
    {
        var runs = PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && !run.Text.Contains('C') &&
                          !string.IsNullOrWhiteSpace(run.Text) &&
                          !"One Two Three".Contains(run.Text.Trim()))
            .ToList();

        // Up the side they read from the top down and are gathered by their baseline; along the
        // foot they read from the left and share one, so they are gathered by where they begin.
        if (lying)
        {
            return
            [
                .. runs
                    .GroupBy(run => Math.Round(run.BaselineY, 1))
                    .OrderByDescending(group => group.Count())
                    .First()
                    .OrderBy(run => run.X)
                    .Select(run => run.Text.Trim())
            ];
        }

        return
        [
            .. runs
                .GroupBy(run => Math.Round(run.BaselineY, 1))
                .OrderBy(group => group.Key)
                .Select(group =>
                    string.Concat(group.OrderBy(run => run.X).Select(run => run.Text.Trim())))
                .Reverse()
        ];
    }

    /// <summary>
    /// The words under the bars go beside the nought rather than at the foot of the plot, which is
    /// only visible once something is negative.
    /// </summary>
    [Fact]
    public void The_categories_sit_where_the_axes_cross()
    {
        var (ours, theirs) = BothWays("chart-scale-probe");

        // The eleventh chart runs from −30 to 70, so its nought is three quarters of the way down.
        var mine = Categories(ours);
        var word = Categories(theirs);

        _output.WriteLine($"ours at {mine:0.##}, Word at {word:0.##}");

        Assert.True(Math.Abs(mine - word) < 0.3,
            $"the categories sit at {mine:0.##} where Word puts them at {word:0.##}.");

        static double Categories(byte[] pdf) =>
            PdfTextExtractor.Extract(pdf)
                .Where(run => run.PageIndex == 10 && run.Text.Contains("C1"))
                .Select(run => run.BaselineY)
                .First();
    }

    /// <summary>
    /// A line through the categories, and a pie divided between them, against Word's own drawing
    /// of both.
    /// </summary>
    /// <remarks>
    /// Compared as ink, since both are curves: a line chart curves through its points unless told
    /// not to, and a pie is nothing but arcs. Neither can be set against Word operator for
    /// operator, and both can be set against it pixel for pixel.
    /// </remarks>
    [Theory]
    [InlineData(0, "one line")]
    [InlineData(1, "two lines")]
    [InlineData(2, "a pie, placed by hand")]
    [InlineData(3, "a pie, placed by Word")]
    public void A_line_and_a_pie_cover_what_word_covers(int page, string what)
    {
        var (ours, theirs) = BothWays("chart-line-pie");

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (agreed, covered, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        // Inside the frame rather than over it. Word clips a chart to its own frame, so the outer
        // half of the border it draws is cut away and the inner half is all that shows; nothing
        // here clips, so the same border straddles the edge. It is a quarter of a point of halo
        // round the outside of a chart, and it is all that is left between the two.
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

        Assert.True(agreement > 99, $"{what} agrees with Word on only {agreement:0.0}% of its ink");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    /// <summary>
    /// Where a pie sits: the middle of the plot area, reaching the nearer pair of its edges.
    /// </summary>
    /// <remarks>
    /// Word's export of the fixture puts the hand-placed pie's centre at (252, 180) with a radius
    /// of 86.4 — the middle of a plot 216 wide and 172.8 tall, and half its shorter side — and the
    /// automatic one at the middle of the frame with a radius of 97, the frame less the eleven
    /// points a chart keeps clear on every side.
    /// </remarks>
    [Theory]
    [InlineData(2, 252, 180, 86.4)]
    [InlineData(3, 252, 180, 97)]
    public void A_pie_fills_the_plot_it_is_given(int page, double x, double y, double radius)
    {
        var (ours, _) = BothWays("chart-line-pie");

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        double left = 999, right = -999, top = 999, bottom = -999;

        // Inside the frame, so that the border round the chart is not mistaken for the pie.
        for (var py = 76.0; py < 284; py += 0.5)
        for (var px = 76.0; px < 428; px += 0.5)
        {
            var pixel = mine.At(px, py, scale);

            if (pixel.R > 200 && pixel.G > 200 && pixel.B > 200) continue;

            left = Math.Min(left, px);
            right = Math.Max(right, px);
            top = Math.Min(top, py);
            bottom = Math.Max(bottom, py);
        }

        _output.WriteLine($"page {page + 1}: the pie spans {left}..{right} across and {top}..{bottom} down");

        Assert.Equal(x, (left + right) / 2, 1);
        Assert.Equal(y, (top + bottom) / 2, 1);

        // The slices are outlined in white, so the outermost three quarters of a point of the pie
        // is the border rather than the pie and does not count as ink: what is measured here comes
        // out just inside the radius rather than at it.
        Assert.True(Math.Abs((right - left) / 2 - radius) < 1.1,
            $"the pie is {(right - left) / 2:0.##} across the middle where it should be {radius}.");

        Assert.True(Math.Abs((bottom - top) / 2 - radius) < 1.1,
            $"the pie is {(bottom - top) / 2:0.##} down the middle where it should be {radius}.");
    }

    /// <summary>
    /// A line curves through its points unless the series says otherwise, and the curve is the one
    /// Word draws.
    /// </summary>
    /// <remarks>
    /// Each point is passed at a slope of half what its neighbours span, with the control points a
    /// third of the way along it; the ends take the slope of their own segment. Word's export of
    /// the fixture's line gives control points that come out of exactly that, to the EMU — the
    /// second control of its first segment is 266700 where this gives 266690.
    /// </remarks>
    [Fact]
    public void A_line_curves_through_its_points_the_way_word_curves_it()
    {
        var chart = new ChartDefinition { Kind = ChartKind.Line };

        chart.Series.Add(new ChartSeries("Units", ["A", "B", "C", "D"],
            [30, 45, 20, 55], null) { Line = new DrawingColorReference("4472C4", null) });

        var plan = new ChartComposer.Plan(144, 93.6, 252, 151.2, 0, 60, 20);
        var drawing = ChartComposer.Draw(chart, plan, 360, 216, new DocumentTheme());

        var path = Assert.IsType<Images.PathOperation>(
            drawing.Operations.Last(operation => operation is Images.PathOperation { Fill: null }));

        // The points sit at the middles of the four quarters of the plot.
        Assert.Equal(144 + 31.5, path.Steps[0].Points[0].X, 2);

        // And between them a curve rather than a line, one for each gap.
        Assert.Equal(3, path.Steps.Count(step => step.Kind == Images.PathStepKind.Curve));

        // The first segment's second control: the point at 45 is passed at half the slope from 30
        // to 20, so the control sits a third of that back from it.
        var first = path.Steps[1].Points;
        var slope = (plan.PositionOf(20) - plan.PositionOf(30)) / 2;

        Assert.Equal(plan.PositionOf(45) - slope / 3, first[1].Y, 2);
    }

    /// <summary>And a series that says not to is drawn straight.</summary>
    [Fact]
    public void A_line_told_not_to_curve_goes_straight()
    {
        var chart = new ChartDefinition { Kind = ChartKind.Line };

        chart.Series.Add(new ChartSeries("Units", ["A", "B", "C"], [10, 20, 30], null)
        {
            Smooth = false,
            Line = new DrawingColorReference("4472C4", null)
        });

        var plan = new ChartComposer.Plan(0, 0, 300, 100, 0, 40, 10);
        var drawing = ChartComposer.Draw(chart, plan, 300, 100, new DocumentTheme());

        var path = Assert.IsType<Images.PathOperation>(
            drawing.Operations.Last(operation => operation is Images.PathOperation { Fill: null }));

        Assert.Equal(2, path.Steps.Count(step => step.Kind == Images.PathStepKind.Line));
        Assert.DoesNotContain(path.Steps, step => step.Kind == Images.PathStepKind.Curve);
    }

    /// <summary>
    /// A line curves unless told not to, which is the format's default and not the obvious one.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("""<c:smooth val="1"/>""", true)]
    [InlineData("""<c:smooth val="0"/>""", false)]
    public void A_series_curves_unless_it_says_not_to(string smooth, bool expected)
    {
        var chart = ChartReader.Parse(XDocument.Parse($"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart><c:plotArea><c:layout/>
                <c:lineChart>
                  <c:ser>
                    <c:idx val="0"/>
                    <c:spPr><a:ln w="28575"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln></c:spPr>
                    <c:val><c:numRef><c:numCache><c:ptCount val="1"/>
                      <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                    {smooth}
                  </c:ser>
                </c:lineChart>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """));

        var series = Assert.Single(chart!.Series);

        Assert.Equal(expected, series.Smooth);
        Assert.Equal("4472C4", series.Line?.Hex);
        Assert.Equal(2.25, series.LineWidthPoints, 3);
    }

    /// <summary>A pie's slices each carry their own colour, which is what a data point is for.</summary>
    [Fact]
    public void A_pie_takes_a_colour_for_every_slice()
    {
        var chart = ChartReader.Parse(XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart><c:plotArea><c:layout/>
                <c:pieChart>
                  <c:varyColors val="1"/>
                  <c:ser>
                    <c:idx val="0"/>
                    <c:dPt><c:idx val="0"/>
                      <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr></c:dPt>
                    <c:dPt><c:idx val="1"/>
                      <c:spPr><a:solidFill><a:srgbClr val="ED7D31"/></a:solidFill></c:spPr></c:dPt>
                    <c:val><c:numRef><c:numCache><c:ptCount val="2"/>
                      <c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>3</c:v></c:pt>
                    </c:numCache></c:numRef></c:val>
                  </c:ser>
                  <c:firstSliceAng val="90"/>
                </c:pieChart>
              </c:plotArea></c:chart>
            </c:chartSpace>
            """));

        Assert.Equal(ChartKind.Pie, chart!.Kind);
        Assert.Equal(90, chart.FirstSliceAngle);

        var series = Assert.Single(chart.Series);
        Assert.Equal("4472C4", series.PointFills[0]?.Hex);
        Assert.Equal("ED7D31", series.PointFills[1]?.Hex);
    }

    /// <summary>What a chart part says, read back off it.</summary>
    [Fact]
    public void A_chart_is_read_from_its_own_part()
    {
        var chart = ChartReader.Parse(XDocument.Parse(Part()));
        Assert.NotNull(chart);

        Assert.Equal(ChartKind.Column, chart.Kind);
        Assert.Equal(150, chart.GapWidth);
        Assert.Equal(["North", "South", "East", "West"], chart.Categories);

        var series = Assert.Single(chart.Series);
        Assert.Equal("Units", series.Name);
        Assert.Equal([30, 45, 20, 55], series.Values.Select(value => value ?? 0));
        Assert.Equal("4472C4", series.Fill?.Hex);

        Assert.Equal(0, chart.ValueAxis?.Minimum);
        Assert.Equal(60, chart.ValueAxis?.Maximum);
        Assert.Equal(20, chart.ValueAxis?.MajorUnit);
        Assert.True(chart.ValueAxis?.MajorGridlines);

        Assert.NotNull(chart.PlotArea);
        Assert.Equal(0.2, chart.PlotArea.X, 3);
        Assert.Equal(0.7, chart.PlotArea.Width, 3);
    }

    /// <summary>
    /// A series with a hole in it has a hole, rather than a nought: the points are numbered, and
    /// the one that is missing is missing.
    /// </summary>
    [Fact]
    public void A_gap_in_the_numbers_is_not_a_nought()
    {
        var chart = ChartReader.Parse(XDocument.Parse(Part("""
            <c:val><c:numRef><c:numCache>
              <c:ptCount val="4"/>
              <c:pt idx="0"><c:v>10</c:v></c:pt>
              <c:pt idx="2"><c:v>30</c:v></c:pt>
            </c:numCache></c:numRef></c:val>
            """)));

        var values = Assert.Single(chart!.Series).Values;

        Assert.Equal(4, values.Count);
        Assert.Equal(10, values[0]);
        Assert.Null(values[1]);
        Assert.Equal(30, values[2]);
        Assert.Null(values[3]);

        // And nothing is drawn where there is nothing: three categories of four carry a bar.
        var plan = new ChartComposer.Plan(0, 0, 100, 100, 0, 40, 10);
        Assert.Equal(2, ChartComposer.Bars(chart, plan).Count());
    }

    /// <summary>The marks up a value axis, from its bottom to its top.</summary>
    [Fact]
    public void An_axis_is_marked_at_every_unit()
    {
        var plan = new ChartComposer.Plan(0, 0, 100, 150, 0, 60, 20);

        Assert.Equal<double>([0, 20, 40, 60], [.. ChartComposer.Marks(plan)]);

        // And where each one falls: the top of the plot is the top of the scale.
        Assert.Equal(150d, plan.PositionOf(0), 3);
        Assert.Equal(0d, plan.PositionOf(60), 3);
        Assert.Equal(75d, plan.PositionOf(30), 3);
    }

    /// <summary>
    /// A chart lying on its side, and one whose bars are piled on each other rather than set
    /// beside them: every bar of it against Word's own.
    /// </summary>
    /// <remarks>
    /// The six pages are a bar chart placed by hand and one left to Word, two stacked columns —
    /// one told what its axis runs between and one not — a stacked column filled out to the whole,
    /// and a stacked bar. Between them they cover which end a lying chart starts its categories at,
    /// which way its series run within one, where a stacked bar begins, and what a chart stacked to
    /// the whole makes of its numbers.
    /// </remarks>
    [Theory]
    [InlineData(0, 4, "a bar chart placed by hand")]
    [InlineData(1, 4, "the same, placed by Word")]
    [InlineData(2, 7, "two series stacked")]
    [InlineData(3, 7, "the same, scaled by Word")]
    [InlineData(4, 7, "the same, filled out to the whole")]
    [InlineData(5, 7, "two series stacked, lying down")]
    [InlineData(6, 4, "the marks along a lying axis")]
    [InlineData(7, 7, "one bar hanging the wrong side of nought")]
    public void A_bar_lies_where_word_lays_it(int page, int count, string what)
    {
        var (ours, theirs) = BothWays("chart-bar-stacked");

        var mine = Fills(ours, page);
        var word = Fills(theirs, page);

        _output.WriteLine($"page {page + 1} ({what})");

        Assert.Equal(count, word.Count);
        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine($"    {mine[i]} against Word's {word[i]}");

            Assert.Equal(word[i].ColorHex, mine[i].ColorHex);

            // Word rounds every edge it draws to a three-hundredth of an inch, which is 0.24pt,
            // so a bar can land an eighth of a point either side of where the arithmetic puts it.
            Assert.True(Math.Abs(mine[i].Left - word[i].Left) < 0.25 &&
                        Math.Abs(mine[i].Top - word[i].Top) < 0.25 &&
                        Math.Abs(mine[i].Width - word[i].Width) < 0.25 &&
                        Math.Abs(mine[i].Height - word[i].Height) < 0.25,
                $"page {page + 1}: a bar is at {mine[i]} where Word draws it at {word[i]}.");
        }
    }

    /// <summary>Where a chart lying down puts its plotting when it does not say.</summary>
    /// <remarks>
    /// The words go up the side and the numbers along the foot, so what has to be made room for
    /// swaps over: the left holds the widest category rather than the widest number, and the right
    /// holds half of the last number along the foot, which is centred on the plot's own corner.
    /// </remarks>
    [Fact]
    public void A_chart_lying_down_is_laid_out_the_way_word_lays_it_out()
    {
        var (ours, theirs) = BothWays("chart-bar-stacked");

        var mine = PlotArea(ours, page: 1);
        var word = PlotArea(theirs, page: 1);

        _output.WriteLine($"{mine} against Word's {word}");

        Assert.True(Math.Abs(mine.Left - word.Left) < 0.3 &&
                    Math.Abs(mine.Top - word.Top) < 0.3 &&
                    Math.Abs(mine.Width - word.Width) < 0.3 &&
                    Math.Abs(mine.Height - word.Height) < 0.3,
            $"the plotting is at {mine} where Word puts it at {word}.");
    }

    /// <summary>
    /// What a stacked chart's axis runs between, which is what its categories come to rather than
    /// what any one bar holds — and what one stacked to the whole runs between, which is nothing
    /// but a hundred per cent.
    /// </summary>
    /// <remarks>
    /// The third page holds 30 and 10 against 45 and 15 against 20 and 25, so its tallest pile is
    /// 60 and Word runs the axis to 70 in tens — where an unstacked chart of the same numbers
    /// would have stopped at 50. The fourth is the same numbers taken as shares of their own
    /// category, which every category makes 100% of.
    /// </remarks>
    [Theory]
    [InlineData(3, "0 10 20 30 40 50 60 70")]
    [InlineData(4, "0% 10% 20% 30% 40% 50% 60% 70% 80% 90% 100%")]
    public void A_stacked_axis_is_scaled_by_what_the_categories_come_to(int page, string expected)
    {
        var (ours, theirs) = BothWays("chart-bar-stacked");

        var mine = AxisLabels(ours, page);
        _output.WriteLine($"page {page + 1}: {string.Join(" ", mine)}");

        Assert.Equal(expected.Split(' '), mine);
        Assert.Equal(expected.Replace(" ", ""), string.Concat(AxisLabels(theirs, page)));
    }

    /// <summary>
    /// What an axis that lies down runs between, which is not what the same numbers up the side
    /// would give: a number written along an axis takes about three times its own type size of
    /// room, and one written up it a tenth over.
    /// </summary>
    /// <remarks>
    /// Fourteen charts, varying the numbers, how long the axis is, which way it runs and what size
    /// its labels are set at. The last four are the ones that part the readings: a chart of
    /// millions divides its foot exactly as a chart of tens does, so the room has nothing to do
    /// with how wide the numbers are; and the same chart set in twenty point divides it into a
    /// third as many steps, so the room grows with the type.
    /// </remarks>
    [Theory]
    [InlineData(0, "-50 0 50")]
    [InlineData(1, "-50 0 50")]
    [InlineData(2, "-60 -40 -20 0 20 40")]
    [InlineData(3, "-50 0 50 100")]
    [InlineData(4, "-50 0 50 100")]
    [InlineData(5, "0 20 40 60")]
    [InlineData(6, "0 20 40 60")]
    [InlineData(7, "0 5 10")]
    [InlineData(8, "0 500 1000 1500")]
    [InlineData(9, "0 0.2 0.4 0.6")]
    [InlineData(10, "0 500000 1000000 1500000")]
    [InlineData(11, "0 50")]
    [InlineData(12, "0 5 10")]
    [InlineData(13, "0 20 40 60")]
    public void An_axis_that_lies_down_is_scaled_the_way_word_scales_it(int page, string expected)
    {
        var (ours, theirs) = BothWays("chart-bar-scale-probe");

        var mine = AxisLabels(ours, page, lying: page < 12);
        _output.WriteLine($"page {page + 1}: {string.Join(" ", mine)}");

        Assert.Equal(expected.Split(' '), mine);
        Assert.Equal(expected.Replace(" ", ""), string.Concat(AxisLabels(theirs, page, page < 12)));
    }

    /// <summary>
    /// The whole of a lying chart as ink, which is what says its gridlines, its axes and the marks
    /// along them agree with Word as well as its bars do.
    /// </summary>
    [Theory]
    [InlineData(0, "a bar chart placed by hand")]
    [InlineData(5, "two series stacked, lying down")]
    [InlineData(6, "the marks along a lying axis")]
    [InlineData(7, "and where that axis goes when something is negative")]
    public void A_lying_chart_covers_what_word_covers(int page, string what)
    {
        var (ours, theirs) = BothWays("chart-bar-stacked");

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (agreed, covered, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        // The inside of the frame only: Word clips a chart to its own frame, so the outer half of
        // its border is cut away where ours is not, and a quarter point of halo all the way round
        // would swamp everything else.
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
            $"page {page + 1} ({what}): ink {inkOfMine} here, {inkOfTheirs} in Word's; " +
            $"the two agree on {agreement:0.00}%");

        Assert.True(agreement > 98, $"the two pages agree on only {agreement:0.0}% of the chart");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    /// <summary>
    /// Stacked bars begin where the last one ended, with what rises above nought and what hangs
    /// below it piled apart.
    /// </summary>
    [Fact]
    public void A_stacked_bar_begins_where_the_last_one_ended()
    {
        var chart = new ChartDefinition { Grouping = ChartGrouping.Stacked, Overlap = 100 };
        var names = new[] { "One" };

        chart.Series.Add(new ChartSeries("A", names, [30], null));
        chart.Series.Add(new ChartSeries("B", names, [-20], null));
        chart.Series.Add(new ChartSeries("C", names, [10], null));

        var plan = new ChartComposer.Plan(0, 0, 100, 100, -50, 50, 10);
        var bars = ChartComposer.Bars(chart, plan).ToList();

        // Nought is halfway up a plot of a hundred points running from −50 to 50.
        Assert.Equal(3, bars.Count);
        Assert.Equal(20d, bars[0].Y, 3);
        Assert.Equal(30d, bars[0].Height, 3);
        Assert.Equal(50d, bars[1].Y, 3);
        Assert.Equal(20d, bars[1].Height, 3);
        Assert.Equal(10d, bars[2].Y, 3);
        Assert.Equal(10d, bars[2].Height, 3);
    }

    /// <summary>And a chart stacked to the whole takes each as a share of its own category.</summary>
    [Fact]
    public void A_chart_stacked_to_the_whole_is_drawn_in_shares()
    {
        var chart = new ChartDefinition { Grouping = ChartGrouping.PercentStacked, Overlap = 100 };
        var names = new[] { "One", "Two" };

        chart.Series.Add(new ChartSeries("A", names, [30, 10], null));
        chart.Series.Add(new ChartSeries("B", names, [10, 10], null));

        var plan = new ChartComposer.Plan(0, 0, 100, 100, 0, 1, 0.1);
        var bars = ChartComposer.Bars(chart, plan).ToList();

        // Three parts to one in the first category, and one to one in the second.
        Assert.Equal(75d, bars[0].Height, 3);
        Assert.Equal(25d, bars[1].Height, 3);
        Assert.Equal(50d, bars[2].Height, 3);
        Assert.Equal(50d, bars[3].Height, 3);
    }

    /// <summary>
    /// A bar hanging below nought is drawn the other way about: white, and outlined rather than
    /// filled, which is what the format's <c>invertIfNegative</c> asks for and asks for by default.
    /// </summary>
    [Fact]
    public void A_bar_below_nought_is_turned_the_other_way_about()
    {
        var (ours, theirs) = BothWays("chart-bar-stacked");

        // The last page holds one: 45 the wrong side of nought, against five that are not.
        var mine = Fills(ours, page: 7).Where(r => r.Height < 20).ToList();
        var word = Fills(theirs, page: 7).Where(r => r.Height < 20).ToList();

        Assert.Equal(6, mine.Count);
        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine($"{mine[i]} against Word's {word[i]}");
            Assert.Equal(word[i].ColorHex, mine[i].ColorHex);
        }

        // And it is the widest of them, which is the one at 45 rather than any of the rest.
        Assert.Equal("FFFFFF", mine.OrderByDescending(r => r.Width).First().ColorHex);
    }

    /// <summary>A chart lying down runs its categories up the plot, first at the foot.</summary>
    [Fact]
    public void A_lying_chart_starts_its_categories_at_the_foot()
    {
        var chart = new ChartDefinition { Kind = ChartKind.Bar };
        var names = new[] { "One", "Two", "Three" };

        chart.Series.Add(new ChartSeries("A", names, [10, 20, 30], null));

        var plan = new ChartComposer.Plan(0, 0, 100, 150, 0, 100, 20, Lying: true);
        var bars = ChartComposer.Bars(chart, plan).ToList();

        // Each bar runs rightwards from the axis, and the first is in the bottom third.
        Assert.Equal([10d, 20, 30], [.. bars.Select(bar => bar.Width)]);
        Assert.All(bars, bar => Assert.Equal(0d, bar.X, 3));

        Assert.True(bars[0].Y > 100, $"the first category is at {bars[0].Y}, not at the foot.");
        Assert.True(bars[2].Y < 50, $"the last category is at {bars[2].Y}, not at the top.");
    }

    /// <summary>The numbers an axis writes, in the format it asks for.</summary>
    [Theory]
    [InlineData(0.25, "0%", "25%")]
    [InlineData(1, "0%", "100%")]
    [InlineData(0.125, "0.0%", "12.5%")]
    [InlineData(1500000, "#,##0", "1,500,000")]
    [InlineData(1500000, "General", "1500000")]
    [InlineData(2.5, null, "2.5")]
    [InlineData(2, "0.00", "2.00")]
    public void A_number_is_written_the_way_the_axis_asks(double value, string? code, string expected) =>
        Assert.Equal(expected, ChartComposer.Format(value, code));

    /// <summary>
    /// The rectangles a page fills, which is the plot area and the bars: everything else a chart
    /// draws is a stroke, and a stroke is a line rather than a rectangle.
    /// </summary>
    private static List<ExtractedRectangle> Fills(byte[] pdf, int page) =>
        [.. PdfPathExtractor.Extract(pdf)
            .Where(r => r.PageIndex == page && r.Width > 2 && r.Height > 2)
            .OrderBy(r => r.Top)
            .ThenBy(r => r.Left)];

    private static string Part(string? values = null) => $"""
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart><c:plotArea>
            <c:layout><c:manualLayout>
              <c:layoutTarget val="inner"/>
              <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.7"/><c:h val="0.7"/>
            </c:manualLayout></c:layout>
            <c:barChart>
              <c:barDir val="col"/><c:grouping val="clustered"/>
              <c:ser>
                <c:idx val="0"/>
                <c:tx><c:strRef><c:strCache><c:ptCount val="1"/>
                  <c:pt idx="0"><c:v>Units</c:v></c:pt></c:strCache></c:strRef></c:tx>
                <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
                <c:cat><c:strRef><c:strCache><c:ptCount val="4"/>
                  <c:pt idx="0"><c:v>North</c:v></c:pt><c:pt idx="1"><c:v>South</c:v></c:pt>
                  <c:pt idx="2"><c:v>East</c:v></c:pt><c:pt idx="3"><c:v>West</c:v></c:pt>
                </c:strCache></c:strRef></c:cat>
                {values ?? """
                    <c:val><c:numRef><c:numCache><c:ptCount val="4"/>
                      <c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>45</c:v></c:pt>
                      <c:pt idx="2"><c:v>20</c:v></c:pt><c:pt idx="3"><c:v>55</c:v></c:pt>
                    </c:numCache></c:numRef></c:val>
                    """}
              </c:ser>
              <c:gapWidth val="150"/>
            </c:barChart>
            <c:valAx>
              <c:axId val="2"/>
              <c:scaling><c:min val="0"/><c:max val="60"/></c:scaling>
              <c:axPos val="l"/><c:majorGridlines/><c:majorUnit val="20"/>
            </c:valAx>
            <c:catAx><c:axId val="1"/><c:axPos val="b"/></c:catAx>
          </c:plotArea></c:chart>
        </c:chartSpace>
        """;

    private static (byte[] Ours, byte[] Theirs) BothWays(string fixtureName)
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        return (Converter.Convert(Fixtures.Build(fixtureName),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));
    }
}
