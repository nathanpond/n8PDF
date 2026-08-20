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
/// The fixtures place their plot area by hand and state what their axes run between, which pins
/// everything that can be measured against and leaves Word's automatic sizing — the part that is
/// not implemented — out of the way. See the README for what that means.
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
    /// Twelve charts differing only in the numbers they hold. Two rules account for every one: the
    /// step is the largest of one, two or five times a power of ten that is no more than a fifth
    /// of the span, and the top is the smallest multiple of that step lying strictly above the
    /// largest value. The strictness is what puts a chart of exactly 100 at 120 rather than leaving
    /// its tallest bar against the frame.
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
    private static List<string> AxisLabels(byte[] pdf, int page) =>
        [.. PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && !run.Text.Contains('C') &&
                          !string.IsNullOrWhiteSpace(run.Text))
            .GroupBy(run => Math.Round(run.BaselineY, 1))
            .OrderBy(group => group.Key)
            .Select(group => string.Concat(group.OrderBy(run => run.X).Select(run => run.Text.Trim())))
            .Reverse()];

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
