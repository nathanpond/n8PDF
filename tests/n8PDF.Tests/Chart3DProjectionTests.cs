using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The projection Word uses when the axes are held square, which is not a rotation.
/// </summary>
/// <remarks>
/// #98 asked for "the rotation order and its sign conventions". There is no rotation order, because
/// there is no rotation. With <c>rAngAx="1"</c> Word draws an **oblique** projection: a scene point
/// at <c>(x, y, z)</c> in box units lands at
///
/// <code>
/// screen = scale × ( x + z·sin(rotY) ,  −y − z·sin(rotX) )
/// </code>
///
/// so the width axis stays exactly horizontal, the height axis exactly vertical, and only the depth
/// axis leans. A genuine rotation cannot do that: turning a box about the vertical and then the
/// horizontal sends the width axis to <c>(cos rotY, sin rotX·sin rotY)</c>, which is off the
/// horizontal at every angle where both rotations are doing anything —
/// <see cref="A_rotation_would_tilt_the_width_axis_and_word_does_not"/> puts numbers on that.
///
/// Measured over both angles swept separately, as the depth edge's offset per unit of width:
///
/// | | `rotY` 5 | 10 | 20 | 35 | 50 | 65 |
/// |---|---|---|---|---|---|---|
/// | across | 0.0869 | 0.1732 | 0.3416 | 0.5730 | 0.7623 | 0.9087 |
/// | `sin rotY` | 0.0872 | 0.1736 | 0.3420 | 0.5736 | 0.7660 | 0.9063 |
///
/// | | `rotX` 5 | 10 | 30 | 45 | 60 |
/// |---|---|---|---|---|---|
/// | down | 0.0885 | 0.1736 | 0.4979 | 0.7079 | 0.8577 |
/// | `sin rotX` | 0.0872 | 0.1736 | 0.5000 | 0.7071 | 0.8660 |
///
/// and two pages with **both** angles away from anything used above, held back: 40° by 45° reads
/// (0.7087, 0.6437) for a predicted (0.7071, 0.6428), and 25° by 60° reads (0.8691, 0.4259) for
/// (0.8660, 0.4226).
///
/// A note on how nearly this went wrong. The first run of this probe reported the sine form fitting
/// **perfectly** on all thirteen pages — because the fixture's angles were hardcoded and every page
/// was the same picture, so what was being confirmed was that 0.342 equals 0.342. The parameters were
/// added and never reached the XML, which C# does not warn about. It showed only because thirteen
/// different angles cannot all read 0.3420 to four places.
/// </remarks>
public class Chart3DProjectionTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-rotation-probe";

    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    /// <summary>
    /// The box's depth offset per unit of width, and how far its width edge is off the horizontal.
    /// </summary>
    private (double Across, double Down, double WidthTilt)? Axes(byte[] pdf, int page)
    {
        const double scale = 6;

        if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered) return null;

        var shape = BoxSilhouette.Find(rendered, scale, Reddish, (73, 73, 431, 287));

        if (!shape.Found)
        {
            _output.WriteLine($"page {page}: {shape.Refused}");
            return null;
        }

        var points = shape.Points;

        var low = 0;
        for (var i = 1; i < points.Count; i++)
            if (points[i].Y > points[low].Y) low = i;

        var one = points[(low - 1 + points.Count) % points.Count];
        var other = points[(low + 1) % points.Count];
        var (across, depth) = Math.Abs(one.Y - points[low].Y) < 0.5 ? (one, other) : (other, one);

        var width = Math.Abs(across.X - points[low].X);

        if (width < 1) return null;

        return ((depth.X - points[low].X) / width,
                -(depth.Y - points[low].Y) / width,
                Math.Abs(across.Y - points[low].Y) / width);
    }

    private byte[]? Reference()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// A unit of depth lands <c>sin(rotY)</c> across and <c>sin(rotX)</c> up the page.
    /// </summary>
    /// <remarks>
    /// The two rotations act on the depth axis and on nothing else, and each acts on one screen
    /// direction alone: <c>rotY</c> on how far the depth leans across, <c>rotX</c> on how far it
    /// rises. Neither touches the other's component, which is the sharpest thing here — under a real
    /// rotation they would be thoroughly mixed.
    /// </remarks>
    [Theory]
    [InlineData(0, 20, 5, false)]
    [InlineData(1, 20, 10, false)]
    [InlineData(2, 20, 20, false)]
    [InlineData(3, 20, 35, false)]
    [InlineData(4, 20, 50, false)]
    [InlineData(5, 20, 65, false)]
    [InlineData(6, 5, 20, false)]
    [InlineData(7, 10, 20, false)]
    [InlineData(8, 30, 20, false)]
    [InlineData(9, 45, 20, false)]
    [InlineData(10, 60, 20, false)]
    [InlineData(11, 40, 45, true)]
    [InlineData(12, 25, 60, true)]
    public void A_unit_of_depth_lands_by_the_sines_of_the_rotations(
        int page, int rotX, int rotY, bool heldBack)
    {
        if (Reference() is not { } pdf) return;

        if (Axes(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var across = Math.Sin(rotY * Math.PI / 180);
        var down = Math.Sin(rotX * Math.PI / 180);

        _output.WriteLine($"rotX {rotX}, rotY {rotY}{(heldBack ? " (held back)" : "")}: " +
                          $"a unit of depth lands ({measured.Across:0.0000}, {measured.Down:0.0000}), " +
                          $"the sines say ({across:0.0000}, {down:0.0000})");

        Assert.InRange(measured.Across - across, -0.01, 0.01);
        Assert.InRange(measured.Down - down, -0.01, 0.01);
    }

    /// <summary>
    /// The width axis stays flat on the page, whatever either rotation is doing.
    /// </summary>
    /// <remarks>
    /// What makes this a projection rather than a rotation, and it is checked at the extremes rather
    /// than the middle: at 60° by 20° and at 25° by 60° the width edge is still level to a
    /// thousandth of its own length.
    /// </remarks>
    [Theory]
    [InlineData(5, 20, 65)]
    [InlineData(10, 60, 20)]
    [InlineData(11, 40, 45)]
    [InlineData(12, 25, 60)]
    public void The_width_axis_stays_level(int page, int rotX, int rotY)
    {
        if (Reference() is not { } pdf) return;

        if (Axes(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        _output.WriteLine($"rotX {rotX}, rotY {rotY}: the width edge is off level by " +
                          $"{measured.WidthTilt:0.0000} of its own length");

        Assert.InRange(measured.WidthTilt, 0, 0.005);
    }

    /// <summary>
    /// The rotation that #98 asked for the order of would not draw this picture.
    /// </summary>
    /// <remarks>
    /// The injection. Turn a box about the vertical by <c>rotY</c> and then about the horizontal by
    /// <c>rotX</c>, and the width axis — which started along the screen's horizontal — comes to rest
    /// at <c>(cos rotY, sin rotX · sin rotY)</c>. That is off the horizontal by
    /// <c>tan(rotX)·tan(rotY)</c> of its length, which at 60° by 20° is **0.63**, and Word's is level
    /// to a thousandth.
    ///
    /// So no order of two rotations produces it, and no choice of signs rescues one — the tilt is
    /// there for either order and both signs. That is why this story's "rotation order and its sign
    /// conventions" has no answer: the question presumed a rotation.
    /// </remarks>
    [Theory]
    [InlineData(10, 60, 20)]
    [InlineData(12, 25, 60)]
    public void A_rotation_would_tilt_the_width_axis_and_word_does_not(int page, int rotX, int rotY)
    {
        if (Reference() is not { } pdf) return;

        if (Axes(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        // Where a turn about the vertical then the horizontal would leave the width axis.
        var turned = Math.Abs(Math.Sin(rotX * Math.PI / 180) * Math.Sin(rotY * Math.PI / 180))
                     / Math.Cos(rotY * Math.PI / 180);

        _output.WriteLine($"rotX {rotX}, rotY {rotY}: a rotation would tilt the width axis by " +
                          $"{turned:0.000} of its length; Word tilts it by {measured.WidthTilt:0.000}");

        Assert.True(turned > 20 * Math.Max(measured.WidthTilt, 0.001),
            $"a rotation's tilt ({turned:0.000}) is no longer far enough above Word's " +
            $"({measured.WidthTilt:0.000}) for this page to tell them apart");
    }
}
