using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests how much of the plot rectangle a three-dimensional scene actually takes.
/// </summary>
/// <remarks>
/// Assertions about Word's own output, as <see cref="Chart3DGeometryTests"/> explains.
///
/// This settles a number that had been seen twice and dismissed twice. #98 measured its model's foot
/// at the rectangle's bottom where Word's was 2.19pt higher, and put it down to noise. #116's fitted
/// widths ran five to seven points wide at every category count and it read that as the model being
/// wrong. Both were the same thing: **the scene does not fill the plot rectangle.**
///
/// The bar reaches the axis maximum here, which everywhere else in the suite is avoided because such
/// a bar is cut by the plot area. That is exactly the point — it is *not* cut, and the gap it leaves
/// is what is being measured.
/// </remarks>
public class Chart3DInsetTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const double Scale = 8;

    /// <summary>Each page's stated height percentage and the plot rectangle it draws in.</summary>
    public static readonly (string What, int HPercent, double Left, double Top, double Width, double Height)[] Pages =
    [
        ("hPercent 25, 216 wide",   25, 144,   93.6, 216, 118.8),
        ("hPercent 25, 144 wide",   25, 180,   93.6, 144, 118.8),
        ("hPercent 25, 288 wide",   25, 108,   93.6, 288, 118.8),
        ("hPercent 25, 108 wide",   25, 198,   93.6, 108, 118.8),
        ("hPercent 25, 64.8 tall",  25, 144,  115.2, 216,  64.8),
        ("hPercent 25, 172.8 tall", 25, 144,   82.8, 216, 172.8),
        ("nothing, 216 wide",        0, 144,   93.6, 216, 118.8),
        ("nothing, 144 wide",        0, 180,   93.6, 144, 118.8),
        ("nothing, 288 wide",        0, 108,   93.6, 288, 118.8),
        ("hPercent 400, 216 wide", 400, 144,   93.6, 216, 118.8)
    ];

    /// <summary>The box Word drew, as its bounds on the page.</summary>
    private (double W, double H, double X, double Y)? Box(int page)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-inset-probe.pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, Scale) is not { } r)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var (minX, maxX, minY, maxY) = (999.0, -999.0, 999.0, -999.0);

        for (var y = 74.0; y < 290; y += 1.0 / Scale)
        for (var x = 74.0; x < 430; x += 1.0 / Scale)
        {
            var q = r.At(x, y, Scale);

            if (q.R <= 60 || q.R <= q.G + 40 || q.R <= q.B + 40) continue;

            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }

        Assert.True(maxX > minX, $"{Pages[page].What}: nothing of the series' colour was drawn");

        return (maxX - minX, maxY - minY, minX, minY);
    }

    /// <summary>
    /// The scene takes about 0.970 of the rectangle on whichever side binds.
    /// </summary>
    /// <remarks>
    /// Measured over nine pages spanning rectangle widths from 108 to 288 and heights from 64.8 to
    /// 172.8, and over both binding sides — which `c:hPercent` is what moves, a low value making the
    /// box short and wide so the width binds, and its absence or a high value leaving the height to.
    ///
    /// Every one of them lands in **[0.9664, 0.9733]**, mean 0.9702. An inset measured on one side
    /// only would say nothing about the other, which is why both are here.
    /// </remarks>
    [Theory]
    [InlineData(0, "width",  "hPercent 25, 216 wide")]
    [InlineData(1, "width",  "hPercent 25, 144 wide")]
    [InlineData(3, "width",  "hPercent 25, 108 wide")]
    [InlineData(2, "height", "hPercent 25, 288 wide")]
    [InlineData(4, "height", "hPercent 25, 64.8 tall")]
    [InlineData(6, "height", "nothing, 216 wide")]
    [InlineData(7, "height", "nothing, 144 wide")]
    [InlineData(8, "height", "nothing, 288 wide")]
    [InlineData(9, "height", "hPercent 400, 216 wide")]
    public void The_scene_takes_about_a_thirty_third_less_than_the_rectangle(int page, string binds, string what)
    {
        if (Box(page) is not { } box) return;

        var p = Pages[page];
        var of = binds == "width" ? box.W / p.Width : box.H / p.Height;

        _output.WriteLine($"{what}: {binds} {(binds == "width" ? box.W : box.H):0.00} " +
                          $"of {(binds == "width" ? p.Width : p.Height):0.0} = {of:0.0000}");

        Assert.InRange(of, 0.960, 0.980);
    }

    /// <summary>
    /// The inset is a share of the rectangle, not a fixed number of points.
    /// </summary>
    /// <remarks>
    /// The distinction that matters for implementing it, and the width sweep is what settles it.
    /// Three rectangles 108, 144 and 216 points wide, all with the width binding, leave 2.88, 4.00
    /// and 6.13 points over. A fixed inset would leave the same on all three; a share leaves it in
    /// proportion, and these are within a twentieth of that.
    /// </remarks>
    [Fact]
    public void The_inset_is_a_share_of_the_rectangle_and_not_a_fixed_measure()
    {
        var left = new List<(double Width, double Over)>();

        foreach (var page in new[] { 3, 1, 0 })
        {
            if (Box(page) is not { } box) return;

            left.Add((Pages[page].Width, Pages[page].Width - box.W));
        }

        foreach (var (width, over) in left)
            _output.WriteLine($"a rectangle {width:0.0} wide leaves {over:0.00}pt over — {over / width:0.0000} of it");

        // In proportion: the widest rectangle leaves about twice what the narrowest does, its being
        // twice as wide.
        var shares = left.Select(e => e.Over / e.Width).ToList();

        Assert.InRange(shares.Max() - shares.Min(), 0, 0.005);

        // And not a fixed measure, which would leave the same on all three.
        Assert.True(left.Max(e => e.Over) - left.Min(e => e.Over) > 2,
            "the leftover barely changes with the rectangle, so the inset may be a fixed measure after all");
    }

    /// <summary>
    /// Which side binds moves with <c>c:hPercent</c>, and the inset holds on both.
    /// </summary>
    /// <remarks>
    /// Two pages with the same rectangle: one stating a low <c>hPercent</c>, where the box goes short
    /// and wide and the width binds, and one stating nothing, where the height does. The first fills
    /// its rectangle's width and not its height; the second the reverse. Without both, an inset
    /// measured on one side would be a guess about the other.
    /// </remarks>
    [Fact]
    public void Which_side_binds_moves_with_the_stated_height()
    {
        if (Box(0) is not { } wide || Box(6) is not { } tall) return;

        var p = Pages[0];

        _output.WriteLine($"hPercent 25:  {wide.W / p.Width:0.000} of the width, {wide.H / p.Height:0.000} of the height");
        _output.WriteLine($"nothing:      {tall.W / p.Width:0.000} of the width, {tall.H / p.Height:0.000} of the height");

        // The short wide box fills its width and falls well short of its height.
        Assert.InRange(wide.W / p.Width, 0.960, 0.980);
        Assert.True(wide.H / p.Height < 0.90);

        // And the other way about.
        Assert.InRange(tall.H / p.Height, 0.960, 0.980);
        Assert.True(tall.W / p.Width < 0.90);
    }
}
