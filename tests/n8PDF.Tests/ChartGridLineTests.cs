using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the instrument that finds a chart's gridlines in a rendered page.
/// </summary>
/// <remarks>
/// Checked against pages this repository draws itself, for the reason
/// <see cref="Chart3DSilhouetteTests"/> gives at length: an instrument checked against the thing it
/// exists to measure is not checked. <see cref="PlainPdf"/> writes them byte by byte and touches no
/// n8PDF type.
///
/// The two cases that matter are the two that defeated the attempts this instrument replaced —
/// lines that **converge**, and lines that **touch** something. Both are here with their positions
/// known exactly.
/// </remarks>
public class ChartGridLineTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const double Scale = 8;
    private const double Reference = 300;

    private static readonly (double Left, double Top, double Right, double Bottom) Region = (60, 60, 550, 400);

    /// <summary>A line of a given thickness, as the thin quad that draws it.</summary>
    /// <remarks>
    /// Thickened across its own direction rather than in y, so that an upright line is a real quad
    /// and not a degenerate one. Getting that wrong once meant a border was never drawn and a test
    /// that claimed to prove touching lines are told apart proved nothing at all.
    /// </remarks>
    private static IReadOnlyList<(double X, double Y)> Bar(
        double x0, double y0, double x1, double y1, double thick = 1.0)
    {
        var (dx, dy) = (x1 - x0, y1 - y0);
        var length = Math.Sqrt(dx * dx + dy * dy);
        var (nx, ny) = (-dy / length * thick / 2, dx / length * thick / 2);

        return [(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)];
    }

    /// <summary>How red a pixel is, as a degree rather than a verdict.</summary>
    /// <remarks>
    /// A line a point wide is drawn over several pixels, none of them the stated colour and each a
    /// different way towards it. Returning how far towards it a pixel has come — rather than
    /// whether it has passed a line drawn somewhere — is what lets the detector's answer stay put
    /// when that line moves.
    /// </remarks>
    private static double Reddish((byte R, byte G, byte B) p) =>
        Math.Clamp((Math.Min(p.R - p.G, p.R - p.B) - 6) / 60.0, 0, 1);

    private static double Bluish((byte R, byte G, byte B) p) =>
        Math.Clamp((Math.Min(p.B - p.R, p.B - p.G) - 6) / 60.0, 0, 1);

    private static IReadOnlyList<GridLines.Line>? Found(byte[] pdf)
    {
        if (PdfRasterizer.Render(pdf, 0, Scale) is not { } page)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _outputStatic?.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        return GridLines.Find(page, Scale, Reddish, Region, Reference, leastPixels: 400);
    }

    private static ITestOutputHelper? _outputStatic;

    /// <summary>
    /// Parallel lines are found, and placed to better than a tenth of a point.
    /// </summary>
    [Fact]
    public void Parallel_lines_are_found_and_placed_finely()
    {
        _outputStatic = _output;

        double[] want = [120, 165, 210, 255, 300];

        var pdf = PlainPdf.Of(want.Select(y =>
            (Bar(100, y, 500, y), ((byte)200, (byte)30, (byte)30))));

        if (Found(pdf) is not { } lines) return;

        _output.WriteLine("wanted " + string.Join(", ", want) +
                          "; found " + string.Join(", ", lines.Select(l => l.At.ToString("0.000"))));

        Assert.Equal(want.Length, lines.Count);

        for (var i = 0; i < want.Length; i++)
        {
            Assert.InRange(lines[i].At - want[i], -0.1, 0.1);
            Assert.InRange(lines[i].Slope, -0.01, 0.01);
        }
    }

    /// <summary>
    /// Lines that converge are told apart, which is what defeated sampling down a column.
    /// </summary>
    /// <remarks>
    /// A chart's floor gridlines run to a vanishing point, so their spacing changes across the plot
    /// and no column of pixels crosses them regularly. These fan from a point off the right of the
    /// page; at the reference column they sit where the arithmetic says and nowhere near evenly.
    /// </remarks>
    [Fact]
    public void Converging_lines_are_told_apart()
    {
        _outputStatic = _output;

        // A fan from (700, 240): each line leaves x=100 at a different height and meets there.
        double[] from = [120, 170, 220, 270, 320];
        var want = from.Select(y0 => y0 + (Reference - 100) * (240 - y0) / (700 - 100)).ToArray();

        var pdf = PlainPdf.Of(from.Select(y0 =>
            (Bar(100, y0, 640, y0 + 540 * (240 - y0) / 600.0), ((byte)200, (byte)30, (byte)30))));

        if (Found(pdf) is not { } lines) return;

        _output.WriteLine("wanted " + string.Join(", ", want.Select(v => v.ToString("0.00"))) +
                          "; found " + string.Join(", ", lines.Select(l => l.At.ToString("0.00"))));

        Assert.Equal(want.Length, lines.Count);

        for (var i = 0; i < want.Length; i++)
            Assert.InRange(lines[i].At - want[i], -0.15, 0.15);

        // Genuinely converging: the gaps at the reference column are not the gaps they started with.
        var gaps = Enumerable.Range(1, lines.Count - 1).Select(i => lines[i].At - lines[i - 1].At).ToList();

        _output.WriteLine("gaps " + string.Join(", ", gaps.Select(g => g.ToString("0.00"))));
        Assert.True(gaps.Max() - gaps.Min() < 1, "the fan should stay evenly spaced at one column");
        Assert.True(gaps[0] < 45, "the lines have not converged, so this proves nothing");
    }

    /// <summary>
    /// Lines that run into a border are still found separately, which is what defeated connected
    /// components.
    /// </summary>
    /// <remarks>
    /// The case that killed the obvious approach. Every gridline in a chart meets the plot's own
    /// outline at both ends, so eight-way components join all of them into one blob and there is
    /// nothing left to fit. Voting does not care: a pixel shared between a line and the border adds
    /// a vote to each.
    /// </remarks>
    [Fact]
    public void Lines_that_run_into_a_border_are_still_told_apart()
    {
        _outputStatic = _output;

        double[] want = [120, 165, 210, 255, 300];

        var shapes = new List<(IReadOnlyList<(double X, double Y)>, (byte, byte, byte))>
        {
            // A border in the same colour, which every line touches.
            (Bar(100, 100, 100, 340, 2), ((byte)200, (byte)30, (byte)30)),
            (Bar(500, 100, 500, 340, 2), ((byte)200, (byte)30, (byte)30))
        };

        shapes.AddRange(want.Select(y =>
            ((IReadOnlyList<(double X, double Y)>)Bar(100, y, 500, y), ((byte)200, (byte)30, (byte)30))));

        if (Found(PlainPdf.Of(shapes)) is not { } lines) return;

        _output.WriteLine("wanted " + string.Join(", ", want) +
                          "; found " + string.Join(", ", lines.Select(l => l.At.ToString("0.000"))));

        // The two uprights are not lines by this instrument's lights — they lean far past what it
        // looks for — so only the five should come back.
        Assert.Equal(want.Length, lines.Count);

        for (var i = 0; i < want.Length; i++)
            Assert.InRange(lines[i].At - want[i], -0.1, 0.1);
    }

    /// <summary>
    /// Nothing of that colour, and nothing is claimed.
    /// </summary>
    [Fact]
    public void An_empty_page_yields_nothing()
    {
        _outputStatic = _output;

        var pdf = PlainPdf.Of([(Bar(100, 200, 500, 200), ((byte)30, (byte)30, (byte)200))]);

        if (PdfRasterizer.Render(pdf, 0, Scale) is not { } page)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        Assert.Empty(GridLines.Find(page, Scale, Reddish, Region, Reference, leastPixels: 400));
    }

    /// <summary>
    /// Voting beats grouping the pixels, on the same page.
    /// </summary>
    /// <remarks>
    /// The claim this instrument exists to make, measured rather than asserted. The alternative is
    /// what was tried first and what anyone would try: take the pixels of the colour, join them into
    /// connected blobs, and fit each blob. On a page whose lines touch a border that returns **one**
    /// blob, because they all join through it — so it finds one line where there are five, and the
    /// line it finds is a fit through the whole lot.
    /// </remarks>
    [Fact]
    public void Voting_beats_joining_the_pixels_into_blobs()
    {
        _outputStatic = _output;

        double[] want = [120, 165, 210, 255, 300];

        var shapes = new List<(IReadOnlyList<(double X, double Y)>, (byte, byte, byte))>
        {
            (Bar(100, 100, 100, 340, 2), ((byte)200, (byte)30, (byte)30)),
            (Bar(500, 100, 500, 340, 2), ((byte)200, (byte)30, (byte)30))
        };

        shapes.AddRange(want.Select(y =>
            ((IReadOnlyList<(double X, double Y)>)Bar(100, y, 500, y), ((byte)200, (byte)30, (byte)30))));

        var pdf = PlainPdf.Of(shapes);

        if (PdfRasterizer.Render(pdf, 0, Scale) is not { } page)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var voted = GridLines.Find(page, Scale, Reddish, Region, Reference, leastPixels: 400);

        // The same pixels, joined into blobs instead.
        var lit = new List<(double X, double Y)>();

        for (var y = Region.Top; y < Region.Bottom; y += 1 / Scale)
        for (var x = Region.Left; x < Region.Right; x += 1 / Scale)
            if (Reddish(page.At(x, y, Scale)) > 0.02) lit.Add((x, y));

        var blobs = Blobs(lit);

        _output.WriteLine($"voting finds {voted.Count} lines; joining the pixels finds {blobs} blob(s)");

        Assert.Equal(want.Length, voted.Count);
        Assert.Equal(1, blobs);
    }

    /// <summary>How many connected blobs a set of pixels forms, eight ways.</summary>
    private static int Blobs(List<(double X, double Y)> lit)
    {
        var cells = lit.Select(p => ((int)Math.Round(p.X * Scale), (int)Math.Round(p.Y * Scale))).ToHashSet();
        var seen = new HashSet<(int, int)>();
        var blobs = 0;

        foreach (var start in cells)
        {
            if (!seen.Add(start)) continue;

            blobs++;

            var stack = new Stack<(int X, int Y)>();
            stack.Push(start);

            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();

                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var next = (x + dx, y + dy);

                    if (cells.Contains(next) && seen.Add(next)) stack.Push(next);
                }
            }
        }

        return blobs;
    }

    /// <summary>
    /// Word's own floor gridlines, read as a regular sequence at several tilts.
    /// </summary>
    /// <remarks>
    /// The demonstration rather than the check — there is no ground truth in Word's output to
    /// assert positions against, so what is asserted is the property that both earlier attempts
    /// could not produce: **a regular sequence.**
    ///
    /// Floor gridlines run to a vanishing point, so their spacings grow steadily across the plot.
    /// Sampling down a column gave 2.500, 13.125, 13.000, 12.625, 21.375, 10.500 — no sequence at
    /// all. Connected components gave one blob and so no lines. This gives four gaps that rise
    /// monotonically, at every tilt from 10 degrees to 40.
    ///
    /// That is what #98 needs and could not get: a length in the picture whose ratio to another
    /// does not depend on the scene's own scaling. The **first gap over the last** is exactly such
    /// a ratio, both being in the same picture, and it moves steadily with the tilt — 0.584 at ten
    /// degrees through 0.794 at forty.
    /// </remarks>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(2, 20)]
    [InlineData(4, 30)]
    [InlineData(6, 40)]
    public void Words_floor_gridlines_come_back_as_a_regular_sequence(int page, double rotX)
    {
        _outputStatic = _output;

        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-gridline-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, Scale) is not { } r)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // The whole of the plot, which is 172.8 points tall in this probe — a region sized for a
        // shorter one silently cuts the last lines off and the count comes back wrong.
        var floor = GridLines.Find(r, Scale, Bluish,
            (150, 84, 354, 256), 250, expect: 5, concur: true);

        var gaps = Enumerable.Range(1, floor.Count - 1).Select(i => floor[i].At - floor[i - 1].At).ToList();

        _output.WriteLine($"rotX {rotX}: {floor.Count} lines, gaps " +
                          string.Join(", ", gaps.Select(g => g.ToString("0.000"))));

        // Five lines for five series' worth of floor.
        Assert.Equal(5, floor.Count);

        // Rising steadily, which is the perspective — and is what says these are really the floor's
        // and not the wall's, whose spacings fall.
        for (var i = 1; i < gaps.Count; i++)
            Assert.True(gaps[i] > gaps[i - 1],
                $"rotX {rotX}: the gaps do not rise, so these may not be converging lines");

        // And by enough to be a convergence rather than a rounding.
        Assert.True(gaps[^1] - gaps[0] > 2, $"rotX {rotX}: the gaps barely change across the plot");
    }

    /// <summary>
    /// How much the floor's gridlines converge, which no rescaling can touch.
    /// </summary>
    /// <remarks>
    /// What #98 has been unable to get at: the scene is scaled into the plot rectangle, and that
    /// scaling hides every absolute length. The ratio of the **first** floor gap to the **last** is
    /// immune to it, both being in the same picture and scaled alike — so it depends on the tilt and
    /// on nothing else in the fitting.
    ///
    /// It was to be the wall's spacing against the floor's, but the wall's gridlines are shorter and
    /// the detector still loses one at four tilts out of seven even with them thickened. This wants
    /// only the floor, which now reads five lines at every tilt.
    ///
    /// Recorded here so #98 has it as a measurement rather than as a note: 0.597, 0.638, 0.683,
    /// 0.700, 0.744, 0.752, 0.784 for tilts of ten to forty degrees.
    ///
    /// Those moved when the lines were made to concur — see <c>GridLines.Concurring</c> — and the
    /// earlier figures are wrong rather than merely differently measured. Fitting each line on its
    /// own let one of the five lean independently of its fellows, which lines running away from the
    /// reader cannot do: the old slopes at ten degrees ran 0.081, 0.083, 0.098, 0.102, 0.110, a
    /// sequence with a step four times its neighbours', and they now run 0.080, 0.086, 0.093,
    /// 0.102, 0.113. The wandering line moved half a point where the rest held to a tenth, and
    /// since it bounds the smallest gap it took two percent of the ratio with it. Eighteen of the
    /// twenty settings in <see cref="The_convergence_is_the_same_however_it_is_measured"/> now
    /// agree to half a percent, against five and a half before.
    /// </remarks>
    [Theory]
    [InlineData(0, 10, 0.597)]
    [InlineData(2, 20, 0.683)]
    [InlineData(4, 30, 0.744)]
    [InlineData(6, 40, 0.784)]
    public void The_floors_convergence_moves_with_the_tilt_and_with_nothing_else(
        int page, double rotX, double want)
    {
        _outputStatic = _output;

        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-gridline-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, Scale) is not { } r)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var floor = GridLines.Find(r, Scale, Bluish,
            (150, 84, 354, 256), 250, expect: 5, concur: true);

        Assert.Equal(5, floor.Count);

        var gaps = Enumerable.Range(1, floor.Count - 1).Select(i => floor[i].At - floor[i - 1].At).ToList();
        var convergence = gaps[0] / gaps[^1];

        _output.WriteLine($"rotX {rotX}: first gap over last is {convergence:0.0000}, wanted {want:0.000}");

        Assert.InRange(convergence - want, -0.01, 0.01);
    }

    /// <summary>
    /// The depth of the scene is **not** proportional to <c>c:depthPercent</c>.
    /// </summary>
    /// <remarks>
    /// #98 measured that coefficient at 0.993 and again at 0.988 and took it as settled. Both were
    /// got by fitting a whole picture, which is now known to absorb an error in one term by
    /// adjusting another — it is how a measured inset disappeared into the viewing distance and
    /// nothing moved.
    ///
    /// The floor's convergence cannot do that: it is a ratio of two lengths in one picture, so no
    /// scale, placement or inset is in it. Solving for the depth each of these pages implies, at a
    /// tilt held at 25 degrees, the depth **keeps step at the shallow end and falls behind as it
    /// grows** — from 20 to 25 it is 1.248 where 1.25 is required, but by 50 it is 2.17 where 2.5 is
    /// needed and by 75 it is 2.89 where 3.75 is.
    ///
    /// The ratios are the same to three decimals at every viewing distance tried from 20 to 200, and
    /// the same again whether the perspective divides by the depth component or by the distance to
    /// the eye. So this is a property of Word's drawing and not of any parameter still being
    /// guessed at.
    /// </remarks>
    /// <remarks>
    /// Only the depths where two different detector settings agree to better than a hundredth are
    /// here. At 35 they differ by a twentieth and the reading sits out of sequence; at 200 by a
    /// twelfth; at 400 the far lines crowd past resolution and nothing reliable comes back at all.
    /// Those pages are on the fixture and are deliberately not asserted — a number the instrument
    /// cannot hold still is not a measurement, and pretending otherwise is how the whole-picture
    /// fitting went wrong in the first place.
    /// </remarks>
    [Theory]
    [InlineData(11, 20, 0.909)]
    [InlineData(7, 25, 0.888)]
    [InlineData(8, 50, 0.790)]
    [InlineData(14, 75, 0.741)]
    public void The_scenes_depth_does_not_follow_the_stated_percentage(int page, int depthPercent, double want)
    {
        _outputStatic = _output;

        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-gridline-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, Scale) is not { } r)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var floor = GridLines.Find(r, Scale, Bluish,
            (150, 84, 354, 256), 250, expect: 5, concur: true);

        Assert.Equal(5, floor.Count);

        var gaps = Enumerable.Range(1, floor.Count - 1).Select(i => floor[i].At - floor[i - 1].At).ToList();
        var convergence = gaps[0] / gaps[^1];

        _output.WriteLine($"depthPercent {depthPercent}: convergence {convergence:0.0000}, wanted {want:0.000}");

        Assert.InRange(convergence - want, -0.01, 0.01);
    }

    /// <summary>
    /// The reading does not move when the caller's settings do.
    /// </summary>
    /// <remarks>
    /// The property this instrument was rewritten for, and it is asserted rather than assumed
    /// because assuming it is what went wrong on #98. That story derived a depth from these numbers,
    /// and the derivation multiplies an error by five to eight — so a reading that moved by a
    /// twentieth when a threshold moved was producing depths that moved by a third, and a residual
    /// was chased that was smaller than the error bars on the thing it sat in.
    ///
    /// Twenty settings: five rendering scales, two slope ranges, two search regions. All twenty
    /// must find five lines and agree on the convergence to within a fortieth.
    ///
    /// Eighteen of the twenty agree far closer than that — to about half a percent. The two that
    /// do not move the **first** line by half a point while the other four hold to a tenth, and
    /// since that line bounds the smallest of the four gaps, half a point of it is two percent of
    /// the ratio. The bound is set on the full range rather than on the eighteen, because a test
    /// that quietly dropped its two worst readings would be reporting the instrument it wished it
    /// had.
    ///
    /// A sixteenth is not tight enough for what #98 wants and is not pretended to be. It is what
    /// this instrument does, measured, so that the next thing built on it knows what it is standing
    /// on.
    /// </remarks>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(2, 20)]
    [InlineData(4, 30)]
    [InlineData(6, 40)]
    public void The_reading_holds_still_when_the_settings_move(int page, double rotX)
    {
        _outputStatic = _output;

        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-gridline-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        var pdf = File.ReadAllBytes(path);
        var seen = new List<double>();

        foreach (var scale in new[] { 6.0, 7.0, 8.0, 9.0, 10.0 })
        foreach (var slope in new[] { 0.6, 0.9 })
        foreach (var region in new[] { (150.0, 84.0, 354.0, 256.0), (147.0, 83.0, 357.0, 257.0) })
        {
            if (PdfRasterizer.Render(pdf, page, scale) is not { } r)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                return;
            }

            var found = GridLines.Find(r, scale, Bluish, region, 250, mostSlope: slope, expect: 5,
                concur: true);

            Assert.Equal(5, found.Count);

            var gaps = Enumerable.Range(1, 4).Select(i => found[i].At - found[i - 1].At).ToList();
            seen.Add(gaps[0] / gaps[^1]);
        }

        var spread = (seen.Max() - seen.Min()) / seen.Average();

        _output.WriteLine($"rotX {rotX}: {seen.Count} settings, {seen.Min():0.0000} to {seen.Max():0.0000}, " +
                          $"spread {spread * 100:0.00}%");

        Assert.True(spread < 0.025, $"rotX {rotX}: the reading moves by {spread * 100:0.0}% across settings");
    }
    /// <summary>
    /// The convergence does not depend on which column it is read at, and that is exact rather
    /// than approximate.
    /// </summary>
    /// <remarks>
    /// Worth pinning because a great deal of reasoning on the projection story went the other way.
    /// The reading is the first gap over the last **at a reference column**, gaps between converging
    /// lines plainly change as you move across them, and so the column looked like a free choice that
    /// had been made carelessly — a page coordinate cutting a scene.
    ///
    /// It is not a choice at all once the lines are made to concur. Through a common point,
    /// <c>y_i(x) = V_y + s_i (x - V_x)</c>, so every gap is <c>(s_{i+1} - s_i)(x - V_x)</c> and the
    /// ratio of any two of them is a ratio of slope differences with the <c>x</c> cancelled. Read at
    /// the near edge of the box or the far one, the answer is the same to every decimal that is
    /// printed.
    ///
    /// So the reading is not sensitive to the section. It is sensitive to the **slopes**, and to
    /// their differences, which is a much worse place to be — see
    /// <see cref="What_the_convergence_costs_in_slope_error"/>.
    /// </remarks>
    [Theory]
    [InlineData(0, 10)]
    [InlineData(2, 20)]
    [InlineData(4, 30)]
    [InlineData(6, 40)]
    public void The_convergence_does_not_depend_on_the_column_it_is_read_at(int page, double rotX)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-gridline-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, Scale) is not { } r)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var floor = GridLines.Find(r, Scale, Bluish, (150, 84, 354, 256), 250, expect: 5, concur: true);

        Assert.Equal(5, floor.Count);

        var readings = new List<double>();

        // Right across the box, and past both ends of it for good measure.
        foreach (var column in new[] { 120.0, 180.0, 250.0, 320.0, 380.0 })
        {
            var at = floor.Select(line => line.At + line.Slope * (column - 250)).ToArray();
            var gaps = Enumerable.Range(1, 4).Select(i => at[i] - at[i - 1]).ToArray();

            readings.Add(gaps[0] / gaps[^1]);
        }

        _output.WriteLine($"rotX {rotX}: " + string.Join("  ", readings.Select(v => v.ToString("0.000000"))));

        Assert.InRange(readings.Max() - readings.Min(), 0, 1e-9);
    }

    /// <summary>
    /// What the convergence actually costs: it multiplies a slope's error by about twenty.
    /// </summary>
    /// <remarks>
    /// This is the reason the reading is worth 1.8% (measured on
    /// <see cref="Chart3DSizeTests.The_gap_ratio_moves_between_scenes_that_are_the_same"/>) while the
    /// lines under it are worth 0.068%.
    ///
    /// The five slopes at twenty degrees run 0.1375, 0.1447, 0.1528, 0.1620, 0.1725. The reading is
    /// <c>(s1 - s0) / (s4 - s3)</c> — a ratio of two **differences**, 0.0072 and 0.0105, taken
    /// between numbers of about 0.15. A difference is therefore some twenty times smaller than the
    /// numbers it is taken between, so a relative error in a slope comes out about twenty times
    /// larger in the reading, and the ratio carries two such differences.
    ///
    /// Measured below: moving one slope by a thousandth of itself moves the reading by 1.9%. Read
    /// the other way, the 1.8% the reading is worth across identical scenes is what a slope error of
    /// about a **ten-thousandth** looks like once amplified — which is why better line fitting has
    /// had so little purchase on it.
    ///
    /// That is a property of the measure and not of the detector, and no amount of better line
    /// fitting will remove it — halving the slope error only halves the amplified error. Getting
    /// materially past it needs a scene whose gridlines differ in slope by more, or more of them to
    /// average over.
    ///
    /// Injected here so the figure is a demonstration rather than an assertion: one slope is moved
    /// by a thousandth of itself and the reading is watched.
    /// </remarks>
    [Fact]
    public void What_the_convergence_costs_in_slope_error()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-gridline-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        if (PdfRasterizer.Render(File.ReadAllBytes(path), 2, Scale) is not { } r)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var floor = GridLines.Find(r, Scale, Bluish, (150, 84, 354, 256), 250, expect: 5, concur: true);

        Assert.Equal(5, floor.Count);

        var slopes = floor.Select(line => line.Slope).ToArray();

        static double Reading(double[] s) => (s[1] - s[0]) / (s[4] - s[3]);

        var honest = Reading(slopes);

        // A thousandth of one slope — far finer than the fitting can be held to.
        var nudged = (double[])slopes.Clone();
        nudged[0] += slopes[0] / 1000;

        var moved = Math.Abs(Reading(nudged) - honest) / honest;

        _output.WriteLine($"slopes {string.Join(" ", slopes.Select(v => v.ToString("0.0000")))}");
        _output.WriteLine($"differences {slopes[1] - slopes[0]:0.0000} and {slopes[4] - slopes[3]:0.0000}");
        _output.WriteLine($"a thousandth of one slope moves the reading by {moved * 100:0.0}%");

        // The amplification is the finding: a part in a thousand of a slope is percents of the answer.
        Assert.True(moved > 0.01,
            $"a thousandth of a slope moved the reading only {moved * 100:0.00}%, so the amplification " +
            "this test records has gone — which would be good news worth understanding, not a passing test");
    }
}
