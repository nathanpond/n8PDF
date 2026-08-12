using System.Globalization;
using System.Text;
using n8PDF.Fonts;

namespace n8PDF.Pdf;

/// <summary>
/// A font as it appears in a PDF: a composite Type0 font with Identity-H encoding over an
/// embedded CIDFontType2 descendant.
/// </summary>
/// <remarks>
/// Identity-H means the two bytes in a show-text operator are the glyph index itself, which
/// sidesteps single-byte encodings entirely — any character the font can draw is reachable, and
/// a <c>ToUnicode</c> map keeps the text selectable and searchable despite the encoding being
/// glyph indices rather than characters.
/// </remarks>
public sealed class PdfFont
{
    private readonly Dictionary<ushort, int> _glyphToUnicode = [];

    // The glyphs of the embedded font, in the order they were first used: the code a glyph is
    // written as is where it sits here, counting from one.
    private readonly Dictionary<ushort, ushort> _subsetIds = [];
    private readonly List<ushort> _subsetOrder = [];

    internal PdfFont(TrueTypeFont font, string resourceName)
    {
        Font = font;
        ResourceName = resourceName;
    }

    public TrueTypeFont Font { get; }

    /// <summary>The name this font is registered under in a page's resource dictionary.</summary>
    public string ResourceName { get; }

    /// <summary>Glyphs referenced so far, which is what the width array is built from.</summary>
    public int UsedGlyphCount => _glyphToUnicode.Count;

    /// <summary>
    /// Whether this font's glyphs are numbered again in the file it is embedded in. Only an
    /// outline face is: renumbering a CFF one means rewriting what its charset says about every
    /// glyph, and for a CID-keyed font its FDSelect too.
    /// </summary>
    private bool Renumbers => !Font.HasCffOutlines;

    /// <summary>
    /// Maps text to glyph indices, expanding surrogate pairs so that characters outside the
    /// basic multilingual plane map to a single glyph rather than two broken ones.
    /// </summary>
    public ushort[] MapToGlyphs(string text)
    {
        var glyphs = new List<ushort>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            glyphs.Add(Font.GetGlyphIndex(codePoint));
        }

        return [.. glyphs];
    }

    /// <summary>
    /// Encodes text for a show-text operator and records the glyph-to-character mapping needed
    /// for text extraction.
    /// </summary>
    public byte[] Encode(string text)
    {
        var bytes = new List<byte>(text.Length * 2);

        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            var glyph = Font.GetGlyphIndex(codePoint);
            RegisterGlyph(glyph, codePoint);

            var code = CodeFor(glyph);
            bytes.Add((byte)(code >> 8));
            bytes.Add((byte)code);
        }

        return [.. bytes];
    }

    /// <summary>Encodes already-mapped glyphs, recording each one's originating character.</summary>
    public byte[] EncodeGlyphs(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<int> codePoints)
    {
        var bytes = new byte[glyphs.Length * 2];

        for (var i = 0; i < glyphs.Length; i++)
        {
            RegisterGlyph(glyphs[i], i < codePoints.Length ? codePoints[i] : 0);

            var code = CodeFor(glyphs[i]);
            bytes[i * 2] = (byte)(code >> 8);
            bytes[i * 2 + 1] = (byte)code;
        }

        return bytes;
    }

    /// <summary>Advance width of a glyph in text-space thousandths, the unit PDF widths use.</summary>
    public double GetGlyphWidth1000(ushort glyph) =>
        Font.GetAdvanceWidth(glyph) * 1000.0 / Font.UnitsPerEm;

    /// <summary>
    /// The code this glyph is written as, which is where it will sit in the embedded font.
    /// </summary>
    /// <remarks>
    /// Glyphs are numbered again from one as they are first used, so that a font holding a
    /// hundred of them is a hundred glyphs long rather than three thousand mostly empty ones.
    /// With Identity-H the code in the content stream is the glyph index, so writing the new
    /// number here is what keeps the two agreeing — and it is also what the width array and the
    /// ToUnicode map are keyed by.
    ///
    /// A CFF face keeps its old numbering: renumbering one means rewriting the charset and, for a
    /// CID-keyed font, its FDSelect, and those say things about the glyphs that the file's own
    /// reader may go looking for.
    /// </remarks>
    private ushort CodeFor(ushort glyph)
    {
        if (!Renumbers) return glyph;
        if (glyph == 0) return 0;

        if (_subsetIds.TryGetValue(glyph, out var code)) return code;

        code = (ushort)(_subsetIds.Count + 1);
        _subsetIds[glyph] = code;
        _subsetOrder.Add(glyph);

        return code;
    }

    private void RegisterGlyph(ushort glyph, int codePoint)
    {
        // .notdef carries no meaningful character, and mapping it would put a spurious entry in
        // the ToUnicode table.
        if (glyph == 0) return;

        _glyphToUnicode.TryAdd(glyph, codePoint);
    }

    /// <summary>Writes the font's object graph into the document and returns the Type0 reference.</summary>
    internal PdfReference Build(PdfDocument document, bool dropHinting = false)
    {
        var glyphs = _glyphToUnicode.Keys.ToList();

        var characters = _glyphToUnicode
            .Select(pair => (CodePoint: pair.Value, Glyph: pair.Key))
            .ToList();

        var program = Renumbers
            ? Font.GetRenumberedFontProgram(_subsetOrder, characters, out var renumbered, dropHinting)
            : Font.GetEmbeddableFontProgram(glyphs, out renumbered, dropHinting);

        var subsetted = renumbered;

        // A subset font is named with a six-letter tag so that two documents carrying different
        // parts of the same face are not mistaken for each other. The tag is derived from the
        // glyphs themselves, which keeps it stable: converting a document twice gives the same
        // bytes, and that is what makes golden comparison possible at all.
        var baseFont = subsetted
            ? SubsetTag(glyphs) + "+" + SanitizeName(Font.PostScriptName)
            : SanitizeName(Font.PostScriptName);

        var fontFile = new PdfStream(program);
        // Length1 is the uncompressed size, which consumers need to reconstruct the program.
        fontFile.Set("Length1", program.Length);
        var fontFileRef = document.Add(fontFile);

        var descriptor = new PdfDictionary()
            .Set("Type", "FontDescriptor")
            .Set("FontName", baseFont)
            .Set("Flags", BuildFlags())
            .Set("FontBBox", new PdfArray()
                .Add(ToThousandths(Font.Metrics.BBoxMinX))
                .Add(ToThousandths(Font.Metrics.BBoxMinY))
                .Add(ToThousandths(Font.Metrics.BBoxMaxX))
                .Add(ToThousandths(Font.Metrics.BBoxMaxY)))
            .Set("ItalicAngle", Font.Metrics.ItalicAngle)
            .Set("Ascent", ToThousandths(Font.Metrics.Ascender))
            .Set("Descent", ToThousandths(Font.Metrics.Descender))
            .Set("CapHeight", ToThousandths(Font.Metrics.CapHeight))
            .Set("StemV", Font.Metrics.StemV);

        // TrueType outlines go in FontFile2; CFF outlines are an OpenType program in FontFile3.
        if (Font.HasCffOutlines)
        {
            fontFile.Set("Subtype", "OpenType");
            descriptor.Set("FontFile3", fontFileRef);
        }
        else
        {
            descriptor.Set("FontFile2", fontFileRef);
        }

        var descriptorRef = document.Add(descriptor);

        var cidFont = new PdfDictionary()
            .Set("Type", "Font")
            .Set("Subtype", "CIDFontType2")
            .Set("BaseFont", baseFont)
            .Set("CIDSystemInfo", new PdfDictionary()
                .Set("Registry", PdfString.FromText("Adobe"))
                .Set("Ordering", PdfString.FromText("Identity"))
                .Set("Supplement", 0))
            .Set("FontDescriptor", descriptorRef)
            .Set("DW", 1000)
            .Set("W", BuildWidths())
            // With Identity-H the CID is already the glyph index, so the map is the identity.
            .Set("CIDToGIDMap", "Identity");

        var cidFontRef = document.Add(cidFont);

        var type0 = new PdfDictionary()
            .Set("Type", "Font")
            .Set("Subtype", "Type0")
            .Set("BaseFont", baseFont)
            .Set("Encoding", "Identity-H")
            .Set("DescendantFonts", new PdfArray().Add(cidFontRef))
            .Set("ToUnicode", document.Add(BuildToUnicode()));

        return document.Add(type0);
    }

    /// <summary>
    /// Builds the /W array. Only referenced glyphs are listed; everything else falls back to /DW,
    /// which keeps the array small even though the whole font program is embedded.
    /// </summary>
    private PdfArray BuildWidths()
    {
        var widths = new PdfArray();
        if (_glyphToUnicode.Count == 0) return widths;

        // Keyed by the code each glyph is written as, and measured from the glyph it came from.
        var codes = _glyphToUnicode.Keys
            .Select(glyph => (Code: CodeFor(glyph), Glyph: glyph))
            .OrderBy(entry => entry.Code)
            .ToList();

        // The compact form groups consecutive CIDs: "startCid [w w w ...]".
        var index = 0;
        while (index < codes.Count)
        {
            var runStart = index;
            while (index + 1 < codes.Count && codes[index + 1].Code == codes[index].Code + 1)
                index++;

            var run = new PdfArray();
            for (var i = runStart; i <= index; i++)
                run.Add(Math.Round(GetGlyphWidth1000(codes[i].Glyph), 2));

            widths.Add(new PdfNumber((int)codes[runStart].Code)).Add(run);
            index++;
        }

        return widths;
    }

    /// <summary>
    /// Builds the ToUnicode CMap. Without it a viewer can render the page but copying text out
    /// yields the glyph indices rather than characters.
    /// </summary>
    private PdfStream BuildToUnicode()
    {
        var sb = new StringBuilder();
        sb.Append("""
                  /CIDInit /ProcSet findresource begin
                  12 dict begin
                  begincmap
                  /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
                  /CMapName /Adobe-Identity-UCS def
                  /CMapType 2 def
                  1 begincodespacerange
                  <0000> <FFFF>
                  endcodespacerange

                  """);

        var entries = _glyphToUnicode
            .Where(pair => pair.Value > 0)
            .Select(pair => new KeyValuePair<ushort, int>(CodeFor(pair.Key), pair.Value))
            .OrderBy(pair => pair.Key)
            .ToList();

        // A bfchar section may hold at most 100 entries.
        for (var offset = 0; offset < entries.Count; offset += 100)
        {
            var chunk = entries.Skip(offset).Take(100).ToList();
            sb.Append(chunk.Count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");

            foreach (var (glyph, codePoint) in chunk)
            {
                sb.Append('<').Append(glyph.ToString("X4", CultureInfo.InvariantCulture)).Append("> <");

                // Values are UTF-16BE, so anything outside the BMP is written as a surrogate pair.
                if (codePoint > 0xffff)
                {
                    var text = char.ConvertFromUtf32(codePoint);
                    foreach (var unit in text)
                        sb.Append(((int)unit).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(codePoint.ToString("X4", CultureInfo.InvariantCulture));
                }

                sb.Append(">\n");
            }

            sb.Append("endbfchar\n");
        }

        sb.Append("""
                  endcmap
                  CMapName currentdict /CMap defineresource pop
                  end
                  end
                  """);

        return new PdfStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// Font descriptor flags (ISO 32000-1 table 123). Symbolic and Nonsymbolic are mutually
    /// exclusive, and getting that pair wrong makes some viewers ignore the encoding entirely.
    /// </summary>
    private int BuildFlags()
    {
        var flags = 0;
        if (Font.Metrics.IsFixedPitch) flags |= 1 << 0;
        if (IsSerifFamily(Font.FamilyName)) flags |= 1 << 1;

        if (Font.IsSymbolFont) flags |= 1 << 2;
        else flags |= 1 << 5;

        if (Font.IsItalic || Font.Metrics.ItalicAngle != 0) flags |= 1 << 6;

        return flags;
    }

    private static bool IsSerifFamily(string familyName) =>
        familyName.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
        familyName.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
        familyName.Contains("Garamond", StringComparison.OrdinalIgnoreCase) ||
        familyName.Contains("Cambria", StringComparison.OrdinalIgnoreCase) ||
        familyName.Contains("Book Antiqua", StringComparison.OrdinalIgnoreCase) ||
        familyName.Contains("Palatino", StringComparison.OrdinalIgnoreCase) ||
        familyName.Contains("Serif", StringComparison.OrdinalIgnoreCase);

    private double ToThousandths(int designUnits) =>
        Math.Round(designUnits * 1000.0 / Font.UnitsPerEm);

    /// <summary>Strips characters that are awkward in a PDF name object.</summary>
    /// <summary>
    /// Six upper-case letters standing for which glyphs this subset holds.
    /// </summary>
    /// <remarks>
    /// The tag only has to differ between subsets, so it is a hash of the glyph set rather than a
    /// counter — a counter would make the tag depend on the order fonts happened to be used in,
    /// and two conversions of the same document would stop matching byte for byte.
    /// </remarks>
    private static string SubsetTag(List<ushort> glyphs)
    {
        // FNV-1a over the glyph indices in order, which is why they are sorted first.
        var hash = 2166136261u;
        foreach (var glyph in glyphs.Order())
        {
            hash = (hash ^ glyph) * 16777619;
            hash = (hash ^ (uint)(glyph >> 8)) * 16777619;
        }

        var tag = new char[6];
        for (var i = 0; i < tag.Length; i++)
        {
            tag[i] = (char)('A' + hash % 26);
            hash /= 26;
        }

        return new string(tag);
    }

    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '+' or '_' or '.')
                sb.Append(ch);
        }

        return sb.Length > 0 ? sb.ToString() : "Embedded";
    }
}
