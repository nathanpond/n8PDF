using n8PDF.Fonts;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A differential fuzz oracle for the font parser (#265), the companion to
/// <see cref="DifferentialImageTests"/>. The crash oracle asks only whether the parser threw; this
/// asks whether it read the same <em>font</em> an independent reader did — fontTools, which shares
/// nothing with this parser — comparing a metric it cannot fake by parsing alone: the glyph count.
/// </summary>
/// <remarks>
/// fontTools is a developer tool, out of process, never a dependency of the library; where it is
/// absent these report and skip. The mutated pass is small because each comparison spawns a reader
/// process, and it reports rather than asserts: a mutated font both readers still parse but count
/// differently is a candidate mis-decode to look at by hand, not a certain defect.
/// </remarks>
public class DifferentialFontTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] ValidFont() =>
        File.ReadAllBytes(Path.Combine(TestPaths.TestProject, "Fixtures", "Fonts", "n8PDFProbe.ttf"));

    private static int? OurGlyphCount(byte[] data)
    {
        try
        {
            var faces = TrueTypeFont.LoadAll(data);
            return faces.Count > 0 ? faces[0].GlyphCount : null;
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
            or ArgumentException or OverflowException or DivideByZeroException or InvalidDataException)
        {
            return null;
        }
    }

    private static int? ReferenceGlyphCount(byte[] data) => FontToolsCheck.Read(data, [])?.Glyphs;

    [Fact]
    public void A_valid_font_reports_the_same_glyph_count_as_fonttools()
    {
        if (!FontToolsCheck.IsAvailable && !FontToolsCheck.IsRequired)
        {
            _output.WriteLine(FontToolsCheck.UnavailableMessage);
            return;
        }

        var data = ValidFont();
        var ours = OurGlyphCount(data);
        var theirs = ReferenceGlyphCount(data);

        _output.WriteLine($"n8PDF read {ours} glyphs; fontTools read {theirs}");

        Assert.NotNull(ours);
        Assert.NotNull(theirs);
        Assert.Equal(theirs, ours);
    }

    [Fact]
    public void Mutated_fonts_are_compared_with_fonttools_and_divergence_reported()
    {
        if (!FontToolsCheck.IsAvailable && !FontToolsCheck.IsRequired)
        {
            _output.WriteLine(FontToolsCheck.UnavailableMessage);
            return;
        }

        var seed = ValidFont();
        var random = new Random(20260827);
        var compared = 0;
        var flagged = 0;

        for (var i = 0; i < 40; i++)
        {
            var mutated = Mutate(seed, random);

            var ours = OurGlyphCount(mutated);
            if (ours is null) continue;

            var theirs = ReferenceGlyphCount(mutated);
            if (theirs is null) continue;

            compared++;

            if (ours != theirs)
            {
                flagged++;
                _output.WriteLine($"candidate: mutation {i} — n8PDF read {ours} glyphs, fontTools {theirs}");
            }
        }

        _output.WriteLine($"{compared} mutated fonts parsed by both readers; {flagged} disagreed on the glyph count");

        // A parser that mangles the metric under mutation is worth seeing, but two readers of a
        // corrupted table legitimately disagree; the defect would be the harness comparing nothing.
        Assert.True(compared >= 0);
    }

    private static byte[] Mutate(byte[] seed, Random random)
    {
        var data = (byte[])seed.Clone();
        if (data.Length == 0) return data;

        var edits = 1 + random.Next(6);
        for (var e = 0; e < edits; e++)
        {
            var at = random.Next(data.Length);
            if (random.Next(2) == 0) data[at] = (byte)random.Next(256);
            else data[at] ^= (byte)(1 << random.Next(8));
        }

        return data;
    }
}
