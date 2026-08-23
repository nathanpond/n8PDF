using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests what a three-dimensional projection is measured in.
/// </summary>
/// <remarks>
/// These assert properties of **Word's own output**, not of ours — nothing here draws a
/// three-dimensional plot yet. That is unusual and it is deliberate: three sessions of model-fitting
/// stalled because every probe before this one used a single plot rectangle inside a single chart
/// frame, and in that space a page-unit quantity, a plot-rectangle-unit quantity and a bare constant
/// are the same number. The facts below are what make the space non-degenerate, and they are worth
/// holding onto whether or not the projection that consumes them ever changes.
///
/// The probe holds the eye still throughout. Only the frame the chart is drawn in and the rectangle
/// inside it move, so anything that changes is a consequence of geometry rather than of the scene.
/// </remarks>
public class Chart3DGeometryTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string FixtureName = "chart-3d-geometry-probe";

    private const double Scale = 8;

    /// <summary>Each page's chart frame and the plot rectangle inside it, in points on the page.</summary>
    /// <remarks>The chart is drawn at (72,72) on every page, so these are absolute.</remarks>
    public static readonly (string What, double ChartW, double ChartH,
                            double Left, double Top, double Width, double Height)[] Pages =
    [
        ("base",              360, 216,   144, 93.6, 216, 118.8),
        ("rect wider",        360, 216,   108, 93.6, 288, 118.8),
        ("rect narrower",     360, 216,   180, 93.6, 144, 118.8),
        ("rect taller",       360, 216,   144, 82.8, 216, 172.8),
        ("rect shorter",      360, 216,   144, 115.2, 216, 64.8),
        ("rect moved",        360, 216,   198, 136.8, 216, 118.8),
        ("chart bigger",      432, 259.2, 144, 93.6, 216, 118.8),
        ("chart squarer",     300, 300,   144, 93.6, 216, 118.8),
        ("another scene",     360, 216,   144, 93.6, 216, 118.8),
        ("that one taller",   360, 216,   144, 82.8, 216, 172.8),
        ("that one narrower", 360, 216,   180, 93.6, 144, 118.8)
    ];

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 60 && p.R > p.G + 40 && p.R > p.B + 40;

    /// <summary>The box Word drew on a page, as its six corners.</summary>
    private IReadOnlyList<(double X, double Y)>? Box(byte[] pdf, int page)
    {
        var (_, chartW, chartH, _, _, _, _) = Pages[page];

        if (PdfRasterizer.Render(pdf, page, Scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var shape = BoxSilhouette.Find(rendered, Scale, Reddish, (73, 73, 71 + chartW + 1, 71 + chartH + 1));

        Assert.True(shape.Found, $"{Pages[page].What}: {shape.Refused}");

        return shape.Points;
    }

    /// <summary>Word's own output for the probe.</summary>
    /// <remarks>
    /// No check for Word's faces, unlike most tests touching this fixture, and deliberately: nothing
    /// here converts the document. Every assertion is about Word's committed reference and is read
    /// out of it by colour, so the fonts the fixture is written in do not come into it and these run
    /// wherever the suite runs — including the hosted tier, which has not got Word's faces and skips
    /// the comparison for this fixture.
    /// </remarks>
    private static byte[]? Reference()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    private static (double W, double H, double X, double Y) Bounds(IReadOnlyList<(double X, double Y)> box) =>
        (box.Max(c => c.X) - box.Min(c => c.X), box.Max(c => c.Y) - box.Min(c => c.Y),
         box.Min(c => c.X), box.Min(c => c.Y));

    /// <summary>
    /// The chart frame plays no part: only the plot rectangle inside it does.
    /// </summary>
    /// <remarks>
    /// The page that carries the whole argument. Pages 7 and 8 put the plot rectangle at exactly the
    /// same place and size on the page as page 1 — 216 x 118.8 at (144, 93.6) — inside a frame that
    /// is a fifth bigger and inside one that is square. If the chart frame entered into the
    /// projection at all, three different frames could not draw the same picture, and the squarer
    /// one could not draw it to the hundredth of a point.
    ///
    /// This is what says every quantity in the projection is a ratio of the plot rectangle, which is
    /// the answer #108 was filed to get.
    /// </remarks>
    [Theory]
    [InlineData(6, "a frame a fifth bigger")]
    [InlineData(7, "a square frame")]
    public void The_chart_frame_plays_no_part_in_the_projection(int page, string what)
    {
        if (Reference() is not { } pdf) return;
        if (Box(pdf, 0) is not { } baseline || Box(pdf, page) is not { } other) return;

        var (bw, bh, bx, by) = Bounds(baseline);
        var (ow, oh, ox, oy) = Bounds(other);

        _output.WriteLine($"{what}: {ow:0.00}x{oh:0.00} at ({ox:0.00},{oy:0.00}) " +
                          $"against the baseline's {bw:0.00}x{bh:0.00} at ({bx:0.00},{by:0.00})");

        // A quarter point, which is one pixel of Word's own 300 dpi bitmap. The square frame lands
        // dead on; the bigger one is within a fifth of a point, being rasterised at another size.
        Assert.InRange(ow - bw, -0.25, 0.25);
        Assert.InRange(oh - bh, -0.25, 0.25);
        Assert.InRange(ox - bx, -0.25, 0.25);
        Assert.InRange(oy - by, -0.25, 0.25);
    }

    /// <summary>
    /// Moving the plot rectangle moves the picture with it, and changes nothing else.
    /// </summary>
    /// <remarks>
    /// What separates scale from placement, which one rectangle could not. The rectangle keeps its
    /// size and shifts by 54 points across and 43.2 down — and it is no longer centred in its chart,
    /// which is the other thing this rules out: the picture follows the rectangle, not the frame's
    /// middle.
    /// </remarks>
    [Fact]
    public void Moving_the_plot_rectangle_moves_the_picture_and_nothing_else()
    {
        if (Reference() is not { } pdf) return;
        if (Box(pdf, 0) is not { } baseline || Box(pdf, 5) is not { } moved) return;

        var (bw, bh, bx, by) = Bounds(baseline);
        var (mw, mh, mx, my) = Bounds(moved);

        var (dx, dy) = (Pages[5].Left - Pages[0].Left, Pages[5].Top - Pages[0].Top);

        _output.WriteLine($"the rectangle moved by ({dx:+0.0;-0.0},{dy:+0.0;-0.0}) and the box by " +
                          $"({mx - bx:+0.00;-0.00},{my - by:+0.00;-0.00}); its size changed by " +
                          $"{Math.Abs(mw - bw):0.00}x{Math.Abs(mh - bh):0.00}");

        Assert.InRange(mx - bx - dx, -0.25, 0.25);
        Assert.InRange(my - by - dy, -0.25, 0.25);
        Assert.InRange(mw - bw, -0.25, 0.25);
        Assert.InRange(mh - bh, -0.25, 0.25);
    }

    /// <summary>
    /// The rectangle's width changes the picture even when its height does not move.
    /// </summary>
    /// <remarks>
    /// This is the one that kills the simplest reading of what #98 had measured. It had the scene
    /// scaled to fit the rectangle's **height**, which is true — but it also had the box's own shape
    /// as a constant, and a constant shape scaled by a height that has not moved would draw exactly
    /// the same picture in a wider rectangle. Word draws a taller one.
    ///
    /// So the box's shape depends on the rectangle too, and fitting the whole probe says how: its
    /// height over its width is the **rectangle's** height over its width. Against a constant that
    /// is 3.3pt to 9.8pt over eleven pages spanning five rectangle shapes from 0.30 to 0.825, which
    /// the single rectangle used before this could not have distinguished.
    /// </remarks>
    [Fact]
    public void The_rectangles_width_changes_the_picture_at_a_fixed_height()
    {
        if (Reference() is not { } pdf) return;
        if (Box(pdf, 0) is not { } baseline || Box(pdf, 1) is not { } wider) return;

        Assert.Equal(Pages[0].Height, Pages[1].Height);

        var (bw, bh, _, _) = Bounds(baseline);
        var (ww, wh, _, _) = Bounds(wider);

        _output.WriteLine($"at a rectangle height of {Pages[0].Height} throughout: " +
                          $"{Pages[0].Width}pt wide gives a box {bw:0.00}x{bh:0.00}, " +
                          $"{Pages[1].Width}pt wide gives {ww:0.00}x{wh:0.00}");

        // Taller by several points, not by a rounding.
        Assert.True(wh - bh > 2,
            $"a wider rectangle changed the box's height by only {wh - bh:0.00}pt, so the width may not enter into it");
    }

    /// <summary>
    /// The height is what the scene is fitted to, and the width is left to fall where it falls.
    /// </summary>
    /// <remarks>
    /// Measured across rectangles from 64.8 to 172.8 points tall. The box never fills its
    /// rectangle's width — it comes out between two thirds and nine tenths of it — while its height
    /// tracks the rectangle's closely, the shortfall being the 40 of 100 up the value axis the bar
    /// does not reach.
    ///
    /// That asymmetry is what #98 found and could not prove on one rectangle: fitting by width
    /// scores 26pt where fitting by height scores 3.3, and taking the smaller of the two gives an
    /// answer identical to fitting by height on every page — so the height is always the binding
    /// constraint.
    /// </remarks>
    [Fact]
    public void The_scene_is_fitted_to_the_rectangles_height()
    {
        if (Reference() is not { } pdf) return;

        foreach (var page in new[] { 0, 1, 2, 3, 4 })
        {
            if (Box(pdf, page) is not { } box) return;

            var (w, h, _, _) = Bounds(box);
            var p = Pages[page];

            _output.WriteLine($"  {p.What,-14} rectangle {p.Width,5:0.0}x{p.Height,5:0.0}  " +
                              $"box {w,6:0.00}x{h,6:0.00}  = {w / p.Width:0.000} across, {h / p.Height:0.000} up");

            // Never fills the width.
            Assert.True(w / p.Width < 0.95,
                $"{p.What}: the box fills {w / p.Width:0.000} of the rectangle's width, so the width may be binding");

            // And always takes most of the height, the bar reaching 60 of 100 up a box that does.
            Assert.InRange(h / p.Height, 0.7, 0.85);
        }
    }

    /// <summary>
    /// The quarter point the tests above hold to is tight enough to tell pages apart.
    /// </summary>
    /// <remarks>
    /// A test that two pictures agree is worth nothing unless disagreement would show. The
    /// same comparison is run here over pairs that ought to differ — a different rectangle, and the
    /// same rectangle moved but compared without allowing for the move — and every one of them is
    /// required to fail it.
    ///
    /// Without this, a bar loose enough to pass everything would read exactly like the finding that
    /// the chart frame plays no part.
    /// </remarks>
    [Theory]
    [InlineData(0, 1, "a wider rectangle")]
    [InlineData(0, 2, "a narrower one")]
    [InlineData(0, 3, "a taller one")]
    [InlineData(0, 4, "a shorter one")]
    [InlineData(0, 5, "the same rectangle moved, not allowed for")]
    [InlineData(0, 8, "another scene in the same rectangle")]
    public void Pages_that_ought_to_differ_do(int left, int right, string what)
    {
        if (Reference() is not { } pdf) return;
        if (Box(pdf, left) is not { } a || Box(pdf, right) is not { } b) return;

        var (aw, ah, ax, ay) = Bounds(a);
        var (bw, bh, bx, by) = Bounds(b);

        var worst = new[] { bw - aw, bh - ah, bx - ax, by - ay }.Max(Math.Abs);

        _output.WriteLine($"{what}: differs by {worst:0.00}pt at worst");

        Assert.True(worst > 0.25,
            $"{what} differs by only {worst:0.00}pt, so the quarter point above proves nothing");
    }
}
