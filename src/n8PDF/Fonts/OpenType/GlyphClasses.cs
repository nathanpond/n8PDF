namespace n8PDF.Fonts.OpenType;

/// <summary>
/// What a font's <c>GDEF</c> table says each glyph is: a letter, a shape standing for several, a
/// mark, or a piece of one.
/// </summary>
/// <remarks>
/// This is what makes a lookup's instruction to ignore something mean anything. A rule about two
/// letters that must still fire with a vowel written between them says "ignore marks", and which
/// glyphs are marks is a question only the font can answer — a mark is not a range of characters
/// and not a width of nought. Without this table every such rule is silently a rule about two
/// adjacent glyphs, which in a script that writes vowels above its letters is almost never true.
/// </remarks>
internal sealed class GlyphClasses
{
    public const int Base = 1;
    public const int Ligature = 2;
    public const int Mark = 3;
    public const int Component = 4;

    private readonly byte[] _data;
    private readonly int _classDef;
    private readonly int _markAttachClassDef;
    private readonly int[] _markSets;

    private GlyphClasses(byte[] data, int classDef, int markAttachClassDef, int[] markSets)
    {
        _data = data;
        _classDef = classDef;
        _markAttachClassDef = markAttachClassDef;
        _markSets = markSets;
    }

    /// <summary>
    /// Whether the font says anything about what its glyphs are. Where it does, its silence about
    /// one glyph is an answer — that glyph is not a mark — and where it does not, the question has
    /// to be put to the character instead.
    /// </summary>
    public bool Classifies => _classDef != 0;

    public int ClassOf(ushort glyph) =>
        _classDef == 0 ? 0 : LayoutReaders.ClassOf(_data, _classDef, glyph);

    public bool IsMark(ushort glyph) => ClassOf(glyph) == Mark;

    /// <summary>
    /// Which attachment class a mark is in, for the lookups that see one class of mark and not the
    /// others — the Arabic rules that treat the dots differently from the vowels.
    /// </summary>
    public int MarkAttachClass(ushort glyph) =>
        _markAttachClassDef == 0 ? 0 : LayoutReaders.ClassOf(_data, _markAttachClassDef, glyph);

    /// <summary>Whether a glyph is in one of the named sets of marks a lookup may filter by.</summary>
    public bool InMarkSet(int set, ushort glyph) =>
        set < _markSets.Length && _markSets[set] != 0 &&
        LayoutReaders.CoverageIndex(_data, _markSets[set], glyph) >= 0;

    public static GlyphClasses? Read(byte[] data, int offset)
    {
        try
        {
            var reader = new BigEndianReader(data, offset);

            reader.Skip(2); // major version
            int minor = reader.ReadUInt16();

            int classDef = reader.ReadUInt16();
            reader.ReadUInt16(); // attachList, which is for hinting
            reader.ReadUInt16(); // ligCaretList, which is for cursor placement
            int markAttachClassDef = reader.ReadUInt16();

            var markSets = Array.Empty<int>();

            if (minor >= 2)
            {
                int markSetsOffset = reader.ReadUInt16();

                if (markSetsOffset != 0)
                {
                    var sets = new BigEndianReader(data, offset + markSetsOffset);
                    sets.Skip(2); // format

                    int count = sets.ReadUInt16();
                    markSets = new int[count];

                    for (var i = 0; i < count; i++)
                    {
                        var at = (int)sets.ReadUInt32();
                        markSets[i] = at == 0 ? 0 : offset + markSetsOffset + at;
                    }
                }
            }

            if (classDef != 0) classDef += offset;
            if (markAttachClassDef != 0) markAttachClassDef += offset;

            return classDef == 0 && markAttachClassDef == 0 && markSets.Length == 0
                ? null
                : new GlyphClasses(data, classDef, markAttachClassDef, markSets);
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }
}
