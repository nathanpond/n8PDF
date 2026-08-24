using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// How a three-dimensional plot arranges its series — in depth, in a cluster, or in a pile —
/// and the depth axis that names the rows, held to Word's output.
/// </summary>
/// <remarks>
/// <see cref="Chart3DArrangement"/> holds the rules; this holds them to Word. The receding case
/// rides the committed <c>chart-3d-slot-probe</c>, whose red-marked bars Word placed under
/// swept gaps and counts; the groupings and the axis ride <c>chart-3d-depth-axis-probe</c>.
///
/// The clustered rows carry a wide bar deliberately: the cluster's own rule — bars abutting,
/// together filling <c>n/(n + gapWidth/100)</c> of the slot — is exact on three pages, but the
/// clustered <b>box's</b> proportions are only bounded (measured 1.90, 1.42 and 3.61 units wide
/// where the provisional rule says 2, 1.5 and 4), and the follow-up issue holds those
/// measurements. Until it closes, a clustered page is held to the family rather than the
/// quarter point.
/// </remarks>
public class Chart3DDepthAxisTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 120 && p.G < 90 && p.B < 90;

    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double X, double Y)>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Astray(List<(double X, double Y)> model, IReadOnlyList<(double X, double Y)> word)
    {
        var n = word.Count;
        if (model.Count != n) return double.PositiveInfinity;
        var best = double.PositiveInfinity;
        for (var shift = 0; shift < n; shift++)
        foreach (var reverse in new[] { false, true })
        {
            double worst = 0;
            for (var i = 0; i < n; i++)
            {
                var idx = reverse ? (shift - i % n + 2 * n) % n : (shift + i) % n;
                var dx = model[idx].X - word[i].X;
                var dy = model[idx].Y - word[i].Y;
                worst = Math.Max(worst, Math.Sqrt(dx * dx + dy * dy));
            }
            best = Math.Min(best, worst);
        }
        return best;
    }

    private IReadOnlyList<(double X, double Y)>? WordCorners(string fixture, int page)
    {
        if (TestFonts.SkipForMissingFonts(fixture)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        const double scale = 6;
        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var shape = BoxSilhouette.Find(rendered, scale, Reddish, (73, 73, 431, 287));
        if (!shape.Found)
        {
            _output.WriteLine($"{fixture} p{page}: {shape.Refused}");
            return null;
        }
        return shape.Points;
    }

    /// <summary>The marked bar's silhouette under the arrangement and the oblique projection.</summary>
    private static List<(double X, double Y)> ModelBar(
        string grouping, int categories, int series, int gapWidth, int gapDepth,
        int category, int index, double value, int rotX = 15)
    {
        var chart = new n8PDF.Ooxml.ChartDefinition
        {
            GapWidth = gapWidth,
            GapDepth = gapDepth,
            Grouping = grouping switch
            {
                "standard" => n8PDF.Ooxml.ChartGrouping.Standard,
                "stacked" => n8PDF.Ooxml.ChartGrouping.Stacked,
                _ => n8PDF.Ooxml.ChartGrouping.Clustered,
            },
        };
        var names = Enumerable.Range(0, categories).Select(i => $"C{i}").ToList();
        for (var j = 0; j < series; j++)
            chart.Series.Add(new n8PDF.Ooxml.ChartSeries($"S{j}", names, [], null));

        var arrangement = Chart3DArrangement.For(chart);
        var projection = new Chart3DObliqueProjection(rotX, 20, 100, null,
            arrangement.WidthUnits, arrangement.DepthUnits, 144, 93.6, 216, 118.8,
            arrangement.HeightUnits);

        var (x0, x1) = arrangement.Across(chart, category, index);
        var (z0, z1) = arrangement.Depth(chart, index);
        var top = value - Chart3DObliqueProjection.BarTopShortfall;

        var points = new List<(double X, double Y)>();
        foreach (var x in new[] { x0, x1 })
        foreach (var y in new[] { 0.0, top })
        foreach (var z in new[] { z0, z1 })
            points.Add(projection.Project(x, y, z));
        return Hull(points);
    }

    /// <summary>
    /// A receding chart's bars keep to their slots — across under <c>gapWidth</c> and in depth
    /// under <c>gapDepth</c> — corner for corner against the committed slot probe.
    /// </summary>
    /// <remarks>
    /// The across rows are the probe's unoccluded pages: several categories, one series, the
    /// marked bar in full view. The depth rows put the marked bar at the front, where nothing
    /// covers it. The bar is the projection's own (#97) plus the finder's fraction.
    /// </remarks>
    [Theory]
    [InlineData(1, 2, 150, 0, 0, "two categories, gapWidth 150, first bar")]
    [InlineData(2, 2, 300, 0, 1, "two categories, gapWidth 300, second bar")]
    [InlineData(4, 3, 150, 0, 0, "three categories, gapWidth 150, first bar")]
    [InlineData(5, 3, 150, 0, 1, "three categories, gapWidth 150, middle bar")]
    [InlineData(6, 3, 300, 0, 2, "three categories, gapWidth 300, last bar")]
    [InlineData(8, 4, 150, 0, 2, "four categories, gapWidth 150, third bar")]
    [InlineData(9, 4, 300, 0, 1, "four categories, gapWidth 300, second bar")]
    [InlineData(15, 3, 50, 0, 1, "held back: three categories, gapWidth 50, middle bar", 1.5)]
    public void A_bar_keeps_to_its_slot_across(
        int page, int categories, int gapWidth, int gapDepth, int red, string what,
        double bar = 0.85)
    {
        if (WordCorners("chart-3d-slot-probe", page) is not { } word) return;

        var model = ModelBar("standard", categories, 1, gapWidth, gapDepth, red, 0, 0.6);
        var astray = Astray(model, word);

        // The gapWidth 50 page carries a wider bar: its bars nearly abut, and the finder reads
        // the red bar's edge through the blend against its grey neighbour rather than against
        // white, which costs most of a pixel on each side.
        _output.WriteLine($"{what}: worst corner {astray:0.000}pt");
        Assert.True(astray < bar, $"{what}: {astray:0.000}pt astray");
    }

    /// <summary>
    /// And to its row in depth, under <c>gapDepth</c>, with the front row unhidden.
    /// </summary>
    [Theory]
    [InlineData(11, 2, 150, 0, "two series, gapDepth 150, nearest")]
    public void A_bar_keeps_to_its_row_in_depth(int page, int series, int gapDepth, int red, string what)
    {
        if (WordCorners("chart-3d-slot-probe", page) is not { } word) return;

        var model = ModelBar("standard", 1, series, 0, gapDepth, 0, red, 0.6);
        var astray = Astray(model, word);

        _output.WriteLine($"{what}: worst corner {astray:0.000}pt");
        Assert.True(astray < 0.85, $"{what}: {astray:0.000}pt astray");
    }

    /// <summary>
    /// A stacked chart puts every series in one row: the pile of three thirties reads as a
    /// single box of ninety in a one-row scene, to the projection's own tolerance.
    /// </summary>
    [Fact]
    public void A_stacked_chart_piles_its_series_in_one_row()
    {
        if (WordCorners("chart-3d-depth-axis-probe", 0) is not { } word) return;

        var model = ModelBar("stacked", 1, 3, 150, 150, 0, 0, 0.9);
        var astray = Astray(model, word);

        _output.WriteLine($"stacked union: worst corner {astray:0.000}pt");
        Assert.True(astray < 0.6, $"the stacked pile is {astray:0.000}pt astray");
    }

    /// <summary>
    /// A clustered chart puts its series side by side across, abutting, the cluster filling
    /// <c>n/(n + gapWidth/100)</c> of the slot — held to the family while the clustered box's
    /// own proportions stay bounded rather than pinned.
    /// </summary>
    [Theory]
    [InlineData(1, 1, 3, 0, 2, "one category, three series, the union")]
    [InlineData(5, 1, 2, 0, 1, "one category, two series, the union")]
    public void A_clustered_chart_goes_across_not_back(
        int page, int categories, int series, int firstIndex, int lastIndex, string what)
    {
        if (WordCorners("chart-3d-depth-axis-probe", page) is not { } word) return;

        // The union of the abutting bars: from the first bar's left to the last bar's right.
        var first = ModelBar("clustered", categories, series, 150, 150, 0, firstIndex, 0.6);
        var last = ModelBar("clustered", categories, series, 150, 150, categories - 1, lastIndex, 0.6);
        var model = Hull([.. first, .. last]);

        var astray = Astray(model, word);
        _output.WriteLine($"{what}: worst corner {astray:0.000}pt");
        Assert.True(astray < 3.5, $"{what}: {astray:0.000}pt astray — outside even the bounded family");
    }

    /// <summary>
    /// The depth axis's labels are the series names, real text, each a fixed reach from its
    /// row's centre on the box's right depth edge — ours within half a point of Word's.
    /// </summary>
    [Fact]
    public void The_depth_axis_names_its_rows_in_text()
    {
        const string fixture = "chart-3d-depth-axis-probe";
        if (TestFonts.SkipForMissingFonts(fixture)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf");
        var ours = Converter.Convert(Fixtures.Build(fixture),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var mine = PdfTextExtractor.Extract(ours)
            .Where(r => r.PageIndex == 3 && r.Text.StartsWith('S')).OrderBy(r => r.Text).ToList();
        var words = PdfTextExtractor.ExtractFile(reference)
            .Where(r => r.PageIndex == 3 && r.Text.StartsWith('S')).OrderBy(r => r.Text).ToList();

        Assert.Equal(3, words.Count);
        Assert.Equal(3, mine.Count);

        foreach (var (m, w) in mine.Zip(words))
        {
            _output.WriteLine($"{w.Text}: ours ({m.X:0.00},{m.BaselineY:0.00}) " +
                              $"word ({w.X:0.00},{w.BaselineY:0.00})");
            Assert.True(Math.Abs(m.X - w.X) < 0.5 && Math.Abs(m.BaselineY - w.BaselineY) < 0.5,
                $"{w.Text} is ({m.X - w.X:+0.00;-0.00},{m.BaselineY - w.BaselineY:+0.00;-0.00}) from Word's");
        }
    }

    /// <summary>
    /// Put back wrong, each rule fails: a clustered chart drawn receding, a slot filled to its
    /// edges, and labels hung on the boundaries rather than the rows.
    /// </summary>
    [Fact]
    public void Put_back_wrong_the_arrangement_fails()
    {
        // Clustered drawn receding: the standard model against the clustered union page.
        if (WordCorners("chart-3d-depth-axis-probe", 1) is { } union)
        {
            var receding = ModelBar("standard", 1, 3, 150, 150, 0, 0, 0.6);
            var astray = Astray(receding, union);
            _output.WriteLine($"clustered drawn receding: {astray:0.000}pt");
            Assert.True(astray > 5, $"a receding front bar lands only {astray:0.000}pt from the cluster");
        }

        // The gap dropped: a bar filling its whole slot, against a gapped page.
        if (WordCorners("chart-3d-slot-probe", 1) is { } gapped)
        {
            var full = ModelBar("standard", 2, 1, 0, 0, 0, 0, 0.6);
            var astray = Astray(full, gapped);
            _output.WriteLine($"the gap dropped: {astray:0.000}pt");
            Assert.True(astray > 5, $"an ungapped bar lands only {astray:0.000}pt from the gapped one");
        }
    }
}
