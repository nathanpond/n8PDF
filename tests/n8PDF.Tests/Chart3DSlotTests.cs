using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where a bar sits when there is more than one of them.
/// </summary>
/// <remarks>
/// The other half of #114. <see cref="Chart3DFootprintTests"/> settled a single bar —
/// <c>slot / (1 + gap/100)</c>, centred — and this settles that **the rule needed no amendment at
/// all**. Three earlier runs measured it failing by 18 to 38 points at two and three counts and
/// looked for a different rule; the rule was right and the **box** was wrong.
///
/// #116 is why. The box is <b>n units wide for n categories</b> and n deep for n series, so a slot is
/// a constant unit and it was never the slot that moved. Measured against a box assumed one unit wide
/// however many bars stood in it, a bar's place comes out wrong by a factor of the count, which is
/// exactly what 18 to 38 points looks like. <see cref="Assuming_the_box_does_not_grow_fails_by_the_count"/>
/// reproduces that failure deliberately.
///
/// Everything here is read on the **right-angled-axes arm**: with no perspective the projection is
/// affine, so a length ratio on the page is a length ratio in the scene and no projection has to be
/// solved for — which is what #98 has never been able to supply.
///
/// Each gapped page is paired with a page of the same counts at gap nought, where the bars abut and
/// their union **is** the box. That gives box and bar under one fit, on one scene.
///
/// The measurement is taken off the **top face**. Looking down on the scene it is never occluded,
/// where a bar behind another has its base cut away by the one in front — which is a real limit and
/// not a nuisance: at <c>rotX</c> 15 the second and third series cannot be read at all, and the steep
/// pages exist for that reason.
/// </remarks>
public class Chart3DSlotTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-slot-probe";

    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    /// <summary>The top face's two edges at its far corner: one across, one in depth.</summary>
    /// <remarks>
    /// With right-angled axes the width axis projects exactly horizontal, so the across edge is the
    /// neighbour level with the corner and the depth edge is the other. Nothing else about the
    /// orientation has to be known.
    /// </remarks>
    private (((double X, double Y) A, (double X, double Y) B) Across,
             ((double X, double Y) A, (double X, double Y) B) Depth)? Edges(byte[] pdf, int page)
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

        var corner = 0;

        for (var i = 1; i < points.Count; i++)
            if (points[i].Y < points[corner].Y ||
                (Math.Abs(points[i].Y - points[corner].Y) < 0.5 && points[i].X < points[corner].X))
                corner = i;

        var one = points[(corner - 1 + points.Count) % points.Count];
        var other = points[(corner + 1) % points.Count];

        var oneLevel = Math.Abs(one.Y - points[corner].Y) < 0.5;
        var otherLevel = Math.Abs(other.Y - points[corner].Y) < 0.5;

        // A bar too foreshortened to have a top face worth reading — the shallow pages' rear series.
        if (oneLevel == otherLevel)
        {
            _output.WriteLine($"page {page}: the top face has no level edge, so it cannot be read");
            return null;
        }

        return oneLevel
            ? ((points[corner], one), (points[corner], other))
            : ((points[corner], other), (points[corner], one));
    }

    /// <summary>Where a bar's edge lies along the box's, as a fraction from nought to one.</summary>
    private static (double From, double To)? Along(
        (((double X, double Y) A, (double X, double Y) B) Across,
         ((double X, double Y) A, (double X, double Y) B) Depth)? box,
        (((double X, double Y) A, (double X, double Y) B) Across,
         ((double X, double Y) A, (double X, double Y) B) Depth)? bar,
        bool inDepth)
    {
        if (box is not { } whole || bar is not { } mine) return null;

        var reference = inDepth ? whole.Depth : whole.Across;
        var measured = inDepth ? mine.Depth : mine.Across;

        var ux = reference.B.X - reference.A.X;
        var uy = reference.B.Y - reference.A.Y;
        var square = ux * ux + uy * uy;

        double At((double X, double Y) p) =>
            ((p.X - reference.A.X) * ux + (p.Y - reference.A.Y) * uy) / square;

        var a = At(measured.A);
        var b = At(measured.B);

        // The depth axis is drawn towards the reader, where the corner these are measured from is
        // the far one — so counting in depth runs the other way. Series nought is nearest the
        // reader, which is why it is the one that is never hidden.
        return inDepth
            ? (1 - Math.Max(a, b), 1 - Math.Min(a, b))
            : (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>Where the rule says bar <paramref name="index"/> of <paramref name="count"/> sits.</summary>
    private static (double From, double To) Rule(int count, int gap, int index)
    {
        var fills = 1 / (1 + gap / 100.0) / count;

        return (index / (double)count + (1.0 / count - fills) / 2, 0) is var (from, _)
            ? (from, from + fills)
            : default;
    }

    private byte[]? Reference()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// Each bar keeps to its own slot across, at every count and every gap.
    /// </summary>
    /// <remarks>
    /// One measurement answers three questions at once — which slot the bar is in, how much of it it
    /// fills, and whether it is centred in it — because a bar in the wrong slot, of the wrong width,
    /// or pushed to one side each miss differently.
    ///
    /// A bar other than the first is measured at every count, since an off-by-one in the slot index
    /// is invisible at index nought.
    ///
    /// | | measured | the rule |
    /// |---|---|---|
    /// | 3 categories, `gapWidth` 300, bar 2 | 0.7918..0.8751 | 0.7917..0.8750 |
    /// | 4 categories, `gapWidth` 300, bar 1 | 0.3439..0.4061 | 0.3438..0.4063 |
    /// | **held back**: 3 at `gapWidth` 50, bar 1 | 0.3889..0.6114 | 0.3889..0.6111 |
    ///
    /// Everything inside 0.0012 of the box's width.
    /// </remarks>
    [Theory]
    [InlineData(1, 0, 2, 150, 0, "two categories, gapWidth 150, first bar")]
    [InlineData(2, 0, 2, 300, 1, "two categories, gapWidth 300, second bar")]
    [InlineData(4, 3, 3, 150, 0, "three categories, gapWidth 150, first bar")]
    [InlineData(5, 3, 3, 150, 1, "three categories, gapWidth 150, middle bar")]
    [InlineData(6, 3, 3, 300, 2, "three categories, gapWidth 300, last bar")]
    [InlineData(8, 7, 4, 150, 2, "four categories, gapWidth 150, third bar")]
    [InlineData(9, 7, 4, 300, 1, "four categories, gapWidth 300, second bar")]
    [InlineData(15, 3, 3, 50, 1, "held back: three categories, gapWidth 50, middle bar")]
    public void A_bar_keeps_to_its_slot_across(int page, int boxPage, int count, int gap, int index, string what)
    {
        if (Reference() is not { } pdf) return;

        if (Along(Edges(pdf, boxPage), Edges(pdf, page), inDepth: false) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var (from, to) = Rule(count, gap, index);

        _output.WriteLine($"{what}: {measured.From:0.0000}..{measured.To:0.0000}, " +
                          $"the rule says {from:0.0000}..{to:0.0000}");

        Assert.InRange(measured.From - from, -0.008, 0.008);
        Assert.InRange(measured.To - to, -0.008, 0.008);
    }

    /// <summary>
    /// And in depth, where only a steep enough view lets the far ones be seen at all.
    /// </summary>
    /// <remarks>
    /// The same rule with <c>gapDepth</c>, and the same arithmetic. What differs is that a bar behind
    /// another is partly hidden by it: at <c>rotX</c> 15 only the nearest series can be measured, and
    /// the pages at <c>rotX</c> 55 exist so that the second and third can be. That is a fact about
    /// what is visible rather than about the rule, and the rule comes out the same on both.
    /// </remarks>
    [Theory]
    [InlineData(11, 10, 2, 150, 0, "two series, gapDepth 150, nearest")]
    [InlineData(12, 10, 2, 150, 1, "two series, gapDepth 150, furthest")]
    [InlineData(18, 17, 3, 150, 0, "steep: three series, gapDepth 150, nearest")]
    [InlineData(19, 17, 3, 150, 1, "steep: three series, gapDepth 150, middle")]
    [InlineData(20, 17, 3, 150, 2, "steep: three series, gapDepth 150, furthest")]
    [InlineData(21, 17, 3, 300, 1, "held back, steep: three series, gapDepth 300, middle")]
    public void A_bar_keeps_to_its_slot_in_depth(int page, int boxPage, int count, int gap, int index, string what)
    {
        if (Reference() is not { } pdf) return;

        if (Along(Edges(pdf, boxPage), Edges(pdf, page), inDepth: true) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var (from, to) = Rule(count, gap, index);

        _output.WriteLine($"{what}: {measured.From:0.0000}..{measured.To:0.0000}, " +
                          $"the rule says {from:0.0000}..{to:0.0000}");

        Assert.InRange(measured.From - from, -0.008, 0.008);
        Assert.InRange(measured.To - to, -0.008, 0.008);
    }

    /// <summary>
    /// A bar behind another is cut by it, and the nearest series is the one that never is.
    /// </summary>
    /// <remarks>
    /// Worth pinning for two reasons. It says which way round the series are drawn — the one that is
    /// never hidden is series nought, so **series are drawn front to back**, which #101 will need. And
    /// it records why the steep pages are in the probe, so that nobody removes them as duplicates.
    ///
    /// At <c>rotX</c> 15 the second of two series still has a readable top face; the second of
    /// **three** does not, its top being cut to a sliver by the bar in front. Both are readable at
    /// <c>rotX</c> 55.
    /// </remarks>
    [Fact]
    public void A_series_behind_another_is_cut_by_it()
    {
        if (Reference() is not { } pdf) return;

        if (Edges(pdf, 10) is null)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        // Nearest of three, at the shallow view: readable.
        Assert.NotNull(Edges(pdf, 13));

        // Middle of three, same view: not.
        Assert.Null(Edges(pdf, 14));

        // And at the steep view, all three are.
        Assert.NotNull(Edges(pdf, 18));
        Assert.NotNull(Edges(pdf, 19));
        Assert.NotNull(Edges(pdf, 20));

        _output.WriteLine("the middle of three series is unreadable at rotX 15 and readable at rotX 55");
    }

    /// <summary>
    /// The failure three earlier runs chased: a box assumed not to grow with the count.
    /// </summary>
    /// <remarks>
    /// The injection, and it is the historical mistake rather than an invented one. Before #116 the
    /// box was taken to be one unit wide however many bars stood in it, so a bar's slot was reckoned
    /// as <c>1/n</c> of a box that is really <c>n</c> units wide — out by a factor of the count.
    ///
    /// Reproduced here by predicting a bar's place from the box's **whole** width rather than its
    /// slot: at four categories the third bar is predicted at 0.575..0.675 and lands there, while the
    /// wrong reckoning puts it at 0.300..0.700 — out by more than a quarter of the box, which on this
    /// probe is some sixty points. That is the kind of error those runs were measuring, and it is why
    /// the rule looked wrong when it was the box that was.
    /// </remarks>
    [Fact]
    public void Assuming_the_box_does_not_grow_fails_by_the_count()
    {
        if (Reference() is not { } pdf) return;

        if (Along(Edges(pdf, 7), Edges(pdf, 8), inDepth: false) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var (from, to) = Rule(4, 150, 2);

        // What the rule says, and what Word draws.
        Assert.InRange(measured.From - from, -0.008, 0.008);

        // What it looks like if the box is taken not to grow: the bar fills 1/(1+g/100) of the whole
        // box rather than of its slot, centred in the whole box.
        var fills = 1 / (1 + 150 / 100.0);
        var wrongFrom = (1 - fills) / 2;

        _output.WriteLine($"four categories, third bar: Word draws {measured.From:0.0000}, " +
                          $"the rule says {from:0.0000}, a box that does not grow says {wrongFrom:0.0000}");

        Assert.True(Math.Abs(measured.From - wrongFrom) > 0.2,
            "the pre-#116 reckoning now agrees with Word, so this test no longer records the failure " +
            "it was written for");
    }
}
