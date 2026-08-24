using n8PDF.Fonts;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The font engine against hostile input (tier 3: uncaught throws that aborted the conversion).
/// A malformed embedded font costs its own face, and at most its own shaping — never the
/// conversion.
/// </summary>
public class FontHostileTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] Be32(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] Ttcf(uint faceCount, uint offset)
    {
        var b = new List<byte>();
        b.AddRange("ttcf"u8.ToArray());
        b.AddRange(Be32(0x00010000));
        b.AddRange(Be32(faceCount));
        b.AddRange(Be32(offset));
        return b.ToArray();
    }

    [Fact]
    public void A_collection_face_offset_near_int_max_does_not_throw()   // #181
    {
        // Before: the offset seated the reader past the end, Require's Position+count overflowed
        // and the array index threw IndexOutOfRangeException past the FontFormatException net.
        var faces = TrueTypeFont.LoadAll(Ttcf(1, 0x7FFFFFFF));
        _output.WriteLine($"LoadAll returned {faces.Count} face(s)");
        Assert.Empty(faces);
    }

    [Fact]
    public void A_negative_collection_face_count_does_not_throw()   // #182
    {
        // Before: the high-bit count cast negative and new List(negative) threw
        // ArgumentOutOfRangeException, outside LoadAll's per-face guard.
        var faces = TrueTypeFont.LoadAll(Ttcf(0x80000000, 0));
        _output.WriteLine($"LoadAll returned {faces.Count} face(s)");
        Assert.Empty(faces);
    }

    [Fact]
    public void Load_of_a_malformed_collection_fails_cleanly()   // #182
    {
        // The single-face entry raises the sanctioned exception, not a runtime one.
        Assert.IsType<FontFormatException>(
            Record.Exception(() => TrueTypeFont.Load(Ttcf(0x80000000, 0))));
    }

    [Fact]
    public void Shaping_a_font_with_scrambled_layout_tables_does_not_throw()   // #183, #186
    {
        var path = TestFonts.TimesNewRomanPath;
        if (!File.Exists(path))
        {
            Assert.False(TestFonts.OfficeFontsRequired, "Times New Roman missing");
            return;
        }

        var bytes = File.ReadAllBytes(path);

        // Scramble the table directory's offset/length fields (bytes 12 onward, past the 12-byte
        // header) so every table points wild — the shaper reads GSUB/GPOS/cmap at attacker
        // offsets, which before threw out of the unguarded apply phase.
        for (var i = 12; i < Math.Min(bytes.Length, 12 + 16 * 30); i += 3)
            bytes[i] ^= 0xFF;

        TrueTypeFont font;
        try
        {
            font = TrueTypeFont.Load(bytes);
        }
        catch (FontFormatException)
        {
            // A collection too broken to load at all is the sanctioned outcome; nothing to shape.
            _output.WriteLine("the scrambled font would not load, which is itself a clean failure");
            return;
        }

        // Whatever the tables became, shaping returns rather than throwing.
        var shaped = Record.Exception(() => TextShaper.Shape(font, "office ligature"));
        Assert.Null(shaped);
        _output.WriteLine("shaping the scrambled font returned without throwing");
    }

    private static void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void U32(List<byte> b, long v) { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    [Fact]
    public void A_cmap_format12_with_a_huge_group_count_does_not_oom_or_hang()   // #157
    {
        // A cmap table: version, one subtable record pointing at a format-12 subtable whose
        // numGroups claims a billion. Before, groupCount*4 sized a Dictionary (overflowing to a
        // negative capacity → ArgumentOutOfRangeException) or the fill ran unbounded.
        var sub = new List<byte>();
        U16(sub, 12); U16(sub, 0);      // format 12, reserved
        U32(sub, 16);                    // length
        U32(sub, 0);                     // language
        U32(sub, 0x40000000);            // numGroups — a billion

        var cmap = new List<byte>();
        U16(cmap, 0); U16(cmap, 1);      // version, numTables
        U16(cmap, 3); U16(cmap, 10);     // platform 3, encoding 10 (full Unicode)
        U32(cmap, 12);                    // subtable at offset 12
        cmap.AddRange(sub);

        var thread = new Thread(() =>
        {
            try { CharacterMap.Parse(cmap.ToArray(), 0); } catch (FontFormatException) { }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "cmap format-12 parse did not finish — not bounded");
        _output.WriteLine("cmap format-12 with a billion groups returned without OOM or hang");
    }

}
