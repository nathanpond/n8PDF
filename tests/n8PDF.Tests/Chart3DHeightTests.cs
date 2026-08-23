using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests what <c>c:hPercent</c> does to a three-dimensional box, and what its absence does.
/// </summary>
/// <remarks>
/// These assert Word's own output, for the reason <see cref="Chart3DGeometryTests"/> gives: nothing
/// draws a three-dimensional plot yet, and these facts are what the projection will be built on.
///
/// The finding worth stating first is that **saying nothing is not the same as saying anything**.
/// <c>c:hPercent</c> absent gives a box as tall relative to its width as the plot rectangle is —
/// which is what #108 measured — and no value of <c>hPercent</c> reproduces that in general, because
/// the rectangle can be any shape. So this element has no numeric default at all, and a reader that
/// substituted 100 for it would draw a box a third narrower than Word's on the probe's own baseline.
/// </remarks>
public class Chart3DHeightTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string FixtureName = "chart-3d-height-probe";
    private const double Scale = 8;

    /// <summary>Each page: what it states, and the plot rectangle it states it in.</summary>
    public static readonly (string What, int? HPercent, double Width, double Height)[] Pages =
    [
        ("no view3D at all",     null, 216, 118.8),
        ("stated, no hPercent",  null, 216, 118.8),
        ("hPercent 25",            25, 216, 118.8),
        ("hPercent 50",            50, 216, 118.8),
        ("hPercent 100",          100, 216, 118.8),
        ("hPercent 200",          200, 216, 118.8),
        ("hPercent 400",          400, 216, 118.8),
        ("taller, no hPercent",  null, 216, 172.8),
        ("taller, hPercent 50",    50, 216, 172.8),
        ("taller, hPercent 200",  200, 216, 172.8)
    ];

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 60 && p.R > p.G + 40 && p.R > p.B + 40;

    /// <summary>Word's own output for the probe. No font check: nothing here converts anything.</summary>
    private static byte[] Reference()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    private (double W, double H, double X, double Y)? Box(byte[] pdf, int page)
    {
        if (PdfRasterizer.Render(pdf, page, Scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var shape = BoxSilhouette.Find(rendered, Scale, Reddish, (73, 73, 432, 288));

        Assert.True(shape.Found, $"{Pages[page].What}: {shape.Refused}");

        var p = shape.Points;

        return (p.Max(c => c.X) - p.Min(c => c.X), p.Max(c => c.Y) - p.Min(c => c.Y),
                p.Min(c => c.X), p.Min(c => c.Y));
    }

    /// <summary>
    /// <c>c:hPercent</c> has one default, where <c>c:rAngAx</c> has two.
    /// </summary>
    /// <remarks>
    /// #96 found that an absent <c>c:view3D</c> and a present one with an absent child do not give
    /// the same scene — <c>rAngAx</c> comes out false in the first case and true in the second. That
    /// makes it worth asking of every child rather than assuming.
    ///
    /// These two pages differ in exactly one thing: the first carries no <c>c:view3D</c>, and the
    /// second states every value #96 measured as the absent-element defaults but no
    /// <c>c:hPercent</c>. They draw the same picture, so this child is not another <c>rAngAx</c>.
    /// </remarks>
    [Fact]
    public void The_element_has_one_default_and_not_two()
    {
        if (Box(Reference(), 0) is not { } absent || Box(Reference(), 1) is not { } stated) return;

        _output.WriteLine($"no view3D at all: {absent.W:0.0000}x{absent.H:0.0000} at ({absent.X:0.0000},{absent.Y:0.0000})");
        _output.WriteLine($"stated, none:     {stated.W:0.0000}x{stated.H:0.0000} at ({stated.X:0.0000},{stated.Y:0.0000})");

        Assert.InRange(stated.W - absent.W, -0.05, 0.05);
        Assert.InRange(stated.H - absent.H, -0.05, 0.05);
        Assert.InRange(stated.X - absent.X, -0.05, 0.05);
        Assert.InRange(stated.Y - absent.Y, -0.05, 0.05);
    }

    /// <summary>
    /// Its absence is not equivalent to any value of it, and in particular not to 100.
    /// </summary>
    /// <remarks>
    /// The one that matters, because 100 is the obvious guess and it is a third out. Absent, the box
    /// on the baseline comes back 172pt wide; at <c>hPercent</c> 100 it is 114pt. A reader
    /// substituting the schema's default for the absent element would draw that.
    /// </remarks>
    [Fact]
    public void Saying_nothing_is_not_the_same_as_saying_a_hundred()
    {
        if (Box(Reference(), 0) is not { } absent || Box(Reference(), 4) is not { } hundred) return;

        _output.WriteLine($"absent gives a box {absent.W:0.00}pt wide; hPercent 100 gives {hundred.W:0.00}pt");

        Assert.True(Math.Abs(absent.W - hundred.W) > 40,
            $"absent and hPercent 100 differ by only {Math.Abs(absent.W - hundred.W):0.00}pt");
    }

    /// <summary>
    /// It replaces what the plot rectangle would have said, rather than scaling it.
    /// </summary>
    /// <remarks>
    /// The two readings that one rectangle could not tell apart. If <c>hPercent</c> multiplied the
    /// rectangle's aspect, the same value in a rectangle half again as tall would give a box of a
    /// different shape; if it replaces it, the shape is the same and only the scale differs.
    ///
    /// Measured: the box's height over its width is 1.176 at <c>hPercent</c> 200 in the baseline
    /// rectangle and 1.173 in one 172.8 tall rather than 118.8. Against that, the two pages stating
    /// nothing come out at 0.525 and 0.638 — which is the rectangle showing through, and is the
    /// whole difference between the two rules.
    /// </remarks>
    [Fact]
    public void It_replaces_what_the_rectangle_would_have_said_rather_than_scaling_it()
    {
        var pdf = Reference();

        if (Box(pdf, 5) is not { } two || Box(pdf, 9) is not { } tallerTwo) return;
        if (Box(pdf, 0) is not { } none || Box(pdf, 7) is not { } tallerNone) return;

        var (a, b) = (two.H / two.W, tallerTwo.H / tallerTwo.W);
        var (c, d) = (none.H / none.W, tallerNone.H / tallerNone.W);

        _output.WriteLine($"hPercent 200: shape {a:0.000} in the baseline rectangle, {b:0.000} in the taller one");
        _output.WriteLine($"stating none: shape {c:0.000} and {d:0.000} — the rectangle showing through");

        // Stated, the shape does not follow the rectangle.
        Assert.InRange(a - b, -0.02, 0.02);

        // Absent, it does — by more than the tolerance above, so the two are genuinely distinguished.
        Assert.True(Math.Abs(c - d) > 0.05,
            $"the two pages stating nothing differ in shape by only {Math.Abs(c - d):0.000}, so this proves nothing");
    }

    /// <summary>
    /// The scene is fitted to whichever of the rectangle's sides binds first, and
    /// <c>c:hPercent</c> is what makes the width bind.
    /// </summary>
    /// <remarks>
    /// A correction to #108, which found fitting by height and fitting by the smaller of the two to
    /// be identical on every page it had. They were — because with the box as tall relative to its
    /// width as the rectangle, the height always binds. State <c>hPercent</c> low enough and the box
    /// becomes short and wide, and the width binds instead.
    ///
    /// It is not a small effect. Fitting by height alone puts the two pages below out by 52 and 60
    /// points; taking the smaller of the two puts them within 8, which is the projection's own error
    /// at this stage rather than the rule's.
    /// </remarks>
    [Theory]
    [InlineData(2, "hPercent 25")]
    [InlineData(8, "taller, hPercent 50")]
    public void A_short_box_is_fitted_to_the_rectangles_width(int page, string what)
    {
        if (Box(Reference(), page) is not { } box) return;

        var p = Pages[page];
        var across = box.W / p.Width;

        _output.WriteLine($"{what}: box {box.W:0.00}pt across a rectangle {p.Width:0.0}pt wide — {across:0.000} of it");

        // Nearly filling it, where every page of #108's probe sat between 0.60 and 0.92.
        Assert.True(across > 0.95,
            $"{what} fills only {across:0.000} of the rectangle's width, so the width may not be binding");
    }

    /// <summary>
    /// Stating it larger makes the box taller and narrower, monotonically.
    /// </summary>
    /// <remarks>
    /// The direction, which is what says the element means what it is named. A taller box scaled to
    /// the same rectangle is a narrower one on the page, so the widths fall throughout — 208, 182,
    /// 114, 66 and 36 points at 25, 50, 100, 200 and 400.
    /// </remarks>
    [Fact]
    public void Stating_it_larger_makes_the_box_narrower()
    {
        var pdf = Reference();
        var widths = new List<double>();

        foreach (var page in new[] { 2, 3, 4, 5, 6 })
        {
            if (Box(pdf, page) is not { } box) return;

            widths.Add(box.W);
            _output.WriteLine($"  {Pages[page].What,-14} {box.W,7:0.00}pt wide");
        }

        for (var i = 1; i < widths.Count; i++)
            Assert.True(widths[i] < widths[i - 1] - 5,
                $"{Pages[i + 2].What} is not meaningfully narrower than {Pages[i + 1].What}");
    }
}
