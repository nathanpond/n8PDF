using System.Text;
using n8PDF.Fonts.Aat;
using n8PDF.Fonts.OpenType;

namespace n8PDF.Fonts;

/// <summary>
/// A single font face parsed from an SFNT container (<c>.ttf</c>, <c>.otf</c>, or one member of
/// a <c>.ttc</c> collection). Parsed from scratch: no platform font APIs are involved, so
/// measurement is identical on every OS.
/// </summary>
internal sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, TableRecord> _tables;
    private readonly CharacterMap _cmap;
    private readonly ushort[] _advanceWidths;
    private readonly Dictionary<int, short>? _kerning;

    private LayoutTable? _gsub;
    private LayoutTable? _gpos;
    private GlyphClasses? _classes;
    private Metamorphosis? _metamorphosis;
    private ExtendedKerning? _extendedKerning;
    private volatile bool _layoutRead;

    private readonly object _layoutGate = new();

    private readonly Dictionary<int, short> _pairKerning = [];

    private TrueTypeFont(
        byte[] data,
        Dictionary<string, TableRecord> tables,
        CharacterMap cmap,
        ushort[] advanceWidths,
        Dictionary<int, short>? kerning,
        FontMetrics metrics,
        int glyphCount,
        string familyName,
        string subfamilyName,
        string postScriptName,
        bool isBold,
        bool isItalic,
        bool hasCffOutlines)
    {
        _data = data;
        _tables = tables;
        _cmap = cmap;
        _advanceWidths = advanceWidths;
        _kerning = kerning;
        Metrics = metrics;
        GlyphCount = glyphCount;
        FamilyName = familyName;
        SubfamilyName = subfamilyName;
        PostScriptName = postScriptName;
        IsBold = isBold;
        IsItalic = isItalic;
        HasCffOutlines = hasCffOutlines;
    }

    public FontMetrics Metrics { get; }

    public int UnitsPerEm => Metrics.UnitsPerEm;

    public int GlyphCount { get; }

    public string FamilyName { get; }

    public string SubfamilyName { get; }

    public string PostScriptName { get; }

    public bool IsBold { get; }

    public bool IsItalic { get; }

    /// <summary>
    /// True for OpenType/CFF fonts. They carry PostScript outlines rather than <c>glyf</c>, and
    /// embed into a PDF through a different font-file key.
    /// </summary>
    public bool HasCffOutlines { get; }

    public bool IsSymbolFont => _cmap.IsSymbolEncoded;

    /// <summary>Maps a Unicode code point to a glyph index; 0 (.notdef) when unmapped.</summary>
    public ushort GetGlyphIndex(int codePoint) => _cmap.GetGlyph(codePoint);

    /// <summary>Advance width in design units.</summary>
    public int GetAdvanceWidth(ushort glyphIndex)
    {
        if (_advanceWidths.Length == 0) return 0;

        // Monospaced tails are common: glyphs past the last full metric all share its width.
        return glyphIndex < _advanceWidths.Length
            ? _advanceWidths[glyphIndex]
            : _advanceWidths[^1];
    }

    /// <summary>
    /// Kerning adjustment in design units for a glyph pair.
    /// </summary>
    /// <remarks>
    /// A font may say this in either of two places. <c>GPOS</c> is asked first, being where fonts
    /// shipped this century put it — Calibri has no legacy table at all — and the old <c>kern</c>
    /// table answers for the fonts that predate it. Zero either way when neither kerns the pair.
    /// </remarks>
    /// <summary>
    /// What the font says its glyphs should be swapped for, and where each of them goes, or null
    /// where it says nothing. Read when first asked, since most documents never ask.
    /// </summary>
    /// <remarks>
    /// A new one for each run rather than one kept on the font. What script a run is in and which
    /// features it may use are properties of the run; the tables they are read from belong to the
    /// font and are shared, which is where the work is.
    /// </remarks>
    internal Substitutor? Substitutor
    {
        get
        {
            ReadLayout();
            return _gsub is null ? null : new Substitutor(_gsub, _classes);
        }
    }

    internal Positioner? Positioner
    {
        get
        {
            ReadLayout();
            return _gpos is null ? null : new Positioner(_gpos, _classes);
        }
    }

    /// <summary>
    /// What the font does to a run of glyphs, where it says so in Apple's tables rather than in
    /// OpenType's. Read only where there is no <c>GSUB</c> to read instead: a font that carries
    /// both carries the same shaping twice, and the OpenType tables are the ones Word reads.
    /// </summary>
    internal Metamorphosis? Metamorphosis
    {
        get
        {
            ReadLayout();
            return _gsub is null ? _metamorphosis : null;
        }
    }

    /// <summary>
    /// Where the font says its glyphs go, in Apple's tables rather than OpenType's. Read only
    /// where there is no <c>GPOS</c> to read instead, for the same reason its shaping is.
    /// </summary>
    internal ExtendedKerning? ExtendedKerning
    {
        get
        {
            ReadLayout();
            return _gpos is null ? _extendedKerning : null;
        }
    }

    /// <summary>What the font says each of its glyphs is: a letter, a ligature, a mark.</summary>
    internal GlyphClasses? Classes
    {
        get
        {
            ReadLayout();
            return _classes;
        }
    }

    /// <summary>
    /// Reads the tables that shape a script, the first time anything asks for them.
    /// </summary>
    /// <remarks>
    /// Under a lock, and with the flag set last, because one face is shared: the library reads a
    /// file once and hands the same face to every conversion that wants it, so two of them may
    /// arrive here at the same moment. Setting the flag first would let the second go on with a
    /// font whose tables the first had not finished reading.
    /// </remarks>
    private void ReadLayout()
    {
        if (_layoutRead) return;

        lock (_layoutGate)
        {
            if (_layoutRead) return;

            ReadLayoutTables();

            _layoutRead = true;
        }
    }

    private void ReadLayoutTables()
    {
        if (Tables.TryGetValue("GDEF", out var gdef)) _classes = GlyphClasses.Read(_data, gdef.Offset);

        if (Tables.TryGetValue("GSUB", out var gsub))
            _gsub = LayoutTable.Read(_data, gsub.Offset, gsub.Length);

        if (Tables.TryGetValue("GPOS", out var gpos))
            _gpos = LayoutTable.Read(_data, gpos.Offset, gpos.Length);

        if (_gsub is null && Tables.TryGetValue("morx", out var morx))
            _metamorphosis = Metamorphosis.Read(_data, morx.Offset, morx.Length, GlyphCount);

        if (_gpos is null && Tables.TryGetValue("kerx", out var kerx))
        {
            var anchors = Tables.TryGetValue("ankr", out var ankr)
                ? Anchors.Read(_data, ankr.Offset, GlyphCount)
                : null;

            _extendedKerning = ExtendedKerning.Read(_data, kerx.Offset, GlyphCount, anchors);
        }
    }

    /// <summary>
    /// Whether the font treats this glyph as a mark drawn on something else.
    /// </summary>
    /// <remarks>
    /// The <c>GDEF</c> table is the answer where a font has one. Where it has not, a glyph that
    /// advances the pen by nothing and stands for a character that is a mark is treated as one,
    /// which is what the rules that ignore marks are reaching for.
    /// </remarks>
    public bool IsMark(ushort glyph)
    {
        if (Classes is { } classes && classes.ClassOf(glyph) is var kind and not 0)
            return kind == GlyphClasses.Mark;

        return glyph < _advanceWidths.Length && _advanceWidths[glyph] == 0 && glyph != 0;
    }

    /// <summary>
    /// What the font says about drawing this pair closer together, from either of the two places
    /// it may say it.
    /// </summary>
    /// <remarks>
    /// <c>GPOS</c> is asked first, being where fonts shipped this century put it — Calibri has no
    /// legacy table at all — and the old <c>kern</c> table answers for the fonts that predate it.
    /// The pair is put through the font's own kerning lookups, which is the only way to get an
    /// answer that agrees with what a whole run would be given.
    /// </remarks>
    public short GetKerning(ushort left, ushort right)
    {
        var key = (left << 16) | right;

        lock (_pairKerning)
        {
            if (_pairKerning.TryGetValue(key, out var found)) return found;
        }

        short adjustment = 0;

        if (Positioner is { } positioner && positioner.HasLookups("kern"))
        {
            var pair = new List<ShapeItem>
            {
                new(left, 0, uint.MaxValue) { Advance = GetAdvanceWidth(left) },
                new(right, 1, uint.MaxValue) { Advance = GetAdvanceWidth(right) }
            };

            positioner.Apply(pair, "kern");

            if (pair.Count == 2) adjustment = (short)(pair[0].Advance - GetAdvanceWidth(left));
        }

        if (adjustment == 0 && _kerning is not null)
            adjustment = _kerning.GetValueOrDefault(key, (short)0);

        lock (_pairKerning) _pairKerning[key] = adjustment;

        return adjustment;
    }

    /// <summary>
    /// The box one glyph's ink fits in, in design units, or null where the face does not say.
    /// </summary>
    /// <remarks>
    /// Every glyph record begins with its own bounding box, which is what this reads; a glyph with
    /// no outline at all — a space — has no record and no box. A PostScript-outlined face keeps
    /// its glyphs somewhere else entirely and answers nothing here, which is why the one thing
    /// that asks, WordArt, falls back on the face's own overall box.
    /// </remarks>
    public (int MinX, int MinY, int MaxX, int MaxY)? GetGlyphBounds(ushort glyph)
    {
        if (HasCffOutlines || glyph >= GlyphCount) return null;
        if (!_tables.TryGetValue("glyf", out var glyf) || !_tables.TryGetValue("loca", out var loca))
            return null;

        if (!_tables.TryGetValue("head", out var head)) return null;

        try
        {
            var longLoca = new BigEndianReader(_data, head.Offset + 50).ReadInt16() != 0;

            var (start, end) = longLoca
                ? (Offset32(loca, glyph), Offset32(loca, glyph + 1))
                : (Offset16(loca, glyph), Offset16(loca, glyph + 1));

            // Nothing between one offset and the next means a glyph that draws nothing.
            if (end <= start || start + 10 > glyf.Length) return null;

            var reader = new BigEndianReader(_data, glyf.Offset + start + 2);

            return (reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException)
        {
            return null;
        }

        int Offset16(TableRecord table, int index) =>
            new BigEndianReader(_data, table.Offset + index * 2).ReadUInt16() * 2;

        int Offset32(TableRecord table, int index) =>
            (int)new BigEndianReader(_data, table.Offset + index * 4).ReadUInt32();
    }

    public bool HasTable(string tag) => _tables.ContainsKey(tag);

    internal ReadOnlySpan<byte> GetTable(string tag)
    {
        if (!_tables.TryGetValue(tag, out var record))
            return default;

        var length = Math.Min(record.Length, _data.Length - record.Offset);
        return _data.AsSpan(record.Offset, Math.Max(0, length));
    }

    internal IReadOnlyDictionary<string, TableRecord> Tables => _tables;

    /// <summary>
    /// What the face says about setting mathematics, where it says anything: a face meant for it
    /// carries a <c>MATH</c> table, and one that does not borrows the proportions of a face that
    /// does. Read the first time it is asked for, since most documents hold no equations.
    /// </summary>
    internal MathConstants Mathematics
    {
        get
        {
            lock (_layoutGate)
            {
                return _math ??= Tables.TryGetValue("MATH", out var math)
                    ? MathConstants.Read(_data, math.Offset, Metrics.UnitsPerEm)
                      ?? MathConstants.Fallback(Metrics.UnitsPerEm)
                    : MathConstants.Fallback(Metrics.UnitsPerEm);
            }
        }
    }

    private MathConstants? _math;

    /// <summary>
    /// How much room each sloped glyph wants after it, where the face says. Empty for a face that
    /// says nothing about mathematics.
    /// </summary>
    internal IReadOnlyDictionary<ushort, short> ItalicCorrections
    {
        get
        {
            lock (_layoutGate)
            {
                return _italics ??= Tables.TryGetValue("MATH", out var math)
                    ? MathConstants.ReadItalics(_data, math.Offset)
                    : new Dictionary<ushort, short>();
            }
        }
    }

    private IReadOnlyDictionary<ushort, short>? _italics;

    /// <summary>
    /// The taller shapes the face offers for each glyph that grows, with the height of each.
    /// </summary>
    internal IReadOnlyDictionary<ushort, IReadOnlyList<(ushort Glyph, int Height)>> MathVariants
    {
        get
        {
            lock (_layoutGate)
            {
                return _variants ??= Tables.TryGetValue("MATH", out var math)
                    ? MathConstants.ReadVariants(_data, math.Offset)
                    : new Dictionary<ushort, IReadOnlyList<(ushort, int)>>();
            }
        }
    }

    private IReadOnlyDictionary<ushort, IReadOnlyList<(ushort Glyph, int Height)>>? _variants;

    /// <summary>
    /// What the face says about tucking a script into each corner of each glyph.
    /// </summary>
    internal IReadOnlyDictionary<ushort, MathKerns> MathKerns
    {
        get
        {
            lock (_layoutGate)
            {
                return _kerns ??= Tables.TryGetValue("MATH", out var math)
                    ? MathConstants.ReadKerns(_data, math.Offset)
                    : new Dictionary<ushort, MathKerns>();
            }
        }
    }

    private IReadOnlyDictionary<ushort, MathKerns>? _kerns;

    /// <summary>
    /// How the face says to build a bracket taller than the tallest shape it keeps.
    /// </summary>
    internal IReadOnlyDictionary<ushort, MathAssembly> MathAssemblies
    {
        get
        {
            lock (_layoutGate)
            {
                return _assemblies ??= Tables.TryGetValue("MATH", out var math)
                    ? MathConstants.ReadAssemblies(_data, math.Offset)
                    : new Dictionary<ushort, MathAssembly>();
            }
        }
    }

    private IReadOnlyDictionary<ushort, MathAssembly>? _assemblies;

    internal byte[] SourceData => _data;

    /// <summary>
    /// Produces a standalone single-font SFNT suitable for embedding. Collections cannot be
    /// embedded directly, so their tables are repackaged into a fresh container.
    /// </summary>
    /// <summary>
    /// The font program to embed. Given the glyphs a document used, the outlines of everything
    /// else are left out.
    /// </summary>
    public byte[] GetEmbeddableFontProgram(
        IReadOnlyCollection<ushort>? usedGlyphs = null, bool dropHinting = false) =>
        SfntRepackager.BuildStandalone(this, usedGlyphs, out _, dropHinting);

    /// <summary>
    /// The same, reporting whether anything was left out. A face this cannot rebuild is embedded
    /// whole, and a PDF may only give a subset tag to a font that really is one.
    /// </summary>
    public byte[] GetEmbeddableFontProgram(
        IReadOnlyCollection<ushort> usedGlyphs, out bool subsetted, bool dropHinting = false) =>
        SfntRepackager.BuildStandalone(this, usedGlyphs, out subsetted, dropHinting);

    /// <summary>
    /// The font program with its glyphs numbered again, in the order given, so that the file
    /// holds as many glyphs as were used rather than as many as the face has.
    /// </summary>
    public byte[] GetRenumberedFontProgram(
        IReadOnlyList<ushort> order, IReadOnlyList<(int CodePoint, ushort Glyph)> characters,
        out bool subsetted, bool dropHinting = false) =>
        SfntRepackager.BuildStandalone(this, order, out subsetted, dropHinting, order, characters);

    // ----- loading -----

    public static TrueTypeFont Load(byte[] data, int faceIndex = 0)
    {
        List<int> faces;
        try
        {
            faces = GetFaceOffsets(data);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            // The collection header is malformed in a way its own reads did not catch; a face
            // that will not parse is no face, which is the same answer a bad SFNT gives (#182).
            throw new FontFormatException("The font collection header is malformed.");
        }

        if (faceIndex < 0 || faceIndex >= faces.Count)
            throw new FontFormatException($"Face index {faceIndex} is out of range; the file holds {faces.Count} face(s).");

        return Parse(data, faces[faceIndex]);
    }

    /// <summary>Loads every face in the file. A <c>.ttc</c> commonly holds a whole family.</summary>
    public static IReadOnlyList<TrueTypeFont> LoadAll(byte[] data)
    {
        List<int> faces;
        try
        {
            faces = GetFaceOffsets(data);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException
            or OverflowException or FontFormatException)
        {
            return [];  // a malformed collection header carries no readable face (#182)
        }

        var result = new List<TrueTypeFont>(faces.Count);
        foreach (var offset in faces)
        {
            // One malformed face in a collection should not make the rest unusable.
            try
            {
                result.Add(Parse(data, offset));
            }
            catch (FontFormatException)
            {
            }
        }

        return result;
    }

    public static int GetFaceCount(byte[] data) => GetFaceOffsets(data).Count;

    private static List<int> GetFaceOffsets(byte[] data)
    {
        var reader = new BigEndianReader(data);
        var tag = reader.ReadTag();

        if (tag != "ttcf")
            return [0];

        reader.ReadUInt32(); // version

        // Clamped before it sizes anything: a negative cast throws ArgumentOutOfRangeException
        // from the List constructor, and a huge value pre-sizes gigabytes — neither of which the
        // collection holds more than a handful of faces (#158, #182).
        var faceCount = Math.Clamp((long)reader.ReadUInt32(), 0, 1024);

        var offsets = new List<int>((int)faceCount);
        for (var i = 0; i < faceCount; i++)
            offsets.Add((int)reader.ReadUInt32());

        return offsets;
    }

    private static TrueTypeFont Parse(byte[] data, int directoryOffset)
    {
        var reader = new BigEndianReader(data, directoryOffset);
        var version = reader.ReadUInt32();

        // 0x00010000 is TrueType outlines, 'OTTO' is CFF outlines, 'true' is the legacy Mac tag.
        var hasCff = version == 0x4f54544f;
        if (version != 0x00010000 && !hasCff && version != 0x74727565)
            throw new FontFormatException($"Unrecognised SFNT version 0x{version:X8}.");

        int tableCount = reader.ReadUInt16();
        reader.Skip(6); // searchRange, entrySelector, rangeShift

        var tables = new Dictionary<string, TableRecord>(tableCount, StringComparer.Ordinal);
        for (var i = 0; i < tableCount; i++)
        {
            var tag = reader.ReadTag();
            var checksum = reader.ReadUInt32();
            var offset = (int)reader.ReadUInt32();
            var length = (int)reader.ReadUInt32();

            // A table cannot reach past the end of the file it is in. The offset was already
            // checked; the length was not, and it is a thirty-two bit number straight out of the
            // file — so anything reading a table by its declared length would have been asked for
            // up to two gigabytes on the word of a malformed font. Trimmed rather than refused,
            // because a truncated font with a readable head and cmap is still worth drawing with,
            // and this reader checks what it reads.
            if (offset >= 0 && offset < data.Length)
                tables[tag] = new TableRecord(tag, checksum, offset, Math.Clamp(length, 0, data.Length - offset));
        }

        var head = Require(tables, "head");
        var headReader = new BigEndianReader(data, head.Offset + 18);
        int unitsPerEm = headReader.ReadUInt16();
        if (unitsPerEm == 0) unitsPerEm = 1000;

        headReader.Position = head.Offset + 36;
        int bboxMinX = headReader.ReadInt16();
        int bboxMinY = headReader.ReadInt16();
        int bboxMaxX = headReader.ReadInt16();
        int bboxMaxY = headReader.ReadInt16();
        int macStyle = headReader.ReadUInt16();

        var maxp = Require(tables, "maxp");
        var maxpReader = new BigEndianReader(data, maxp.Offset + 4);
        int glyphCount = maxpReader.ReadUInt16();

        var hhea = Require(tables, "hhea");
        var hheaReader = new BigEndianReader(data, hhea.Offset + 4);
        int ascender = hheaReader.ReadInt16();
        int descender = hheaReader.ReadInt16();
        int lineGap = hheaReader.ReadInt16();
        hheaReader.Position = hhea.Offset + 34;
        int metricCount = hheaReader.ReadUInt16();

        var advanceWidths = ReadHorizontalMetrics(data, tables, metricCount, glyphCount);
        var os2 = ReadOs2(data, tables);
        var italicAngle = ReadItalicAngle(data, tables);
        var names = ReadNames(data, tables);
        var cmap = CharacterMap.Parse(data, Require(tables, "cmap").Offset);
        var kerning = ReadKerning(data, tables);

        // macStyle bit 0 is bold and bit 1 is italic; OS/2 fsSelection says the same thing and
        // the two disagree often enough in the wild that either asserting the style is enough.
        var isBold = (macStyle & 0x1) != 0 || os2.IsBold;
        var isItalic = (macStyle & 0x2) != 0 || os2.IsItalic || italicAngle != 0;

        var familyName = names.TypographicFamily ?? names.Family ?? "Unknown";
        var subfamilyName = names.TypographicSubfamily ?? names.Subfamily ?? "Regular";
        var postScriptName = names.PostScript ?? familyName.Replace(" ", string.Empty);

        var metrics = new FontMetrics
        {
            UnitsPerEm = unitsPerEm,
            Ascender = ascender,
            Descender = descender,
            LineGap = lineGap,
            TypoAscender = os2.TypoAscender,
            TypoDescender = os2.TypoDescender,
            TypoLineGap = os2.TypoLineGap,
            WinAscent = os2.WinAscent,
            WinDescent = os2.WinDescent,
            IsEastAsian = os2.IsEastAsian,
            UseTypoMetrics = os2.UseTypoMetrics,
            CapHeight = os2.CapHeight != 0 ? os2.CapHeight : (int)(unitsPerEm * 0.7),
            XHeight = os2.XHeight != 0 ? os2.XHeight : (int)(unitsPerEm * 0.5),
            ItalicAngle = italicAngle,
            WeightClass = os2.WeightClass,
            IsFixedPitch = IsMonospaced(advanceWidths),
            BBoxMinX = bboxMinX,
            BBoxMinY = bboxMinY,
            BBoxMaxX = bboxMaxX,
            BBoxMaxY = bboxMaxY
        };

        return new TrueTypeFont(
            data, tables, cmap, advanceWidths, kerning, metrics, glyphCount,
            familyName, subfamilyName, postScriptName, isBold, isItalic, hasCff);
    }

    private static TableRecord Require(Dictionary<string, TableRecord> tables, string tag) =>
        tables.TryGetValue(tag, out var record)
            ? record
            : throw new FontFormatException($"The font is missing the required '{tag}' table.");

    /// <summary>
    /// Reads <c>hmtx</c>. Only the first <c>numberOfHMetrics</c> glyphs carry an advance width;
    /// every glyph after that repeats the last one, which is how monospaced tails are stored
    /// compactly.
    /// </summary>
    private static ushort[] ReadHorizontalMetrics(
        byte[] data, Dictionary<string, TableRecord> tables, int metricCount, int glyphCount)
    {
        if (!tables.TryGetValue("hmtx", out var hmtx) || metricCount == 0)
            return [];

        var count = Math.Min(metricCount, glyphCount);
        var widths = new ushort[Math.Max(count, 1)];
        var reader = new BigEndianReader(data, hmtx.Offset);

        for (var i = 0; i < count; i++)
        {
            if (reader.Position + 4 > data.Length) break;
            widths[i] = reader.ReadUInt16();
            reader.ReadInt16(); // left side bearing
        }

        return widths;
    }

    private static Os2Values ReadOs2(byte[] data, Dictionary<string, TableRecord> tables)
    {
        if (!tables.TryGetValue("OS/2", out var os2))
            return new Os2Values { WeightClass = 400 };

        var reader = new BigEndianReader(data, os2.Offset);
        int version = reader.ReadUInt16();
        reader.Skip(2); // xAvgCharWidth
        int weightClass = reader.ReadUInt16();

        reader.Position = os2.Offset + 62;
        int fsSelection = reader.ReadUInt16();

        reader.Position = os2.Offset + 68;
        int typoAscender = reader.ReadInt16();
        int typoDescender = reader.ReadInt16();
        int typoLineGap = reader.ReadInt16();
        int winAscent = reader.ReadUInt16();
        int winDescent = reader.ReadUInt16();

        // Which code pages the face says it is for. Only the East Asian ones are asked about,
        // and only because Word gives a face that declares one a taller line than its own
        // metrics ask for. Bits 17 to 21 are Japanese, the two Chinese and the two Korean.
        var codePages = version >= 1 && os2.Offset + 82 <= data.Length ? reader.ReadUInt32() : 0;

        var capHeight = 0;
        var xHeight = 0;
        if (version >= 2 && os2.Offset + 90 <= data.Length)
        {
            reader.Position = os2.Offset + 86;
            xHeight = reader.ReadInt16();
            capHeight = reader.ReadInt16();
        }

        return new Os2Values
        {
            WeightClass = weightClass == 0 ? 400 : weightClass,
            IsItalic = (fsSelection & 0x1) != 0,
            IsBold = (fsSelection & 0x20) != 0,
            UseTypoMetrics = (fsSelection & 0x80) != 0,
            TypoAscender = typoAscender,
            TypoDescender = typoDescender,
            TypoLineGap = typoLineGap,
            WinAscent = winAscent,
            WinDescent = winDescent,
            IsEastAsian = (codePages & 0x3E0000) != 0,
            CapHeight = capHeight,
            XHeight = xHeight
        };
    }

    private static double ReadItalicAngle(byte[] data, Dictionary<string, TableRecord> tables)
    {
        if (!tables.TryGetValue("post", out var post)) return 0;

        var reader = new BigEndianReader(data, post.Offset + 4);
        return reader.ReadFixed();
    }

    /// <summary>
    /// Reads format 0 subtables from the legacy <c>kern</c> table. Later OpenType fonts express
    /// kerning through GPOS instead, which is a separate and much larger job.
    /// </summary>
    private static Dictionary<int, short>? ReadKerning(byte[] data, Dictionary<string, TableRecord> tables)
    {
        if (!tables.TryGetValue("kern", out var kern)) return null;

        try
        {
            var reader = new BigEndianReader(data, kern.Offset);
            int version = reader.ReadUInt16();
            if (version != 0) return null; // Apple's extended kern table; not supported.

            int subtableCount = reader.ReadUInt16();
            var pairs = new Dictionary<int, short>();

            for (var i = 0; i < subtableCount; i++)
            {
                var subtableStart = reader.Position;
                reader.ReadUInt16(); // subtable version
                int length = reader.ReadUInt16();
                int coverage = reader.ReadUInt16();

                // Bits 8-15 hold the format; we only handle 0 (ordered pair list). Bit 0 must
                // be set for horizontal kerning, and bit 1 clear for kerning rather than minimum.
                var format = coverage >> 8;
                var horizontal = (coverage & 0x1) != 0;
                var isMinimum = (coverage & 0x2) != 0;

                if (format == 0 && horizontal && !isMinimum)
                {
                    int pairCount = reader.ReadUInt16();
                    reader.Skip(6); // searchRange, entrySelector, rangeShift

                    for (var p = 0; p < pairCount; p++)
                    {
                        if (reader.Position + 6 > data.Length) break;
                        int left = reader.ReadUInt16();
                        int right = reader.ReadUInt16();
                        var value = reader.ReadInt16();
                        if (value != 0) pairs[(left << 16) | right] = value;
                    }
                }

                if (length <= 0) break;
                reader.Position = subtableStart + length;
            }

            return pairs.Count > 0 ? pairs : null;
        }
        catch (FontFormatException)
        {
            // A malformed kern table costs us kerning, not the whole font.
            return null;
        }
    }

    private static NameValues ReadNames(byte[] data, Dictionary<string, TableRecord> tables)
    {
        var result = new NameValues();
        if (!tables.TryGetValue("name", out var name)) return result;

        var reader = new BigEndianReader(data, name.Offset);
        reader.ReadUInt16(); // format
        int recordCount = reader.ReadUInt16();
        int stringOffset = reader.ReadUInt16();

        // Windows records win over Mac ones when both are present: they are UTF-16 and are what
        // the document's font names were authored against. And an English record wins over the
        // same name in another language, whichever platform it is on — a document naming
        // "Gujarati MT" is naming the font by the name Word knows it by, and the face itself also
        // carries that name written in Gujarati.
        var bestScore = new Dictionary<int, int>();

        for (var i = 0; i < recordCount; i++)
        {
            int platformId = reader.ReadUInt16();
            int encodingId = reader.ReadUInt16();
            int languageId = reader.ReadUInt16();
            int nameId = reader.ReadUInt16();
            int length = reader.ReadUInt16();
            int offset = reader.ReadUInt16();

            if (nameId is not (1 or 2 or 6 or 16 or 17)) continue;

            var english = platformId switch
            {
                3 => languageId == 0x0409,
                1 => languageId == 0,
                _ => true
            };

            var score = platformId switch
            {
                3 => english ? 6 : 3,
                0 => 4,
                1 => english ? 5 : 1,
                _ => 0
            };

            if (score == 0) continue;
            if (bestScore.TryGetValue(nameId, out var existing) && existing >= score) continue;

            var start = name.Offset + stringOffset + offset;
            if (start < 0 || start + length > data.Length) continue;

            var bytes = data.AsSpan(start, length);
            var text = platformId == 3 || platformId == 0
                ? Encoding.BigEndianUnicode.GetString(bytes)
                : Encoding.ASCII.GetString(bytes);

            if (string.IsNullOrWhiteSpace(text)) continue;

            bestScore[nameId] = score;
            switch (nameId)
            {
                case 1: result.Family = text; break;
                case 2: result.Subfamily = text; break;
                case 6: result.PostScript = text; break;
                case 16: result.TypographicFamily = text; break;
                case 17: result.TypographicSubfamily = text; break;
            }
        }

        return result;
    }

    private static bool IsMonospaced(ushort[] widths)
    {
        if (widths.Length < 2) return false;

        // Glyph 0 is .notdef and often has an atypical width, so start the comparison at 1.
        var first = widths[1];
        for (var i = 2; i < widths.Length; i++)
        {
            if (widths[i] != first && widths[i] != 0) return false;
        }

        return true;
    }

    internal readonly record struct TableRecord(string Tag, uint Checksum, int Offset, int Length);

    private sealed class NameValues
    {
        public string? Family { get; set; }
        public string? Subfamily { get; set; }
        public string? PostScript { get; set; }
        public string? TypographicFamily { get; set; }
        public string? TypographicSubfamily { get; set; }
    }

    private sealed class Os2Values
    {
        public int WeightClass { get; init; }
        public bool IsItalic { get; init; }
        public bool IsBold { get; init; }
        public bool UseTypoMetrics { get; init; }
        public int TypoAscender { get; init; }
        public int TypoDescender { get; init; }
        public int TypoLineGap { get; init; }
        public int WinAscent { get; init; }
        public int WinDescent { get; init; }
        public bool IsEastAsian { get; init; }
        public int CapHeight { get; init; }
        public int XHeight { get; init; }
    }
}
