namespace n8PDF.Ooxml;

/// <summary>
/// A bound on how deep the document parser may recurse, shared across every recursive descent it
/// makes (#143–#146).
/// </summary>
/// <remarks>
/// A <c>.docx</c> is a tree the parser walks by recursion, and several of those walks can recurse
/// without end on a document written to make them: inline wrappers inside inline wrappers, a table
/// in a cell in a table, an equation nested into itself, a text box holding a paragraph holding a
/// text box. Each is one stack frame per level, and a <c>StackOverflowException</c> cannot be
/// caught in .NET — it kills the whole process, so a converter cannot fall back to a clean failure.
///
/// What the stack cares about is the total depth, not which walk it belongs to, so the count is one
/// shared thread-local total across all of them. Past the bound the walk stops and returns what it
/// has rather than descending further — the same silent truncation the <c>Blocks</c> wrapper cap
/// already uses, because a document nested past this was not written to be read.
///
/// The bound is far past anything Word writes — a deeply laid-out document nests tables and
/// equations a handful deep, not hundreds — and far below what overflows a stack: at a few frames
/// per level it is on the order of a thousand frames, which every platform this targets handles
/// without trouble. It is a backstop against a hostile file, not a limit a real one meets.
/// </remarks>
internal static class ParseGuard
{
    private const int MaxDepth = 64;

    [ThreadStatic]
    private static int _depth;

    /// <summary>
    /// Descends one level if the bound allows, returning a scope that climbs back on dispose.
    /// The caller checks <see cref="Scope.Allowed"/>: when false, nothing was descended and the
    /// walk must return without recursing.
    /// </summary>
    public static Scope Enter()
    {
        if (_depth >= MaxDepth) return new Scope(false);

        _depth++;
        return new Scope(true);
    }

    /// <summary>One level of the parse, climbed back on dispose. Only ever created by <see cref="Enter"/>.</summary>
    public readonly ref struct Scope
    {
        public bool Allowed { get; }

        internal Scope(bool allowed) => Allowed = allowed;

        public void Dispose()
        {
            if (Allowed) _depth--;
        }
    }
}
