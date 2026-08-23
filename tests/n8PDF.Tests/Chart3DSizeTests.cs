using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Measures how accurate a reading off a three-dimensional plot actually is, as against how
/// repeatable it is.
/// </summary>
/// <remarks>
/// Every convergence measured for the projection has carried a **reproducibility** figure and no
/// **accuracy** figure, and the two were treated as one number through six successive instruments.
/// <see cref="ChartGridLineTests.The_reading_holds_still_when_the_settings_move"/> sweeps twenty
/// settings and reports how far the reading moves; but all twenty read the same raster through the
/// same detection, so whatever biases one page biases all twenty together. It bounds how repeatable
/// a reading is and says nothing about how right it is.
///
/// This probe separates them. It draws **one scene** — <c>rotX</c> 25, depth 100, everything held —
/// at five frame sizes in the same 5:3, so the plot rectangle scales uniformly and nothing about the
/// box's own shape moves; the aspect is deliberately not varied, because the box's height follows the
/// rectangle's aspect and that would be a different question. A sixth page repeats the middle size
/// shifted by a fractional indent, which moves the gridlines' sub-pixel phase in Word's 300 dpi
/// raster and changes nothing else.
///
/// What comes out is a factor of twenty-six, and it lands on the measure rather than on the
/// detector — see <see cref="The_gap_ratio_moves_between_scenes_that_are_the_same"/>.
/// </remarks>
public class Chart3DSizeTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-size-probe";

    /// <summary>The frames, in the order the probe draws them.</summary>
    private static readonly string[] Frames =
        ["240 by 144", "300 by 180", "360 by 216", "420 by 252", "480 by 288", "360 by 216 shifted"];

    private readonly ITestOutputHelper _output = output;

    private static double Bluish((byte R, byte G, byte B) pixel) =>
        Math.Clamp((Math.Min(pixel.B - pixel.R, pixel.B - pixel.G) - 6) / 60.0, 0, 1);

    /// <summary>
    /// The cross ratio of four points, which a projection cannot change.
    /// </summary>
    private static double Cross(double a, double b, double c, double d) =>
        (c - a) / (c - b) * ((d - b) / (d - a));

    /// <summary>Where the floor's gridlines are on a page, and how much room they take.</summary>
    /// <remarks>
    /// The search region is taken from the ink rather than stated in points, because the whole point
    /// of the probe is that the chart is a different size on every page. A region stated once would
    /// cut the big ones off and swallow the frame on the small ones, which is the quiet failure that
    /// bit <see cref="ChartGridLineTests"/> when its fixture grew taller.
    /// </remarks>
    private static (IReadOnlyList<GridLines.Line> Lines, double Left, double Right)? Floor(
        RenderedPage page, double scale)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        for (var y = 0.0; y < 780; y += 1 / scale)
        for (var x = 0.0; x < 610; x += 1 / scale)
        {
            if (Bluish(page.At(x, y, scale)) <= 0.02) continue;

            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        if (right <= left) return null;

        var lines = GridLines.Find(page, scale, Bluish, (left - 2, top - 2, right + 2, bottom + 2),
            (left + right) / 2, expect: 5, concur: true);

        return (lines, left, right);
    }

    private byte[]? Reference()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// The probe really does draw one scene at several sizes, which everything else here presumes.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed. If Word were fitting the box differently at different sizes —
    /// which is exactly the sort of thing it does elsewhere — then the pages would not be the same
    /// scene and a spread between them would mean nothing. The floor's ink is measured against the
    /// frame it was asked for: the ratio comes out 0.283 at every size, and the shifted page sits
    /// 3.6 points right of the one it repeats, for the 74 twips it was given.
    /// </remarks>
    [Fact]
    public void The_scene_is_the_same_shape_at_every_size()
    {
        if (Reference() is not { } pdf) return;

        const double scale = 8;
        double[] widths = [240, 300, 360, 420, 480, 360];

        var shares = new List<double>();
        var placed = new List<double>();

        for (var page = 0; page < Frames.Length; page++)
        {
            if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            var found = Floor(rendered, scale);
            Assert.NotNull(found);

            var (_, left, right) = found.Value;

            shares.Add((right - left) / widths[page]);
            placed.Add(left);

            _output.WriteLine($"{Frames[page]}: floor ink {right - left:0.00} wide from {left:0.00}, " +
                              $"{(right - left) / widths[page]:0.0000} of the frame");
        }

        // One shape at five sizes: the share of the frame the floor takes cannot move.
        Assert.InRange(shares.Max() - shares.Min(), 0, 0.002);

        // And the sixth page is the third one moved sideways and nothing else. 74 twips is 3.7pt,
        // which lands between raster columns — the point of it is that it changes the phase.
        Assert.InRange(placed[5] - placed[2], 3.3, 4.1);
    }

    /// <summary>
    /// The cross ratio of the floor's lines is four thirds, and how far off it comes out is what
    /// the line finding is actually worth.
    /// </summary>
    /// <remarks>
    /// This is the only measurement here whose right answer is known in advance, and it is known
    /// without a model of anything. The gridlines stand at even steps of depth; a projection carries
    /// four evenly spaced collinear points to four points of cross ratio **exactly 4/3**, whatever
    /// the tilt, the perspective, the depth or the size. So every departure from 4/3 is measurement
    /// error and nothing else, with no fitting, no reference implementation and no appeal to Word.
    ///
    /// Being a tautology is precisely what makes it useful, and it is worth saying plainly that it
    /// **cannot** be used to measure the projection: it is 4/3 at <c>rotX</c> 10 and at <c>rotX</c>
    /// 40 and at every depth, while the gap ratio over those same pages runs from 0.597 to 0.909.
    /// A constant that does not move when the scene moves tells you about your ruler, not the thing
    /// you are measuring.
    ///
    /// Measured: over 28 readings, five rendering scales across six pages, the worst departure is
    /// **0.068%**. The same check on the tilt probe's eleven pages is worst at 0.089%. The bound
    /// here is set at 0.15%, above both.
    ///
    /// The smallest frame at the two coarsest rendering scales finds only three lines, so it cannot
    /// be read; that is reported rather than passed over, since a reading that quietly disappears is
    /// how a probe comes to prove less than it claims.
    /// </remarks>
    [Fact]
    public void The_cross_ratio_of_the_floor_lines_is_four_thirds()
    {
        if (Reference() is not { } pdf) return;

        var off = new List<double>();
        var unreadable = 0;

        foreach (var scale in new[] { 6.0, 7.0, 8.0, 9.0, 10.0 })
        for (var page = 0; page < Frames.Length; page++)
        {
            if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            if (Floor(rendered, scale) is not { } found) continue;

            // Word draws one gridline the fewer on the two smallest frames — its own choice of tick
            // interval, and CT_SerAx has no majorUnit to pin it with. Four is all this needs.
            if (found.Lines.Count < 4)
            {
                unreadable++;
                _output.WriteLine($"{Frames[page]} at {scale}: only {found.Lines.Count} lines, not read");
                continue;
            }

            var at = found.Lines.Select(line => line.At).ToArray();

            off.Add(Cross(at[0], at[1], at[2], at[3]) - 4.0 / 3);
        }

        _output.WriteLine($"{off.Count} readings, {unreadable} unreadable; off four thirds by " +
                          $"{off.Min() / (4.0 / 3) * 100:+0.000;-0.000}% to {off.Max() / (4.0 / 3) * 100:+0.000;-0.000}%");

        Assert.True(off.Count >= 24, $"only {off.Count} readings, too few to say anything");

        Assert.All(off, one =>
            Assert.InRange(Math.Abs(one) / (4.0 / 3), 0, 0.0015));
    }

    /// <summary>
    /// The gap ratio moves between scenes that are the same, and by twenty-six times what the line
    /// finding costs.
    /// </summary>
    /// <remarks>
    /// This is the number the projection story needed and never had.
    ///
    /// The convergence it has been reasoning from is the first gap over the last **at a reference
    /// column**, and that is not a projective invariant: the lines converge, so the gaps between
    /// them depend on where they are sectioned. Across these six pages the scene is provably one
    /// scene — <see cref="The_scene_is_the_same_shape_at_every_size"/> holds it to two parts in a
    /// thousand — and the column is put at the middle of the floor's ink every time, so the reading
    /// ought to be identical. It moves by **1.8%**.
    ///
    /// It cannot be the lines, because the same fitted lines give a cross ratio good to 0.068%. What
    /// is left is the section: the middle of the ink is a ragged estimate of a corresponding column,
    /// and a fraction of a point of it is a per cent of the smallest gap. That error is invisible to
    /// a settings sweep, which never moves the scene, and it is roughly the size of the roughness in
    /// the tilt series that three separate instruments have now failed to explain.
    ///
    /// So this asserts the ordering rather than only the bound: whatever the absolute figures drift
    /// to, the gap ratio must remain much the worse measure, because that is the finding.
    /// </remarks>
    [Fact]
    public void The_gap_ratio_moves_between_scenes_that_are_the_same()
    {
        if (Reference() is not { } pdf) return;

        var ratios = new List<double>();
        var cross = new List<double>();

        foreach (var scale in new[] { 6.0, 7.0, 8.0, 9.0, 10.0 })
        for (var page = 0; page < Frames.Length; page++)
        {
            if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            if (Floor(rendered, scale) is not { } found || found.Lines.Count < 4) continue;

            var at = found.Lines.Select(line => line.At).ToArray();

            cross.Add(Math.Abs(Cross(at[0], at[1], at[2], at[3]) - 4.0 / 3) / (4.0 / 3));

            if (found.Lines.Count != 5) continue;

            var gaps = Enumerable.Range(1, 4).Select(i => at[i] - at[i - 1]).ToArray();

            ratios.Add(gaps[0] / gaps[^1]);
        }

        var spread = (ratios.Max() - ratios.Min()) / ratios.Average();
        var worstCross = cross.Max();

        _output.WriteLine($"gap ratio: {ratios.Count} readings, {ratios.Min():0.0000} to {ratios.Max():0.0000}, " +
                          $"spread {spread * 100:0.00}%");
        _output.WriteLine($"cross ratio over the same lines: worst {worstCross * 100:0.000}%");
        _output.WriteLine($"the gap ratio is {spread / worstCross:0} times the worse");

        Assert.True(ratios.Count >= 12, $"only {ratios.Count} readings, too few to say anything");

        // What is measured, guarded against drifting further.
        Assert.InRange(spread, 0.005, 0.03);

        // And the claim itself: the section costs an order of magnitude more than the lines do.
        Assert.True(spread > 8 * worstCross,
            $"the gap ratio's spread ({spread * 100:0.00}%) is no longer much worse than the cross " +
            $"ratio's error ({worstCross * 100:0.000}%), so the finding this test records has changed");
    }

    /// <summary>
    /// The cross ratio check fails when the lines are put back wrong.
    /// </summary>
    /// <remarks>
    /// A test whose quantity is a constant needs showing that it can fail, because a bug that
    /// returned four evenly spaced numbers — or the same number four times — would sail through a
    /// check written carelessly. So a real reading is taken and one line is moved by half a point,
    /// which is the size of the wandering that the concurrency constraint was put in to stop.
    /// </remarks>
    [Fact]
    public void Moving_one_line_half_a_point_breaks_the_cross_ratio()
    {
        if (Reference() is not { } pdf) return;

        const double scale = 8;

        if (PdfRasterizer.Render(pdf, 2, scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var found = Floor(rendered, scale);
        Assert.NotNull(found);
        Assert.Equal(5, found.Value.Lines.Count);

        var at = found.Value.Lines.Select(line => line.At).ToArray();

        // As measured, it passes.
        Assert.InRange(Math.Abs(Cross(at[0], at[1], at[2], at[3]) - 4.0 / 3) / (4.0 / 3), 0, 0.0015);

        // Half a point out on the second line, and it does not.
        var moved = Math.Abs(Cross(at[0], at[1] + 0.5, at[2], at[3]) - 4.0 / 3) / (4.0 / 3);

        _output.WriteLine($"one line half a point out puts the cross ratio {moved * 100:0.00}% off four thirds");

        Assert.True(moved > 0.0015,
            $"half a point of error only moved the cross ratio {moved * 100:0.000}%, so this check " +
            "would not catch a line that wandered");
    }
}
