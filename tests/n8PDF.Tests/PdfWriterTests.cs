using System.Text;
using n8PDF.Pdf;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tier 2 structural tests for the PDF writer, exercised independently of any DOCX input.
/// </summary>
public class PdfWriterTests
{
    [Fact]
    public void Number_formatting_is_culture_invariant_and_has_no_exponent()
    {
        Assert.Equal("0", PdfNumber.Format(0));
        Assert.Equal("72", PdfNumber.Format(72.0));
        Assert.Equal("12.5", PdfNumber.Format(12.5));
        Assert.Equal("-3.25", PdfNumber.Format(-3.25));

        // A value that would round-trip through scientific notation under "R" or "G".
        Assert.DoesNotContain("E", PdfNumber.Format(0.0000123), StringComparison.OrdinalIgnoreCase);

        // Negative values that round to zero must not emit "-0", which some parsers reject.
        Assert.Equal("0", PdfNumber.Format(-0.000001));
    }

    [Fact]
    public void Name_objects_escape_irregular_characters()
    {
        Assert.Equal("/Type", Write(new PdfName("Type")));
        Assert.Equal("/A#20B", Write(new PdfName("A B")));
        Assert.Equal("/Hash#23", Write(new PdfName("Hash#")));
    }

    [Fact]
    public void Strings_switch_to_hex_when_non_ascii()
    {
        Assert.Equal("(hello)", Write(PdfString.FromText("hello")));
        Assert.Equal(@"(a\(b\))", Write(PdfString.FromText("a(b)")));

        // Non-ASCII becomes UTF-16BE with a byte-order mark, written as hex.
        var unicode = Write(PdfString.FromText("é"));
        Assert.StartsWith("<FEFF", unicode);
    }

    [Fact]
    public void Flate_round_trips()
    {
        var original = Encoding.ASCII.GetBytes(new string('a', 4096) + "trailing");
        var encoded = PdfFilters.FlateEncode(original);

        Assert.True(encoded.Length < original.Length);
        Assert.Equal(original, PdfFilters.FlateDecode(encoded));
    }

    [Fact]
    public void Empty_document_has_valid_structure()
    {
        var document = new PdfDocument();
        var bytes = Save(document);
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.7", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/Type /Pages", text);
        Assert.Equal(0, document.PageCount);
    }

    [Fact]
    public void Xref_offsets_point_at_their_objects()
    {
        var document = new PdfDocument();
        document.AddPage(612, 792, out _);
        var bytes = Save(document);

        AssertXrefIsConsistent(bytes);
    }

    [Fact]
    public void Saving_twice_produces_identical_bytes()
    {
        // Save must not mutate the document; reproducible output is what makes golden
        // comparison viable at all.
        var document = new PdfDocument { Title = "Repeat" };
        document.AddPage(612, 792, out _);

        Assert.Equal(Save(document), Save(document));
    }

    [Fact]
    public void Page_writes_text_with_a_base14_font()
    {
        var document = new PdfDocument { Title = "n8PDF base-14 smoke test" };
        var page = document.AddPage(612, 792, out _);

        // A standard font needs no embedding, which lets us verify the writer before the
        // font engine exists.
        var font = document.Add(new PdfDictionary()
            .Set("Type", "Font")
            .Set("Subtype", "Type1")
            .Set("BaseFont", "Helvetica"));

        var content = new ContentStreamBuilder();
        content.BeginText()
            .SetFont("F1", 24)
            .SetTextPosition(72, 720)
            .ShowGlyphs("Hello from n8PDF"u8)
            .EndText();

        // A one-inch square rule, to confirm the path operators land where we expect.
        content.Save().SetStrokeColor(0.8, 0.1, 0.1).SetLineWidth(2)
            .Rectangle(72, 72, 72, 72).Stroke().Restore();

        var stream = new PdfStream(content.ToArray());
        page.Set("Contents", document.Add(stream));
        page.Set("Resources", new PdfDictionary()
            .Set("Font", new PdfDictionary().Set("F1", font)));

        var bytes = Save(document);
        var path = TestPaths.WriteArtifact("base14-smoke.pdf", bytes);

        AssertXrefIsConsistent(bytes);
        Assert.Equal(1, document.PageCount);
        Assert.True(new FileInfo(path).Length > 0);

        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/BaseFont /Helvetica", text);
        Assert.Contains("/MediaBox [0 0 612 792]", text);
    }

    [Fact]
    public void Text_operators_outside_a_text_object_are_rejected()
    {
        var content = new ContentStreamBuilder();

        // Emitting Tf outside BT/ET produces a file that some viewers silently drop text from,
        // so this is a hard error rather than a warning.
        Assert.Throws<InvalidOperationException>(() => content.SetFont("F1", 12));
    }

    /// <summary>
    /// Parses the trailing xref table and confirms each recorded offset really is the start of
    /// the object it claims. This catches the whole class of "one byte off" writer bugs.
    /// </summary>
    private static void AssertXrefIsConsistent(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);

        var startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.True(startxref >= 0, "missing startxref");

        var offsetText = text[(startxref + "startxref".Length)..].Trim().Split('\n')[0].Trim();
        var xrefOffset = int.Parse(offsetText);
        Assert.Equal("xref", text.Substring(xrefOffset, 4));

        var lines = text[xrefOffset..].Split('\n');
        var counts = lines[1].Split(' ');
        var objectCount = int.Parse(counts[1]);

        // lines[0] is "xref", lines[1] the subsection header, lines[2] the free-list head for
        // object 0; the entry for object i therefore sits at lines[i + 2].
        for (var i = 1; i < objectCount; i++)
        {
            var entry = lines[i + 2];
            var objectOffset = int.Parse(entry[..10]);
            var expected = $"{i} 0 obj";
            Assert.Equal(expected, text.Substring(objectOffset, expected.Length));
        }
    }

    private static byte[] Save(PdfDocument document)
    {
        using var buffer = new MemoryStream();
        document.Save(buffer);
        return buffer.ToArray();
    }

    private static string Write(PdfObject value)
    {
        using var buffer = new MemoryStream();
        var document = new PdfDocument();
        document.Add(value);

        // Round-trip through a full save and pull the object body back out, which exercises the
        // real write path rather than a test-only shortcut.
        document.Save(buffer);
        var text = Encoding.Latin1.GetString(buffer.ToArray());
        var marker = "3 0 obj\n";
        var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = text.IndexOf("\nendobj", start, StringComparison.Ordinal);
        return text[start..end];
    }
}
