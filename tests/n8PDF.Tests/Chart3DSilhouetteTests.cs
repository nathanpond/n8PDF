using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the instrument that finds a projected box's corners in a rendered page.
/// </summary>
/// <remarks>
/// Everything measuring a three-dimensional projection depends on this, so it is checked against
/// shapes whose corners are known **exactly** rather than against Word. A box is projected here, by
/// arithmetic written out in this file, and drawn to a PDF written by hand in
/// <see cref="PlainPdf"/> — so the corners the instrument is asked to find are the corners this test
/// computed, to the last decimal place, and nothing in the library stands between the two.
///
/// The point of the whole exercise is accuracy finer than a pixel. Word's bitmap is 300 dpi, which
/// is 0.24pt to the pixel, and the projection has to be pinned finer than that or #98 cannot tell a
/// right formula from a close one. The claim is that fitting lines to edges and intersecting them
/// beats looking at pixels, and the last test here does not take that on faith: it runs the
/// pixel-wise alternative over the same mask and compares.
/// </remarks>
public class Chart3DSilhouetteTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>The eight corners of a box, projected the way a chart's would be.</summary>
    /// <remarks>
    /// Not a claim about what Word does — that is #98's question and this file has no opinion on it.
    /// It is a projection with the properties that make the instrument's job hard: near-vertical
    /// edges, three faces in view, and no two edges parallel on the page.
    /// </remarks>
    private static List<(double X, double Y)> Project(
        double rotX, double rotY, double distance, double scale, double atX, double atY)
    {
        var corners = new List<(double X, double Y)>();

        var (sy, cy) = Math.SinCos(rotY * Math.PI / 180);
        var (sx, cx) = Math.SinCos(rotX * Math.PI / 180);

        foreach (var z in new[] { -0.5, 0.5 })
        foreach (var y in new[] { -0.5, 0.5 })
        foreach (var x in new[] { -0.5, 0.5 })
        {
            var (x1, z1) = (x * cy + z * sy, -x * sy + z * cy);
            var (y2, z2) = (y * cx + z1 * sx, -y * sx + z1 * cx);

            var near = distance / (distance - z2);

            corners.Add((atX + x1 * near * scale, atY - y2 * near * scale));
        }

        return corners;
    }

    /// <summary>The six faces of that box, each as four of its corners.</summary>
    private static readonly int[][] Faces =
    [
        [0, 1, 3, 2], [4, 5, 7, 6],   // back and front
        [0, 1, 5, 4], [2, 3, 7, 6],   // bottom and top
        [0, 2, 6, 4], [1, 3, 7, 5]    // left and right
    ];

    /// <summary>
    /// A page carrying that box, painted the way Word paints one: three faces in view, each the same
    /// hue at a different lightness.
    /// </summary>
    private static byte[] Page(List<(double X, double Y)> corners, double[] depths)
    {
        var shades = new (byte R, byte G, byte B)[] { (200, 30, 30), (255, 70, 70), (150, 20, 20) };

        // Painted furthest first, so what ends up in view is what a solid box would show.
        var order = Enumerable.Range(0, Faces.Length)
            .OrderByDescending(f => Faces[f].Average(c => depths[c]))
            .ToList();

        return PlainPdf.Of(order.Select((face, i) =>
            ((IReadOnlyList<(double X, double Y)>)[.. Faces[face].Select(c => corners[c])],
             shades[i % shades.Length])));
    }

    /// <summary>How deep each corner of the box lies, for painting them in order.</summary>
    private static double[] Depths(double rotX, double rotY)
    {
        var depths = new List<double>();

        var (sy, cy) = Math.SinCos(rotY * Math.PI / 180);
        var (sx, cx) = Math.SinCos(rotX * Math.PI / 180);

        foreach (var z in new[] { -0.5, 0.5 })
        foreach (var y in new[] { -0.5, 0.5 })
        foreach (var x in new[] { -0.5, 0.5 })
        {
            var z1 = -x * sy + z * cy;
            depths.Add(-y * sx + z1 * cx);
        }

        return [.. depths];
    }

    /// <summary>The silhouette of those eight points: their convex hull, which is the truth.</summary>
    private static List<(double X, double Y)> Truth(List<(double X, double Y)> corners)
    {
        var sorted = corners.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var built = new List<(double X, double Y)>();

        foreach (var p in sorted)
        {
            while (built.Count >= 2 && Cross(built[^2], built[^1], p) <= 1e-9) built.RemoveAt(built.Count - 1);
            built.Add(p);
        }

        var lower = built.Count + 1;
        built.RemoveAt(built.Count - 1);

        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            while (built.Count >= lower && Cross(built[^2], built[^1], sorted[i]) <= 1e-9)
                built.RemoveAt(built.Count - 1);
            built.Add(sorted[i]);
        }

        built.RemoveAt(built.Count - 1);

        // Started at the leftmost, as the instrument reports them.
        var first = 0;
        for (var i = 1; i < built.Count; i++) if (built[i].X < built[first].X) first = i;

        return [.. Enumerable.Range(0, built.Count).Select(i => built[(first + i) % built.Count])];
    }

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 60 && p.R > p.G + 40 && p.R > p.B + 40;

    private const double Scale = 6;

    private static readonly (double Left, double Top, double Right, double Bottom) Region = (40, 40, 572, 500);

    /// <summary>How far the instrument's corners lie from the truth, worst of the six.</summary>
    private static double Worst(
        IReadOnlyList<(double X, double Y)> found, IReadOnlyList<(double X, double Y)> truth)
    {
        var worst = 0.0;

        foreach (var t in truth)
            worst = Math.Max(worst,
                found.Min(f => Math.Sqrt((f.X - t.X) * (f.X - t.X) + (f.Y - t.Y) * (f.Y - t.Y))));

        return worst;
    }

    /// <summary>
    /// The corners come back finer than a pixel, over a range of rotations.
    /// </summary>
    /// <remarks>
    /// The claim this instrument exists to make. Word's pixel is 0.24pt; the bar here is 0.1pt,
    /// which is what #106 asked for and roughly two fifths of a pixel at Word's resolution and two
    /// and a half at the scale these are rendered.
    /// </remarks>
    [Theory]
    [InlineData(15, 20, "the scene Word uses when a chart says nothing")]
    [InlineData(30, 20, "tilted further")]
    [InlineData(15, 40, "turned further")]
    [InlineData(25, 55, "both, well away from the defaults")]
    [InlineData(10, 15, "barely turned, where the edges nearly run on into each other")]
    [InlineData(12, 18, "a little more")]
    public void The_corners_come_back_finer_than_a_pixel(double rotX, double rotY, string what)
    {
        var corners = Project(rotX, rotY, 4, 260, 300, 260);
        var truth = Truth(corners);

        Assert.Equal(BoxSilhouette.Corners, truth.Count);

        var page = PdfRasterizer.Render(Page(corners, Depths(rotX, rotY)), 0, Scale);

        if (page is null)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var shape = BoxSilhouette.Find(page, Scale, Reddish, Region);

        Assert.True(shape.Found, $"{what}: {shape.Refused}");
        Assert.Equal(BoxSilhouette.Corners, shape.Points.Count);

        var worst = Worst(shape.Points, truth);

        _output.WriteLine($"{what}: worst corner is {worst:0.0000}pt out " +
                          $"({worst / 0.24:0.00} of a pixel at Word's 300 dpi)");

        Assert.True(worst < 0.1, $"{what}: worst corner is {worst:0.000}pt out, wanted under 0.1pt");
    }

    /// <summary>A shape running outside the region is refused rather than measured.</summary>
    /// <remarks>
    /// The failure that actually happened. A bar reaching the top of its value axis is cut by the
    /// plot area, and hunting for extremes in what is left returns the corners of the plot — which
    /// look like a perfectly good answer and are an answer to a different question. Every page of
    /// the first attempt at #98 came back with the same two numbers before anyone noticed.
    /// </remarks>
    [Fact]
    public void A_shape_cut_by_the_region_is_refused()
    {
        var corners = Project(15, 20, 4, 260, 300, 260);
        var page = PdfRasterizer.Render(Page(corners, Depths(15, 20)), 0, Scale);

        if (page is null) { _output.WriteLine(PdfRasterizer.UnavailableMessage); return; }

        // A region whose foot cuts through the box.
        var refused = BoxSilhouette.Find(page, Scale, Reddish, (40, 40, 572, 300));

        Assert.False(refused.Found);
        Assert.Empty(refused.Points);
        _output.WriteLine($"refused: {refused.Refused}");
        Assert.Contains("cut", refused.Refused);
    }

    /// <summary>Nothing of that colour is said out loud rather than guessed at.</summary>
    [Fact]
    public void An_absent_shape_is_refused()
    {
        var corners = Project(15, 20, 4, 260, 300, 260);
        var page = PdfRasterizer.Render(Page(corners, Depths(15, 20)), 0, Scale);

        if (page is null) { _output.WriteLine(PdfRasterizer.UnavailableMessage); return; }

        var refused = BoxSilhouette.Find(page, Scale, p => p.B > 200 && p.R < 50, Region);

        Assert.False(refused.Found);
        Assert.Contains("pixels of that colour", refused.Refused);
    }

    /// <summary>
    /// A shape that is not a box seen in three dimensions is refused.
    /// </summary>
    /// <remarks>
    /// A rectangle is the case that matters: a box turned to face the viewer square on draws one,
    /// and it says nothing about depth. Returning six corners for it — two pairs coincident — would
    /// be arithmetically true and useless, so it is refused instead.
    /// </remarks>
    [Fact]
    public void A_shape_that_is_not_a_box_in_three_dimensions_is_refused()
    {
        var page = PdfRasterizer.Render(
            PlainPdf.Of([([(200.0, 150.0), (400.0, 150.0), (400.0, 300.0), (200.0, 300.0)], (200, 30, 30))]),
            0, Scale);

        if (page is null) { _output.WriteLine(PdfRasterizer.UnavailableMessage); return; }

        var refused = BoxSilhouette.Find(page, Scale, Reddish, Region);

        _output.WriteLine($"a rectangle: {(refused.Found ? "accepted" : refused.Refused)}");
        Assert.False(refused.Found);
    }

    /// <summary>
    /// A box turned too nearly square-on is refused rather than answered badly.
    /// </summary>
    /// <remarks>
    /// The instrument's own limit, and it is a limit of the geometry rather than of the fitting: two
    /// nearly-collinear edges cross at a place that moves a long way for a very small error in
    /// either. At 8° and 12° of rotation the outline turns by 2.6° at its shallowest corner and the
    /// answer is 0.224pt out — more than twice what this is required to hold to, and it would look
    /// like a perfectly good answer to anything that used it.
    ///
    /// Refusing is what makes the tenth of a point above mean something: a number is returned only
    /// where it is worth the name.
    /// </remarks>
    [Fact]
    public void A_box_turned_too_nearly_square_on_is_refused()
    {
        var corners = Project(8, 12, 4, 260, 300, 260);
        var page = PdfRasterizer.Render(Page(corners, Depths(8, 12)), 0, Scale);

        if (page is null) { _output.WriteLine(PdfRasterizer.UnavailableMessage); return; }

        var refused = BoxSilhouette.Find(page, Scale, Reddish, Region);

        _output.WriteLine($"8° and 12°: {(refused.Found ? "accepted" : refused.Refused)}");

        Assert.False(refused.Found);
        Assert.Contains("square-on", refused.Refused);

        // And the truth it would have got wrong is still a proper hexagon, so this is a refusal on
        // conditioning rather than on the shape being the wrong sort.
        Assert.Equal(BoxSilhouette.Corners, Truth(corners).Count);
    }

    /// <summary>
    /// Fitting lines to the edges beats reading corners off the pixels, on the same pixels.
    /// </summary>
    /// <remarks>
    /// The instrument's whole claim, put to the test rather than asserted. The alternative is what
    /// anyone would write first and what #98 did write: take the outline, reduce it to six corners,
    /// and use those pixels as the answer. Both are given the same mask from the same page; the only
    /// difference is whether the corners come from pixels or from where fitted lines cross.
    ///
    /// A pixel here is 1/6 of a point and the outline's corners can only ever land on one, so the
    /// pixel-wise answer cannot do better than about half that however carefully it is written.
    /// Fitting has no such floor, because tens of pixels along an edge average down the error of
    /// each.
    /// </remarks>
    [Fact]
    public void Fitted_edges_beat_reading_the_corners_off_the_pixels()
    {
        var corners = Project(15, 20, 4, 260, 300, 260);
        var truth = Truth(corners);
        var page = PdfRasterizer.Render(Page(corners, Depths(15, 20)), 0, Scale);

        if (page is null) { _output.WriteLine(PdfRasterizer.UnavailableMessage); return; }

        var fitted = BoxSilhouette.Find(page, Scale, Reddish, Region);
        Assert.True(fitted.Found, fitted.Refused);

        // The same mask, with the corners taken as the pixels the outline turns on.
        var pixels = new List<(double X, double Y)>();

        for (var py = (int)(Region.Top * Scale); py < (int)(Region.Bottom * Scale); py++)
        for (var px = (int)(Region.Left * Scale); px < (int)(Region.Right * Scale); px++)
        {
            var at = (py * page.Pixels.Width + px) * 3;

            if (Reddish((page.Pixels.Data[at], page.Pixels.Data[at + 1], page.Pixels.Data[at + 2])))
                pixels.Add(((px + 0.5) / Scale, (py + 0.5) / Scale));
        }

        var byPixel = BoxSilhouette.CornersFromPixels(pixels);

        var (fittedWorst, pixelWorst) = (Worst(fitted.Points, truth), Worst(byPixel, truth));

        _output.WriteLine($"fitted edges: {fittedWorst:0.0000}pt out; " +
                          $"read off the pixels: {pixelWorst:0.0000}pt out; " +
                          $"a pixel at this scale is {1 / Scale:0.0000}pt");

        // Better, and by enough to matter rather than by a rounding.
        Assert.True(fittedWorst < pixelWorst / 2,
            $"fitting gained little: {fittedWorst:0.000}pt against {pixelWorst:0.000}pt");

        // And the pixel-wise answer is genuinely too coarse for what #98 needs, which is the reason
        // this instrument exists at all.
        Assert.True(pixelWorst > 0.1, $"reading off the pixels was already good enough at {pixelWorst:0.000}pt");
    }
}
