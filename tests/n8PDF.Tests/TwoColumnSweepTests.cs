using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The division of a stated table width, asked in the one shape that can answer it.
/// </summary>
/// <remarks>
/// Two columns leave one edge between them, so where that edge falls is the whole of what Word
/// decided. two-column-sweep-probe puts an 'i' and a 'b' either side of it — 66.68 and 120 twips
/// of Times at twelve — and sweeps the table from a hundred points to a hundred and eighty-seven
/// and a half in steps of two and a half, thirty-six widths in all. Each width says the share the
/// first column got lies in a window a grid step wide divided by that width; together they say it
/// far more narrowly than any one could.
///
/// What they say is that **there is no such share**. The width of 3450 twips needs a share of at
/// least 0.358261 and the width of 3250 needs one below 0.358154, and no number is both. Sweeping
/// every ratio between 0.3555 and 0.3605 against every rounding of the column — exact, whole twip,
/// half twip, up, down — the best any of them manages is 35 of the 36, and that 35 wants a ratio
/// matching no measurement of the two letters. So Word's division is not a fixed proportion of the
/// table applied to a fixed pair of wants, whatever else it is.
///
/// Ours is proportional to wants of 67 and 120 twips — each letter's width rounded up to a whole
/// twip — which lands 34 of the 36, the best any *natural* ratio does, and misses only the two
/// widths where its edge falls a hundredth of a point the wrong side of a rounding boundary. The
/// four tables at the end, with the columns the other way about, put the same edge in from the
/// right and agree exactly.
/// </remarks>
public class TwoColumnSweepTests(ITestOutputHelper output)
{
    /// <summary>The widths the probe sweeps, in twips.</summary>
    private static IEnumerable<int> Widths => Enumerable.Range(0, 36).Select(i => 2000 + i * 50);

    /// <summary>Every edge of the sweep, against Word's, and the two that miss.</summary>
    [Fact]
    public void Every_edge_but_two_is_words()
    {
        if (TestFonts.SkipForMissingFonts("two-column-sweep-probe")) return;

        var word = Edges(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "two-column-sweep-probe.pdf")));
        var ours = Edges(Converter.Convert(Fixtures.Build("two-column-sweep-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        Assert.Equal(40, word.Count);
        Assert.Equal(word.Count, ours.Count);

        var missed = new List<int>();

        for (var i = 0; i < word.Count; i++)
        {
            if (Math.Abs(word[i] - ours[i]) > 0.001) missed.Add(i);
        }

        output.WriteLine($"{word.Count - missed.Count} of {word.Count} exact; missed at " +
                         string.Join(", ", missed.Select(i => $"#{i} (word {word[i]}, ours {ours[i]})")));

        // The two are the widths of 2700 and 3250 twips, and each is a single step.
        Assert.Equal([14, 25], missed);

        foreach (var i in missed) Assert.Equal(0.24, Math.Abs(word[i] - ours[i]), 0.001);

        // Including the four at the end, which measure the same edge from the right-hand side.
        Assert.All(word.Skip(36).Zip(ours.Skip(36)), pair => Assert.Equal(pair.First, pair.Second, 0.001));
    }

    /// <summary>
    /// No single share of the table explains Word's own page, which is what stops this being
    /// re-opened in the hope of a tidy ratio.
    /// </summary>
    /// <remarks>
    /// Each width's drawn edge says the exact edge was within half a grid step of it, and so bounds
    /// the share above and below. Two of the thirty-six bounds cross.
    /// </remarks>
    [Fact]
    public void No_single_share_of_the_table_fits_words_own_page()
    {
        if (TestFonts.SkipForMissingFonts("two-column-sweep-probe")) return;

        var word = Edges(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "two-column-sweep-probe.pdf")));

        var lower = 0.0;
        var upper = 1.0;
        var lowerAt = 0;
        var upperAt = 0;

        foreach (var (twips, edge) in Widths.Zip(word))
        {
            var points = twips / 20.0;

            if ((edge - 0.12) / points > lower) (lower, lowerAt) = ((edge - 0.12) / points, twips);
            if ((edge + 0.12) / points < upper) (upper, upperAt) = ((edge + 0.12) / points, twips);
        }

        output.WriteLine($"the share must be at least {lower:0.000000} (from {lowerAt} twips) " +
                         $"and below {upper:0.000000} (from {upperAt} twips)");

        Assert.True(lower > upper,
            $"a share of {lower:0.000000} would satisfy every width, which would make this a solved rule");

        // Ours is the best any natural reading of the two letters does: their widths rounded up to
        // a whole twip. It satisfies the lower bound and not the upper, by a hundredth of a point.
        const double Ours = 67 / 187.0;

        Assert.True(Ours > upper && Ours >= lower,
            $"{Ours:0.000000} should sit above the crossing, as the arithmetic says it does");
    }

    /// <summary>The width of each shaded first cell, down the document.</summary>
    private static List<double> Edges(byte[] pdf)
    {
        var kept = new List<ExtractedRectangle>();

        foreach (var fill in PdfPathExtractor.Extract(pdf)
                     .Where(fill => fill.Width > 1 && fill.Height > 1)
                     .OrderBy(fill => fill.PageIndex).ThenBy(fill => fill.Top).ThenBy(fill => fill.Left))
        {
            if (kept.Any(seen => seen.PageIndex == fill.PageIndex && Math.Abs(seen.Top - fill.Top) < 1))
            {
                continue;
            }

            kept.Add(fill);
        }

        return [.. kept.Select(fill => fill.Width)];
    }
}
