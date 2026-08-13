using n8PDF.Fonts.OpenType;

namespace n8PDF.Fonts.Aat;

/// <summary>
/// One entry of a state table: where to go next, what to do on the way, and whatever the kind of
/// table it belongs to needs to say.
/// </summary>
internal readonly record struct StateEntry(int NextState, ushort Flags, ushort First, ushort Second);

/// <summary>
/// Apple's way of describing what a font does to a run: a machine that reads the glyphs one at a
/// time and changes state.
/// </summary>
/// <remarks>
/// Where OpenType says "these glyphs in this company become those glyphs", this says "in this
/// state, a glyph of this class takes you to that state, and on the way you may mark this one,
/// swap that one, or write several as one". It is the older idea and the more general one: the
/// same machinery expresses ligatures, contextual shapes, insertions and rearrangement, which
/// OpenType needs four kinds of lookup for.
///
/// A class is a group of glyphs the machine cannot tell apart. Four of them are reserved and mean
/// something other than a glyph: the end of the text, a glyph outside the table, one that has been
/// deleted, and the end of a line.
/// </remarks>
internal sealed class StateMachine
{
    public const int StartOfText = 0;

    public const ushort DeletedGlyph = 0xFFFF;

    private const int EndOfText = 0;
    private const int OutOfBounds = 1;
    private const int Deleted = 2;

    /// <summary>Don't move on to the next glyph before changing state.</summary>
    public const ushort DontAdvance = 0x4000;

    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _classes;
    private readonly int _classTable;
    private readonly int _stateArray;
    private readonly int _entryTable;
    private readonly int _glyphCount;
    private readonly int _entrySize;

    private StateMachine(byte[] data, int start, int classes, int classTable, int stateArray,
        int entryTable, int glyphCount, int entrySize)
    {
        _data = data;
        _start = start;
        _classes = classes;
        _classTable = classTable;
        _stateArray = stateArray;
        _entryTable = entryTable;
        _glyphCount = glyphCount;
        _entrySize = entrySize;
    }

    /// <summary>
    /// Reads the header of a state table. Everything in it is an offset from the table itself.
    /// </summary>
    /// <param name="extra">How many further values each entry carries beyond its state and flags.</param>
    public static StateMachine? Read(byte[] data, int start, int glyphCount, int extra)
    {
        if (start + 16 > data.Length) return null;

        var classes = (int)AatLookup.Read32(data, start);
        var classTable = start + (int)AatLookup.Read32(data, start + 4);
        var stateArray = start + (int)AatLookup.Read32(data, start + 8);
        var entryTable = start + (int)AatLookup.Read32(data, start + 12);

        if (classes < 4 || classTable >= data.Length || stateArray >= data.Length ||
            entryTable >= data.Length)
        {
            return null;
        }

        return new StateMachine(data, start, classes, classTable, stateArray, entryTable,
            glyphCount, 4 + extra * 2);
    }

    /// <summary>Which class a glyph is in, as far as this machine is concerned.</summary>
    private int ClassOf(ushort glyph) =>
        glyph == DeletedGlyph
            ? Deleted
            : AatLookup.Value(_data, _classTable, glyph, _glyphCount) ?? OutOfBounds;

    private StateEntry Entry(int state, int glyphClass)
    {
        var at = _stateArray + (state * _classes + glyphClass) * 2;
        if (at + 2 > _data.Length) return default;

        var index = AatLookup.Read16(_data, at);
        var entry = _entryTable + index * _entrySize;

        if (entry + _entrySize > _data.Length) return default;

        return new StateEntry(
            AatLookup.Read16(_data, entry),
            AatLookup.Read16(_data, entry + 2),
            _entrySize > 4 ? AatLookup.Read16(_data, entry + 4) : (ushort)0,
            _entrySize > 6 ? AatLookup.Read16(_data, entry + 6) : (ushort)0);
    }

    /// <summary>
    /// Runs the machine over a buffer, calling back for every entry it reaches.
    /// </summary>
    /// <remarks>
    /// The end of the text is a class of its own, so the machine is run one step past the last
    /// glyph: a font that writes two letters as one needs to be told that the run has ended before
    /// it can act on what it has been holding.
    /// </remarks>
    public void Run(List<ShapeItem> buffer, Action<int, StateEntry> act)
    {
        var state = StartOfText;
        var at = 0;
        var steps = 0;
        var limit = buffer.Count * 64 + 1024;

        while (steps++ < limit)
        {
            var glyphClass = at < buffer.Count ? ClassOf(buffer[at].Glyph) : EndOfText;
            var entry = Entry(state, glyphClass);

            act(at, entry);

            state = entry.NextState;

            if (at >= buffer.Count) break;

            if ((entry.Flags & DontAdvance) == 0) at++;
        }
    }
}
