using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// How much of its slot a three-dimensional bar fills, across and in depth.
/// </summary>
/// <remarks>
/// The rule is <c>slot / (1 + gap/100)</c>, centred in the slot, for **both** gaps — <c>gapWidth</c>
/// across and <c>gapDepth</c> in depth — with the slot being one over the count. At one category and
/// one series the slot is the whole box, which is what this probe holds.
///
/// It had been measured three times before this and committed none of them, so it lived only in
/// #114's comments with no fixture and no test. What follows is the same rule, verified a way that
/// does not need the thing it was previously verified against.
///
/// **There is no projection model here, and that is the point.** Building one is #98's subject and it
/// is not finished, so the earlier runs checked the rule by predicting a silhouette from scratch code
/// that was never committed. Instead this reads the answer out of the picture projectively.
///
/// With <c>gapDepth</c> nought a bar spans the full depth, so its front-bottom edge is a **centred
/// sub-segment of the box's own front-bottom edge** — four points on one line of the scene. A cross
/// ratio is unchanged by any projection, and for a centred segment of fraction <c>f</c> it comes to
/// exactly <c>((1-f)/(1+f))²</c>, so
///
/// <code>
/// f = (1 - √CR) / (1 + √CR)
/// </code>
///
/// recovers the fraction from the page with nothing assumed about how the scene was projected onto
/// it. This is a sound use of a cross ratio where <see cref="Chart3DSizeTests"/>'s was not: there the
/// configuration was fixed so the invariant was a tautology, here it **moves with the gap**, which is
/// what makes it a measurement.
///
/// What it gives, against what the rule says:
///
/// | stated | recovered `f` | rule |
/// |---|---|---|
/// | `gapWidth` 50 | 0.6668 | 0.6667 |
/// | `gapWidth` 150 | 0.4004 | 0.400 |
/// | `gapWidth` 300 | 0.2500 | 0.250 |
/// | `gapDepth` 50 | 0.6695 | 0.6667 |
/// | `gapDepth` 150 | 0.3996 | 0.400 |
/// | `gapDepth` 300 | 0.2633 | 0.250 |
///
/// The last is the loosest and for a visible reason: at <c>gapDepth</c> 300 the depth edge is 12.5
/// points long, so a corner placed to a twentieth of a point is a third of a per cent of it and the
/// recovery is levered accordingly. Everything wider than that lands within half a per cent.
///
/// **The multi-bar case is not here and is not this test's business.** It is blocked on #116: with
/// more than one category or series the box's own proportions move, so a footprint measured against
/// it would be measuring two things at once.
/// </remarks>
public class Chart3DFootprintTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-footprint-probe";

    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    /// <summary>
    /// The two bottom edges of a bar's silhouette, told apart by which way they run.
    /// </summary>
    /// <remarks>
    /// The lowest corner of the outline is the one nearest the reader, and the two edges meeting
    /// there are the across one and the depth one. Every page of this probe is drawn at the same
    /// <c>rotY</c>, so they always run the same ways: the across edge goes left from that corner and
    /// the depth edge goes right. That is a property of the scene rather than a fitted guess, and it
    /// is checked by the sweeps themselves — only the across edge shortens when <c>gapWidth</c> moves
    /// and only the depth edge when <c>gapDepth</c> does.
    /// </remarks>
    private static (((double X, double Y) A, (double X, double Y) B) Across,
                    ((double X, double Y) A, (double X, double Y) B) Depth)? Edges(
        byte[] pdf, int page, double scale)
    {
        if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered) return null;

        var shape = BoxSilhouette.Find(rendered, scale, Reddish, (73, 73, 431, 287));

        if (!shape.Found) return null;

        var points = shape.Points;

        var low = 0;
        for (var i = 1; i < points.Count; i++)
            if (points[i].Y > points[low].Y) low = i;

        var one = points[(low - 1 + points.Count) % points.Count];
        var other = points[(low + 1) % points.Count];

        var (left, right) = one.X < other.X ? (one, other) : (other, one);

        return ((points[low], left), (points[low], right));
    }

    /// <summary>
    /// The fraction of a segment that a centred sub-segment covers, from the four points alone.
    /// </summary>
    /// <remarks>
    /// The four are collinear in the scene and therefore collinear on the page, so any affine
    /// coordinate along the line serves — the cross ratio does not care which. The x of each point is
    /// used, the line never being vertical in this probe.
    /// </remarks>
    private static double Fraction(
        ((double X, double Y) A, (double X, double Y) B) whole,
        ((double X, double Y) A, (double X, double Y) B) part)
    {
        double a = whole.A.X, b = whole.B.X, p = part.A.X, q = part.B.X;

        var cross = (p - a) / (p - b) / ((q - a) / (q - b));
        var root = Math.Sqrt(cross);

        return (1 - root) / (1 + root);
    }

    private byte[]? Reference()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// A bar fills <c>1 / (1 + gap/100)</c> of its slot, for either gap.
    /// </summary>
    /// <remarks>
    /// The first six rows are the two sweeps, each read against the baseline page where both gaps are
    /// nought and the bar is therefore the whole box.
    ///
    /// The last four are **held back**: two pages stating both gaps at once, which were not used in
    /// arriving at the rule. They need a reference of their own, and the reason is worth keeping. Once
    /// the depth is gapped the bar's front face is no longer the box's front face, so its across edge
    /// sits on a different line of the scene and the baseline's edge is not a segment it belongs to.
    /// Each is therefore read against the page sharing its **other** gap, where the bar stands at the
    /// same depth and the two edges really are collinear. Read against the baseline instead, page
    /// seven comes out near 0.383 for a fraction that is 0.400 — wrong by twenty times what it is
    /// wrong by when referenced properly.
    /// </remarks>
    [Theory]
    [InlineData(1, 0, false, 50, "gapWidth 50")]
    [InlineData(2, 0, false, 150, "gapWidth 150")]
    [InlineData(3, 0, false, 300, "gapWidth 300")]
    [InlineData(4, 0, true, 50, "gapDepth 50")]
    [InlineData(5, 0, true, 150, "gapDepth 150")]
    [InlineData(6, 0, true, 300, "gapDepth 300")]
    [InlineData(7, 5, false, 150, "held back: gapWidth 150 beside a gapped depth")]
    [InlineData(7, 2, true, 150, "held back: gapDepth 150 beside a gapped width")]
    [InlineData(8, 6, false, 50, "held back: gapWidth 50 beside a gapped depth")]
    [InlineData(8, 1, true, 300, "held back: gapDepth 300 beside a gapped width")]
    public void A_bar_fills_its_slot_less_the_gap(int page, int against, bool inDepth, int gap, string what)
    {
        if (Reference() is not { } pdf) return;

        const double scale = 6;

        if (Edges(pdf, page, scale) is not { } bar || Edges(pdf, against, scale) is not { } whole)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var measured = inDepth
            ? Fraction(whole.Depth, bar.Depth)
            : Fraction(whole.Across, bar.Across);

        var rule = 1 / (1 + gap / 100.0);

        _output.WriteLine($"{what}: the bar fills {measured:0.0000} of its slot, the rule says {rule:0.0000}");

        Assert.InRange(measured - rule, -0.02, 0.02);
    }

    /// <summary>
    /// Only the across edge answers to <c>gapWidth</c>, and only the depth edge to <c>gapDepth</c>.
    /// </summary>
    /// <remarks>
    /// What makes the two edges identifiable at all, and worth asserting rather than assuming, since
    /// everything above rests on having picked the right one of the two. A gap that shortened both
    /// would mean the edges had been swapped or the scene was moving under the sweep.
    ///
    /// The across edge runs 141.8, 94.4, 56.6, 35.3 points as <c>gapWidth</c> goes 0, 50, 150, 300
    /// while the depth edge barely stirs — 50.8, 52.9, 54.8, 56.1, and that slight *growth* is the
    /// near corner sliding round the box as the bar narrows, not the depth changing.
    /// </remarks>
    [Fact]
    public void Each_gap_shortens_its_own_edge_and_not_the_other()
    {
        if (Reference() is not { } pdf) return;

        const double scale = 6;

        static double Length(((double X, double Y) A, (double X, double Y) B) edge) =>
            Math.Sqrt((edge.A.X - edge.B.X) * (edge.A.X - edge.B.X) +
                      (edge.A.Y - edge.B.Y) * (edge.A.Y - edge.B.Y));

        var across = new List<double>();
        var depth = new List<double>();

        foreach (var page in new[] { 0, 1, 2, 3 })
        {
            if (Edges(pdf, page, scale) is not { } edges)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            across.Add(Length(edges.Across));
            depth.Add(Length(edges.Depth));
        }

        _output.WriteLine("gapWidth 0, 50, 150, 300 — across " +
                          string.Join(", ", across.Select(v => v.ToString("0.0"))) +
                          "; depth " + string.Join(", ", depth.Select(v => v.ToString("0.0"))));

        // The across edge loses three quarters of itself.
        Assert.True(across[^1] < 0.3 * across[0],
            $"gapWidth barely moved the across edge: {across[0]:0.0} to {across[^1]:0.0}");

        // The depth edge does not follow it down.
        Assert.True(depth[^1] > 0.9 * depth[0],
            $"gapWidth shortened the depth edge too, {depth[0]:0.0} to {depth[^1]:0.0}, so the two " +
            "edges are not being told apart correctly");
    }

    /// <summary>
    /// The recovery fails when the rule is put back wrong.
    /// </summary>
    /// <remarks>
    /// A fraction recovered from a cross ratio needs showing that it can miss, because the arithmetic
    /// returns something plausible for any four numbers. Two injections: the competing reading of what
    /// the gap is a percentage of, and a corner moved.
    ///
    /// The rival rule is <c>1 - gap/100</c> — the gap as a share of the slot rather than of the bar —
    /// which agrees at nought and nowhere else. At <c>gapWidth</c> 50 it says 0.500 where Word draws
    /// 0.667, so it fails by eight times the tolerance here.
    /// </remarks>
    [Fact]
    public void The_competing_reading_of_the_gap_does_not_fit()
    {
        if (Reference() is not { } pdf) return;

        const double scale = 6;

        if (Edges(pdf, 1, scale) is not { } bar || Edges(pdf, 0, scale) is not { } whole)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var measured = Fraction(whole.Across, bar.Across);

        // The gap as a share of the slot, rather than of the bar.
        const double rival = 1 - 50 / 100.0;

        _output.WriteLine($"gapWidth 50: Word draws {measured:0.0000}; " +
                          $"slot/(1+g) says {1 / 1.5:0.0000}, 1-g says {rival:0.0000}");

        Assert.True(Math.Abs(measured - rival) > 0.1,
            $"the rival reading now fits too ({measured:0.0000} against {rival:0.0000}), so this " +
            "probe no longer tells the two apart");

        // And a corner half a point out is enough to see.
        var nudged = Fraction(whole.Across, ((bar.Across.A.X + 0.5, bar.Across.A.Y), bar.Across.B));

        _output.WriteLine($"half a point on one corner moves the fraction to {nudged:0.0000}");

        Assert.True(Math.Abs(nudged - measured) > 0.0005,
            "half a point on a corner did not move the recovered fraction at all");
    }
}
