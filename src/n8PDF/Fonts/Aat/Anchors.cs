namespace n8PDF.Fonts.Aat;

/// <summary>
/// Apple's <c>ankr</c> table: the points on each glyph that other glyphs are fastened to.
/// </summary>
/// <remarks>
/// The same idea as an OpenType anchor and a plainer arrangement: a glyph has a numbered list of
/// points, and the table that puts marks on letters names a point on each of the two. Nothing here
/// says what a point is for — which is the mark's and which the letter's is the business of the
/// table doing the fastening.
/// </remarks>
internal sealed class Anchors
{
    private readonly byte[] _data;
    private readonly int _lookup;
    private readonly int _glyphData;
    private readonly int _glyphCount;

    private Anchors(byte[] data, int lookup, int glyphData, int glyphCount)
    {
        _data = data;
        _lookup = lookup;
        _glyphData = glyphData;
        _glyphCount = glyphCount;
    }

    public static Anchors? Read(byte[] data, int offset, int glyphCount)
    {
        if (offset + 12 > data.Length) return null;

        var version = AatLookup.Read16(data, offset);
        if (version != 0) return null;

        var lookup = offset + (int)AatLookup.Read32(data, offset + 4);
        var glyphData = offset + (int)AatLookup.Read32(data, offset + 8);

        return lookup >= data.Length || glyphData >= data.Length
            ? null
            : new Anchors(data, lookup, glyphData, glyphCount);
    }

    /// <summary>One numbered point on one glyph, in design units.</summary>
    public (short X, short Y)? Point(ushort glyph, int index)
    {
        if (AatLookup.Value(_data, _lookup, glyph, _glyphCount) is not { } offset) return null;

        var at = _glyphData + offset;
        if (at + 4 > _data.Length) return null;

        var count = (int)AatLookup.Read32(_data, at);
        if (index < 0 || index >= count) return null;

        var point = at + 4 + index * 4;
        if (point + 4 > _data.Length) return null;

        return (AatLookup.ReadInt16(_data, point), AatLookup.ReadInt16(_data, point + 2));
    }
}
