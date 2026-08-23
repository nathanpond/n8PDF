using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests how Word shades the faces of a three-dimensional bar, and what it fills the walls with.
/// </summary>
/// <remarks>
/// Assertions about Word's own output, as <see cref="Chart3DGeometryTests"/> explains. Unlike the
/// rest of the three-dimensional work this one needed no geometry at all: each face is a flat fill,
/// so counting the distinct colours inside the plot finds them without knowing where any of them is.
/// That is what let it be measured while the projection was still unsettled.
///
/// The finding that ties it together is that **the shade belongs to the face's orientation, not to
/// the thing the face is part of**. A bar's front and the back wall are both unshaded; a bar's top
/// and the floor are both three quarters; a bar's side and the side wall are both five eighths. One
/// rule, not two.
/// </remarks>
public class Chart3DShadingTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const double Scale = 6;

    /// <summary>Each page: what the series is coloured, and what the walls state.</summary>
    public static readonly (string What, string Colour, string Walls)[] Pages =
    [
        ("saturated red",   "FF0000", "none"),
        ("dark red",        "800000", "none"),
        ("mid blue",        "4080C0", "none"),
        ("near black",      "202020", "none"),
        ("near white",      "E0E0E0", "none"),
        ("saturated green", "00C000", "none"),
        ("walls stated",    "FF00FF", "all"),
        ("floor only",      "FF00FF", "floor"),
        ("walls unstated",  "FF00FF", "none"),
        ("walls misplaced", "FF00FF", "misplaced")
    ];

    private static byte[] Reference()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-shading-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>Every colour covering more than a given number of samples inside the plot.</summary>
    private Dictionary<(byte R, byte G, byte B), int>? Colours(int page, int atLeast)
    {
        if (PdfRasterizer.Render(Reference(), page, Scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var counts = new Dictionary<(byte, byte, byte), int>();

        for (var y = 95.0; y < 212; y += 1.0 / Scale)
        for (var x = 145.0; x < 359; x += 1.0 / Scale)
        {
            var q = rendered.At(x, y, Scale);
            counts.TryGetValue(q, out var n);
            counts[q] = n + 1;
        }

        return counts.Where(e => e.Value >= atLeast).ToDictionary(e => e.Key, e => e.Value);
    }

    private static (byte R, byte G, byte B) Parse(string hex) =>
        (Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex[4..], 16));

    /// <summary>How much of the stated colour a drawn one is, averaged over the channels it has.</summary>
    private static double Factor((byte R, byte G, byte B) stated, (byte R, byte G, byte B) drawn)
    {
        var of = new List<double>();

        if (stated.R > 0) of.Add((double)drawn.R / stated.R);
        if (stated.G > 0) of.Add((double)drawn.G / stated.G);
        if (stated.B > 0) of.Add((double)drawn.B / stated.B);

        return of.Average();
    }

    /// <summary>
    /// A bar shows three faces: its own colour, three quarters of it, and five eighths.
    /// </summary>
    /// <remarks>
    /// The rule is a **multiply**, and the near-black page is what proves it against an addition.
    /// <c>202020</c> shades to <c>181818</c> and <c>141414</c> — three quarters and five eighths
    /// exactly. An additive rule fitted to the saturated red would take away 62 and 92, which sends
    /// 32 below nought twice over. That is why these six colours and not six others: a dark one and
    /// a light one part the two rules company, and the saturated pair say the factor is applied per
    /// channel rather than to a lightness.
    ///
    /// The factors are measured over all six at [0.750, 0.766] and [0.625, 0.646]. The spread is
    /// Word's own: its bitmap shifts <c>00C000</c> to <c>00C100</c> before anything is shaded, so a
    /// unit or two of noise is in every reading. A clean three quarters and five eighths sit just
    /// outside the top of both intervals and so cannot be claimed either.
    /// </remarks>
    [Theory]
    [InlineData(0, "saturated red")]
    [InlineData(1, "dark red")]
    [InlineData(2, "mid blue")]
    [InlineData(3, "near black")]
    [InlineData(4, "near white")]
    [InlineData(5, "saturated green")]
    public void A_bar_shows_its_colour_and_two_shades_of_it(int page, string what)
    {
        if (Colours(page, 3000) is not { } counts) return;

        var stated = Parse(Pages[page].Colour);

        // The three biggest that are not the white behind them.
        var faces = counts
            .Where(e => e.Key != ((byte)255, (byte)255, (byte)255))
            .OrderByDescending(e => e.Value)
            .Take(3)
            .Select(e => (Colour: e.Key, Of: Factor(stated, e.Key)))
            .OrderByDescending(f => f.Of)
            .ToList();

        _output.WriteLine($"{what} ({Pages[page].Colour}): " + string.Join("  ",
            faces.Select(f => $"{f.Colour.R:X2}{f.Colour.G:X2}{f.Colour.B:X2} = {f.Of:0.000}x")));

        Assert.Equal(3, faces.Count);

        // Its own colour, near enough — Word's raster moves it by a unit before anything else.
        Assert.InRange(faces[0].Of, 0.99, 1.02);

        // Three quarters, and five eighths.
        Assert.InRange(faces[1].Of, 0.745, 0.775);
        Assert.InRange(faces[2].Of, 0.620, 0.655);

        // And they are genuinely three different faces, not one colour counted thrice.
        Assert.True(faces[0].Of - faces[1].Of > 0.15, $"{what}: the first two faces are too close to tell apart");
        Assert.True(faces[1].Of - faces[2].Of > 0.08, $"{what}: the last two faces are too close to tell apart");
    }

    /// <summary>
    /// The walls and floor are unfilled unless the document says otherwise.
    /// </summary>
    [Fact]
    public void The_walls_are_unfilled_where_nothing_is_stated()
    {
        if (Colours(8, 3000) is not { } counts) return;

        var white = counts[((byte)255, (byte)255, (byte)255)];
        var rest = counts.Where(e => e.Key != ((byte)255, (byte)255, (byte)255)).Sum(e => e.Value);

        _output.WriteLine($"nothing stated: {white} white against {rest} of everything else");

        // Only the bar is inked, and it is a tenth of the scale, so the plot is overwhelmingly white.
        Assert.True(white > rest * 15,
            $"the plot is only {(double)white / (white + rest):0.00} white, so something may be filling the walls");
    }

    /// <summary>
    /// A stated fill is honoured, and shaded by the same rule the bar's faces are.
    /// </summary>
    /// <remarks>
    /// The result that makes this one rule rather than two. The back wall faces the viewer as a
    /// bar's front does and is drawn **unshaded**; the floor lies flat as a bar's top does and is
    /// drawn at three quarters; the side wall stands as a bar's side does and is drawn at five
    /// eighths. Stated <c>FF0000</c>, <c>0000FF</c> and <c>00C000</c> come out <c>FF0000</c>,
    /// <c>0000C1</c> and <c>007C00</c>.
    ///
    /// So the shade belongs to the face's orientation and not to the object — which is worth knowing
    /// before #99 draws the walls and #101 draws the boxes, since it is one rule to implement.
    /// </remarks>
    [Fact]
    public void A_stated_wall_is_honoured_and_shaded_like_a_bar_face_of_the_same_lie()
    {
        if (Colours(6, 3000) is not { } counts) return;

        (string What, string Stated, double Low, double High)[] want =
        [
            ("the back wall", "FF0000", 0.98, 1.02),
            ("the floor",     "0000FF", 0.745, 0.775),
            ("the side wall", "00C000", 0.620, 0.655)
        ];

        foreach (var (whatWall, statedHex, low, high) in want)
        {
            var stated = Parse(statedHex);

            // The largest area whose colour is a multiple of what was asked for.
            var drawn = counts
                .Where(e => Multiple(stated, e.Key))
                .OrderByDescending(e => e.Value)
                .Select(e => ((byte R, byte G, byte B)?)e.Key)
                .FirstOrDefault();

            Assert.True(drawn is not null, $"{whatWall} was stated {statedHex} and nothing of that hue was drawn");

            var of = Factor(stated, drawn.Value);

            _output.WriteLine($"{whatWall}: stated {statedHex}, drawn " +
                              $"{drawn.Value.R:X2}{drawn.Value.G:X2}{drawn.Value.B:X2} = {of:0.000}x");

            Assert.InRange(of, low, high);
        }

        // A colour is a multiple of another where the channels that were nought stay nought and the
        // rest keep their proportion — which is what tells a shaded red from a shaded blue.
        static bool Multiple((byte R, byte G, byte B) stated, (byte R, byte G, byte B) drawn)
        {
            if (stated.R == 0 && drawn.R > 12) return false;
            if (stated.G == 0 && drawn.G > 12) return false;
            if (stated.B == 0 && drawn.B > 12) return false;

            return drawn.R + drawn.G + drawn.B > 40;
        }
    }

    /// <summary>Each of the three can be stated on its own.</summary>
    [Fact]
    public void The_floor_can_be_stated_by_itself()
    {
        if (Colours(7, 3000) is not { } counts) return;

        var blue = counts.Where(e => e.Key.B > 100 && e.Key.R < 40 && e.Key.G < 40).Sum(e => e.Value);
        var red = counts.Where(e => e.Key.R > 100 && e.Key.G < 40 && e.Key.B < 40).Sum(e => e.Value);
        var green = counts.Where(e => e.Key.G > 100 && e.Key.R < 40 && e.Key.B < 40).Sum(e => e.Value);

        _output.WriteLine($"floor alone: {blue} blue, {red} red, {green} green");

        Assert.True(blue > 3000, "the floor was stated blue and no blue was drawn");
        Assert.Equal(0, red);
        Assert.Equal(0, green);
    }

    /// <summary>
    /// The same fills put inside <c>c:plotArea</c> are silently ignored.
    /// </summary>
    /// <remarks>
    /// Where these elements go is not obvious and getting it wrong costs nothing visible — Word
    /// neither complains nor draws anything. This page is the same document as the one that works,
    /// with <c>c:floor</c>, <c>c:sideWall</c> and <c>c:backWall</c> moved from <c>c:chart</c> into
    /// <c>c:plotArea</c>, and it comes out identical to stating nothing at all.
    ///
    /// It is here because the rule above — that a stated fill is honoured — means nothing without a
    /// page showing what *not* being honoured looks like. It cost an afternoon to find.
    /// </remarks>
    [Fact]
    public void Wall_fills_put_in_the_plot_area_are_ignored()
    {
        if (Colours(9, 3000) is not { } misplaced || Colours(8, 3000) is not { } unstated) return;

        var coloured = misplaced
            .Where(e => e.Key != ((byte)255, (byte)255, (byte)255))
            .Where(e => e.Key.R > 100 && e.Key.G < 40 && e.Key.B < 40
                     || e.Key.B > 100 && e.Key.R < 40 && e.Key.G < 40
                     || e.Key.G > 100 && e.Key.R < 40 && e.Key.B < 40)
            .Sum(e => e.Value);

        _output.WriteLine($"misplaced: {coloured} samples of the three stated colours");
        _output.WriteLine($"white: {misplaced[((byte)255, (byte)255, (byte)255)]} " +
                          $"against {unstated[((byte)255, (byte)255, (byte)255)]} when nothing is stated");

        Assert.Equal(0, coloured);

        // And the page is the one that states nothing, to within the bar's own antialiasing.
        Assert.InRange(misplaced[((byte)255, (byte)255, (byte)255)] -
                       unstated[((byte)255, (byte)255, (byte)255)], -2000, 2000);
    }
}
