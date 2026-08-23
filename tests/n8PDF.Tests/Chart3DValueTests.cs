using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests how a value becomes a height inside a three-dimensional box.
/// </summary>
/// <remarks>
/// Assertions about Word's own output, for the reason <see cref="Chart3DGeometryTests"/> gives.
///
/// This was the last untested assumption in the chain: every probe before it drew 60 of a maximum of
/// 100 and nothing else, so "a bar reaches value over range of the box" had only ever been
/// consistent with one number. It turns out to be exactly right — which is worth having written
/// down, because #98's residual was blamed on it for a while and the blame was misplaced.
/// </remarks>
public class Chart3DValueTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const double Scale = 8;

    /// <summary>Each page's value and axis bounds.</summary>
    public static readonly (string What, double Value, double Min, double Max)[] Pages =
    [
        ("20 of 100",     20,    0, 100),
        ("40 of 100",     40,    0, 100),
        ("60 of 100",     60,    0, 100),
        ("80 of 100",     80,    0, 100),
        ("95 of 100",     95,    0, 100),
        ("60 of 200",     60,    0, 200),
        ("60 of 300",     60,    0, 300),
        ("120 of 200",   120,    0, 200),
        ("60, min -100",  60, -100, 100),
        ("-60, min -100",-60, -100, 100)
    ];

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 60 && p.R > p.G + 40 && p.R > p.B + 40;

    private static byte[] Reference()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-value-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    private (double W, double H, double Top, double Foot)? Box(byte[] pdf, int page)
    {
        if (PdfRasterizer.Render(pdf, page, Scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var shape = BoxSilhouette.Find(rendered, Scale, Reddish, (73, 73, 431, 287));

        Assert.True(shape.Found, $"{Pages[page].What}: {shape.Refused}");

        var p = shape.Points;

        return (p.Max(c => c.X) - p.Min(c => c.X), p.Max(c => c.Y) - p.Min(c => c.Y),
                p.Min(c => c.Y), p.Max(c => c.Y));
    }

    /// <summary>
    /// Only the fraction counts: the numbers themselves do not reach the page.
    /// </summary>
    /// <remarks>
    /// The control this probe exists for. 120 of a maximum of 200 and 60 of a maximum of 100 are the
    /// same fraction and entirely different numbers, and Word draws them **identically**. So is 60 of
    /// 300 against 20 of 100. Nothing absolute leaks in — not the value, not the maximum.
    /// </remarks>
    [Theory]
    [InlineData(7, 2, "120 of 200 against 60 of 100")]
    [InlineData(6, 0, "60 of 300 against 20 of 100")]
    public void Only_the_fraction_reaches_the_page(int left, int right, string what)
    {
        if (Box(Reference(), left) is not { } a || Box(Reference(), right) is not { } b) return;

        _output.WriteLine($"{what}: {a.W:0.00}x{a.H:0.00} top {a.Top:0.00} against {b.W:0.00}x{b.H:0.00} top {b.Top:0.00}");

        Assert.InRange(a.W - b.W, -0.05, 0.05);
        Assert.InRange(a.H - b.H, -0.05, 0.05);
        Assert.InRange(a.Top - b.Top, -0.05, 0.05);
    }

    /// <summary>
    /// The box runs from the axis's minimum to its maximum, and a bar runs from **nought**.
    /// </summary>
    /// <remarks>
    /// Two rules that a scale starting at nought cannot tell apart, and one page settles both.
    ///
    /// With the scale running from −100 to 100, a value of 60 sits eight tenths of the way up it. Its
    /// bar's top lands at exactly the page position that 80 of 100 does — so the box spans the
    /// **axis**, minimum to maximum, and not the data.
    ///
    /// Its foot, meanwhile, does not reach the bottom of the box: it stops half way up, where nought
    /// is. So a bar hangs from nought and not from the axis's minimum — which on every other page in
    /// this suite are the same place, and here are not.
    /// </remarks>
    [Fact]
    public void The_box_spans_the_axis_and_a_bar_hangs_from_nought()
    {
        var pdf = Reference();

        if (Box(pdf, 8) is not { } shifted || Box(pdf, 3) is not { } eightTenths) return;
        if (Box(pdf, 2) is not { } plain) return;

        _output.WriteLine($"60 on a scale from -100: top {shifted.Top:0.00}, foot {shifted.Foot:0.00}");
        _output.WriteLine($"80 on a scale from 0:    top {eightTenths.Top:0.00}, foot {eightTenths.Foot:0.00}");

        // The same fraction up the axis puts the top in the same place.
        Assert.InRange(shifted.Top - eightTenths.Top, -0.05, 0.05);

        // But the foot is nowhere near the bottom of the box, where every other page's is.
        Assert.True(shifted.Foot < plain.Foot - 30,
            $"the foot is at {shifted.Foot:0.00} against {plain.Foot:0.00}, so the bar may be hanging from the minimum");
    }

    /// <summary>
    /// Every bar on a scale starting at nought stands on the same floor.
    /// </summary>
    [Fact]
    public void Bars_on_a_scale_from_nought_share_a_floor()
    {
        var pdf = Reference();
        var feet = new List<double>();

        foreach (var page in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        {
            if (Box(pdf, page) is not { } box) return;

            feet.Add(box.Foot);
        }

        _output.WriteLine($"feet: {string.Join(", ", feet.Select(f => f.ToString("0.00")))}");

        Assert.InRange(feet.Max() - feet.Min(), 0, 0.05);
    }

    /// <summary>
    /// A bar's height on the page is **not** proportional to its value.
    /// </summary>
    /// <remarks>
    /// Said explicitly because it is the sort of thing that gets assumed. Equal steps in value give
    /// growing steps on the page — 12.03, 12.21 and 12.39 points for each fifth of the axis — which
    /// is the perspective, the top of a taller bar being further from the eye than a shorter one's.
    ///
    /// A reader treating height as proportional would be out by a couple of points at the extremes
    /// and exactly right in the middle, which is the worst kind of wrong to notice.
    /// </remarks>
    [Fact]
    public void Height_is_not_proportional_to_value()
    {
        var pdf = Reference();
        var heights = new List<double>();

        foreach (var page in new[] { 0, 1, 2, 3 })
        {
            if (Box(pdf, page) is not { } box) return;

            heights.Add(box.H);
        }

        var steps = Enumerable.Range(1, heights.Count - 1).Select(i => heights[i] - heights[i - 1]).ToList();

        _output.WriteLine($"heights: {string.Join(", ", heights.Select(h => h.ToString("0.00")))}");
        _output.WriteLine($"steps:   {string.Join(", ", steps.Select(s => s.ToString("0.00")))}");

        // Growing, and by enough to see.
        for (var i = 1; i < steps.Count; i++)
            Assert.True(steps[i] > steps[i - 1], "the steps are not growing, so the rise may be proportional after all");

        Assert.True(steps[^1] - steps[0] > 0.25,
            $"the steps grow by only {steps[^1] - steps[0]:0.00}pt across the sweep, which is within a pixel");
    }

    /// <summary>
    /// Word draws **nothing at all** for a bar below nought.
    /// </summary>
    /// <remarks>
    /// Not a measurement so much as a warning, and a surprising one. The scale runs from −100 to 100
    /// and the value is −60, so the bar has half the box to hang down into and is not off the scale.
    /// Word puts no ink of the series' colour anywhere on the page — not clipped, not hidden behind
    /// the walls at this rotation, simply absent, where the same chart with the value at +60 draws
    /// half a million red samples.
    ///
    /// Whether that is Word declining to draw it or drawing it somewhere invisible is not settled
    /// here. What is settled is that a reader extruding it downwards would put ink where Word puts
    /// none. Recorded on #101, which is where negative bars are built.
    /// </remarks>
    [Fact]
    public void A_bar_below_nought_is_not_drawn()
    {
        if (PdfRasterizer.Render(Reference(), 9, Scale) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var found = 0;

        for (var y = 72.0; y < 300; y += 0.5)
        for (var x = 72.0; x < 440; x += 0.5)
            if (Reddish(rendered.At(x, y, Scale)))
                found++;

        _output.WriteLine($"a bar of -60 on a scale from -100: {found} samples of the series' colour");

        Assert.Equal(0, found);

        // And the same chart at +60 is covered in it, so the test is looking for the right colour.
        if (PdfRasterizer.Render(Reference(), 8, Scale) is not { } positive) return;

        var there = 0;

        for (var y = 72.0; y < 300; y += 0.5)
        for (var x = 72.0; x < 440; x += 0.5)
            if (Reddish(positive.At(x, y, Scale)))
                there++;

        Assert.True(there > 1000, $"the +60 page has only {there} samples, so the colour test may be wrong");
    }
}
