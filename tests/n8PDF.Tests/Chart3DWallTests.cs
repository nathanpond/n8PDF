using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The room a three-dimensional plot stands in — walls, floor and the gridlines on them — held
/// to Word's own raster of <c>chart-3d-wall-probe</c>.
/// </summary>
/// <remarks>
/// The probe's bars are slivers (value 1 of 100) deliberately: a bar of any height casts an
/// occlusion shadow across the floor, and what a corner finder returns there is the shadow's
/// edge rather than the floor's — which cost the first attempt at measuring the floor an
/// afternoon of chasing a quad that was not there.
///
/// Two instruments. The walls' corners are read by #106's finder and compared against
/// <see cref="Chart3DComposer.Projection"/> in page coordinates, on Word's pages and on ours
/// alike. The floor and the line-dense gridline pages are compared ink to ink, the way
/// <see cref="ChartDropLineTests"/> compares hanging lines: each of Word's pixels of a colour
/// must have one of ours within a pixel, and the other way about — which catches a surface
/// drawn the wrong size, a line on the wrong surface, and a shade left out, without asking a
/// corner finder to work on the floor's slab edge.
/// </remarks>
public class Chart3DWallTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-wall-probe";

    private readonly ITestOutputHelper _output = output;

    private static readonly ChartScene Camera = new(15, 20, 100, false, 30);
    private static readonly ChartScene CameraMirrored = new(15, 340, 100, false, 30);
    private static readonly ChartScene Square = new(15, 20, 100, true, 30);
    private static readonly ChartScene SquareMirrored = new(15, 340, 100, true, 30);

    private static ChartScene Scene(int page) => page switch
    {
        0 or 4 or 6 => Camera,
        1 or 7 => CameraMirrored,
        2 or 5 => Square,
        _ => SquareMirrored,
    };

    private (RenderedPage Ours, RenderedPage Word, double Scale)? Pages(int page)
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(FixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        const double scale = 6;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(File.ReadAllBytes(reference), page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        return (mine, word, scale);
    }

    /// <summary>The walls page uses the smaller stated rectangle; the grid pages a taller one.</summary>
    private static IChart3DProjection Projection(int page) => Chart3DComposer.Projection(
        Scene(page),
        categories: page < 4 ? 1 : 3,
        series: page < 4 ? 1 : 3,
        rectLeft: 144,
        rectTop: page < 4 ? 93.6 : 82.8,
        rectWidth: 216,
        rectHeight: page < 4 ? 118.8 : 172.8);

    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double X, double Y)>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Astray(IReadOnlyList<(double X, double Y)> model, IReadOnlyList<(double X, double Y)> word)
    {
        var n = word.Count;
        if (model.Count != n) return double.PositiveInfinity;
        var best = double.PositiveInfinity;
        for (var shift = 0; shift < n; shift++)
        foreach (var reverse in new[] { false, true })
        {
            double worst = 0;
            for (var i = 0; i < n; i++)
            {
                var idx = reverse ? (shift - i % n + 2 * n) % n : (shift + i) % n;
                var dx = model[idx].X - word[i].X;
                var dy = model[idx].Y - word[i].Y;
                worst = Math.Max(worst, Math.Sqrt(dx * dx + dy * dy));
            }
            best = Math.Min(best, worst);
        }
        return best;
    }

    private static bool BackRed((byte R, byte G, byte B) p) => p.R > 200 && p.G < 60 && p.B < 60;
    private static bool SideGreen((byte R, byte G, byte B) p) => p.G is > 90 and < 160 && p.R < 60 && p.B < 60;
    private static bool FloorBlue((byte R, byte G, byte B) p) => p.B is > 150 and < 235 && p.R < 60 && p.G < 60;

    /// <summary>
    /// How much of one page's ink of a colour has the other's within a pixel, both ways round.
    /// </summary>
    private (double WordCovered, double OursCovered, int WordInk, int OursInk) Agreement(
        RenderedPage ours, RenderedPage word, double scale,
        Func<(byte R, byte G, byte B), bool> belongs)
    {
        var (wordInk, wordNear, oursInk, oursNear) = (0, 0, 0, 0);

        bool Near(RenderedPage page, double x, double y)
        {
            for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
                if (belongs(page.At(x + dx / scale, y + dy / scale, scale)))
                    return true;
            return false;
        }

        for (var y = 74.0; y < 287; y += 1 / scale)
        for (var x = 74.0; x < 431; x += 1 / scale)
        {
            var w = belongs(word.At(x, y, scale));
            var o = belongs(ours.At(x, y, scale));

            if (w) { wordInk++; if (o || Near(ours, x, y)) wordNear++; }
            if (o) { oursInk++; if (w || Near(word, x, y)) oursNear++; }
        }

        return (wordInk == 0 ? 1 : (double)wordNear / wordInk,
                oursInk == 0 ? 1 : (double)oursNear / oursInk, wordInk, oursInk);
    }

    /// <summary>
    /// The back and side walls' corners land within the projection's own tolerance of Word's, on
    /// every walls page — both arms, both signs of the turn — and the side wall stands where the
    /// mirror puts it.
    /// </summary>
    /// <remarks>
    /// The bar for Word's pages is the projections' own: a third of a point on the oblique arm
    /// plus the corner finder's fraction; the camera pages carry the same vintage caveat as
    /// <see cref="Chart3DCameraTests"/>. Our own render is read by the same finder, so the same
    /// bar holds for it.
    /// </remarks>


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_walls_stand_where_word_stands_them(int page)
    {
        if (Pages(page) is not { } pages) return;

        var projection = Projection(page);

        var back = new[] { (0.0, 0.0, 1.0), (1.0, 0.0, 1.0), (1.0, 1.0, 1.0), (0.0, 1.0, 1.0) };
        var side = new[] { (0.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 1.0, 1.0), (0.0, 0.0, 1.0) };

        foreach (var (name, plane, belongs) in new[]
        {
            ("back", back, (Func<(byte R, byte G, byte B), bool>)BackRed),
            ("side", side, SideGreen),
        })
        {
            var model = plane.Select(q => projection.Project(q.Item1, q.Item2, q.Item3)).ToList();

            foreach (var (whose, rendered) in new[] { ("word", pages.Word), ("ours", pages.Ours) })
            {
                var shape = BoxSilhouette.Find(rendered, pages.Scale, belongs, (73, 73, 431, 287), corners: 4);

                if (!shape.Found)
                {
                    _output.WriteLine($"p{page} {name} {whose}: {shape.Refused}");
                    continue;
                }

                var astray = Astray(model, shape.Points);
                _output.WriteLine($"p{page} {name} {whose}: astray {astray:0.000}pt");
                Assert.True(astray < 0.85, $"p{page} {name} {whose}: {astray:0.000}pt astray");
            }
        }

        // The floor's own corners are unreadable past its slab edge, so it is held ink to ink.
        var floor = Agreement(pages.Ours, pages.Word, pages.Scale, FloorBlue);
        _output.WriteLine($"p{page} floor: word covered {floor.WordCovered:0.0000}, " +
                          $"ours covered {floor.OursCovered:0.0000} ({floor.WordInk}/{floor.OursInk} px)");
        Assert.True(floor.WordInk > 2000, "the floor left almost no ink to compare");

        // Everything Word paints blue, we paint blue — measured 1.0000 on all four pages. The
        // other direction holds only 0.84: Word stands its sliver bars on the floor and we do
        // not draw bars yet, so our floor is unbroken where Word's has little slabs standing on
        // it. #101 tightens this to match.
        Assert.True(floor.WordCovered > 0.995,
            $"p{page}: only {floor.WordCovered:0.0000} of Word's floor is covered by ours");
        Assert.True(floor.OursCovered > 0.80,
            $"p{page}: only {floor.OursCovered:0.0000} of our floor is near Word's");
    }

    /// <summary>
    /// The gridlines land on the surfaces where Word lands them, ink to ink per axis colour.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void The_gridlines_rule_the_surfaces_where_words_do(int page)
    {
        if (Pages(page) is not { } pages) return;

        var colours = new (string Name, Func<(byte R, byte G, byte B), bool> Belongs)[]
        {
            ("val red", p => p.R > 150 && p.G < 90 && p.B < 90),
            ("ser blue", p => p.B > 150 && p.R < 90 && p.G < 90),
            ("cat green", p => p.G > 110 && p.R < 90 && p.B < 90),
            ("minor orange", p => p.R > 180 && p.G is > 90 and < 190 && p.B < 90),
        };

        foreach (var (name, belongs) in colours)
        {
            if (name == "minor orange" && page != 6) continue;

            var (wordCovered, oursCovered, wordInk, oursInk) =
                Agreement(pages.Ours, pages.Word, pages.Scale, belongs);

            _output.WriteLine($"p{page} {name}: word covered {wordCovered:0.0000}, " +
                              $"ours covered {oursCovered:0.0000} ({wordInk}/{oursInk} px)");
            Assert.True(wordInk > 500, $"p{page} {name}: Word left almost no ink");

            // The bars sit under the measured values, which the asymmetries explain: Word's
            // plot is a 300 dpi raster whose antialiasing halves a stated line's
            // threshold-passing core, so our crisp strokes always cover more ink than his and
            // never the reverse; and at the junctions Word's colours blend where ours overdraw,
            // which costs the covered-by-word direction a few points where lines coincide.
            var (word, ours) = name switch
            {
                "val red" => (0.90, 0.93),
                "ser blue" => (0.85, 0.85),
                "cat green" => (0.99, 0.78),
                _ => (0.97, 0.98),
            };
            Assert.True(wordCovered > word,
                $"p{page} {name}: only {wordCovered:0.0000} of Word's ink is covered, under the {word} bar");
            Assert.True(oursCovered > ours,
                $"p{page} {name}: only {oursCovered:0.0000} of our ink is near Word's, under the {ours} bar");
        }
    }

    /// <summary>
    /// The side wall put on the wrong side of the box misses Word's by a hundred points.
    /// </summary>
    [Fact]
    public void The_side_wall_on_the_wrong_side_fails()
    {
        if (Pages(0) is not { } pages) return;

        var shape = BoxSilhouette.Find(pages.Word, pages.Scale, SideGreen, (73, 73, 431, 287), corners: 4);
        if (!shape.Found) return;

        var projection = Projection(0);
        var wrong = new[] { (1.0, 0.0, 0.0), (1.0, 1.0, 0.0), (1.0, 1.0, 1.0), (1.0, 0.0, 1.0) }
            .Select(q => projection.Project(q.Item1, q.Item2, q.Item3)).ToList();

        var astray = Astray(wrong, shape.Points);
        _output.WriteLine($"side wall at the box's right: {astray:0.000}pt astray");
        Assert.True(astray > 5, $"the wrong side lands only {astray:0.000}pt astray");
    }

    /// <summary>
    /// A turn past 180 drawn without the mirror — the raw angles fed straight to the camera —
    /// misses Word's picture by tens of points.
    /// </summary>
    [Fact]
    public void Left_unmirrored_the_turn_past_180_fails()
    {
        if (Pages(1) is not { } pages) return;

        var shape = BoxSilhouette.Find(pages.Word, pages.Scale, BackRed, (73, 73, 431, 287), corners: 4);
        if (!shape.Found) return;

        var raw = new Chart3DProjection(15, 340, 30, 100, null, 1, 1, 144, 93.6, 216, 118.8);
        var wrong = new[] { (0.0, 0.0, 1.0), (1.0, 0.0, 1.0), (1.0, 1.0, 1.0), (0.0, 1.0, 1.0) }
            .Select(q => raw.Project(q.Item1, q.Item2, q.Item3)).ToList();

        var astray = Astray(wrong, shape.Points);
        _output.WriteLine($"unmirrored at rotY 340: {astray:0.000}pt astray");
        Assert.True(astray > 5, $"the unmirrored back wall lands only {astray:0.000}pt astray");
    }

    /// <summary>
    /// Gridlines drawn flat — straight across the plot the way a two-dimensional chart rules
    /// them — land nowhere near the lines Word draws on the walls.
    /// </summary>
    [Fact]
    public void Drawn_flat_the_gridlines_fail()
    {
        if (Pages(4) is not { } pages) return;

        // Word's value lines down a clean back-wall column.
        var centres = RunCentres(pages.Word, pages.Scale, 300,
            px => px is { R: > 150, G: < 90, B: < 90 }, 80, 260);
        if (centres.Count == 0) return;

        // Flat, the mark at nought would rule the plot rectangle's bottom edge.
        const double flat = 82.8 + 172.8;
        var nearest = centres.Min(c => Math.Abs(c - flat));

        _output.WriteLine($"flat nought at y {flat:0.0}; Word's nearest line {nearest:0.0}pt away");
        Assert.True(nearest > 10, $"a flat gridline lands only {nearest:0.000}pt from a real one");
    }

    /// <summary>
    /// The depth axis rules row boundaries, not row centres: the boundary predictions land on
    /// Word's floor lines and the centre predictions land between them.
    /// </summary>
    [Fact]
    public void Centres_in_place_of_boundaries_fail()
    {
        if (Pages(4) is not { } pages) return;

        var lines = RunCentres(pages.Word, pages.Scale, 300,
            px => px is { B: > 150, R: < 90, G: < 90 }, 195, 258);
        if (lines.Count < 3) return;

        var projection = Projection(4);

        double At(double z)
        {
            var a = projection.Project(0, 0, z);
            var b = projection.Project(1, 0, z);
            return a.Y + (300 - a.X) / (b.X - a.X) * (b.Y - a.Y);
        }

        // The boundary at the back, z = 1, runs along the wall junction where Word's colours
        // blend to purple and leave no blue run to measure; the two interior boundaries carry
        // the assertion.
        foreach (var z in new[] { 1 / 3.0, 2 / 3.0 })
        {
            var predicted = At(z);
            var off = lines.Min(c => Math.Abs(c - predicted));
            _output.WriteLine($"boundary z {z:0.00} predicted y {predicted:0.00}, off by {off:0.00}pt");
            Assert.True(off < 1.0, $"the boundary at z {z:0.00} is {off:0.00}pt from Word's line");
        }

        foreach (var z in new[] { 1 / 6.0, 0.5, 5 / 6.0 })
        {
            var predicted = At(z);
            var off = lines.Min(c => Math.Abs(c - predicted));
            _output.WriteLine($"centre z {z:0.00} predicted y {predicted:0.00}, off by {off:0.00}pt");
            Assert.True(off > 2.5, $"a centre-drawn line at z {z:0.00} lands only {off:0.00}pt from a real one");
        }
    }

    /// <summary>The centres of a colour's runs down one column.</summary>
    private static List<double> RunCentres(
        RenderedPage page, double scale, double x,
        Func<(byte R, byte G, byte B), bool> belongs, double top, double bottom)
    {
        var centres = new List<double>();
        double began = -1;
        for (var y = top; y <= bottom; y += 1 / scale)
        {
            var hit = belongs(page.At(x, y, scale));
            if (hit && began < 0) began = y;
            if (!hit && began >= 0)
            {
                if (y - began > 0.5) centres.Add((began + y) / 2);
                began = -1;
            }
        }
        return centres;
    }
}
