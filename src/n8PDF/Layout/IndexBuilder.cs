using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Turns the entries a document marks into the index that lists them.
/// </summary>
/// <remarks>
/// An index is written in two halves. Where a term belongs, the document carries an XE field that
/// draws nothing at all — it is there to be found, not read — and where the index goes, an INDEX
/// field gathers every one of them, sorts them, and lists each term against the pages it was
/// marked on. A term written "Engine:analytical" is a subentry, and reads as "analytical" under a
/// heading of "Engine".
///
/// Word's own export of the index fixture is where the shape comes from: a term, a comma, and the
/// pages; subentries indented under a parent that carries no page number of its own; and, where
/// <c>\h</c> asks for them, a line holding the letter each group begins with.
/// </remarks>
internal static class IndexBuilder
{
    /// <summary>What an XE field marks: the term, and what to show instead of a page number.</summary>
    /// <param name="Levels">
    /// The term split at its colons: "Engine:analytical" is the "analytical" subentry of "Engine".
    /// </param>
    /// <param name="Text">
    /// What <c>\t</c> asks to be shown in place of the page number — "see Difference engine", say.
    /// </param>
    /// <param name="Type">
    /// The entry type <c>\f</c> names, which an INDEX field with the same <c>\f</c> gathers on its
    /// own. A document can carry several indexes that way.
    /// </param>
    public sealed record Mark(IReadOnlyList<string> Levels, string? Text, string? Type);

    /// <summary>Reads an XE instruction, or null where it marks nothing.</summary>
    public static Mark? Read(FieldInstruction instruction)
    {
        if (instruction.Keyword != "XE") return null;
        if (instruction.Argument is not { Length: > 0 } term) return null;

        // A colon divides a term from its subentry, and a backslash before one is how a term that
        // holds a colon of its own is written.
        var levels = SplitLevels(term);
        if (levels.Count == 0) return null;

        return new Mark(levels, instruction.SwitchValue('t'), instruction.SwitchValue('f'));
    }

    private static List<string> SplitLevels(string term)
    {
        var levels = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < term.Length; i++)
        {
            if (term[i] == '\\' && i + 1 < term.Length)
            {
                current.Append(term[++i]);
                continue;
            }

            if (term[i] == ':')
            {
                levels.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(term[i]);
        }

        levels.Add(current.ToString().Trim());

        return [.. levels.Where(level => level.Length > 0)];
    }

    /// <summary>The paragraphs of the index itself, in the order they are to be laid out.</summary>
    /// <param name="marks">Every entry the document marks, with the page it was marked on.</param>
    /// <param name="instruction">The INDEX field, which says how the list is to read.</param>
    /// <param name="isStyled">Whether the document defines the style for an entry of a given depth.</param>
    public static List<BlockElement> Build(
        IEnumerable<(Mark Mark, int Page)> marks,
        FieldInstruction instruction,
        Func<string, bool> isStyled)
    {
        var type = instruction.SwitchValue('f');
        var entrySeparator = instruction.SwitchValue('e') ?? ", ";
        var pageSeparator = instruction.SwitchValue('l') ?? ", ";
        var heading = instruction.SwitchValue('h');

        var gathered = Gather(marks, type);
        var blocks = new List<BlockElement>();
        var letter = '\0';

        foreach (var (levels, pages, text) in Flatten(gathered))
        {
            if (heading is { Length: > 0 } template && levels.Count == 1)
            {
                var first = char.ToUpperInvariant(levels[0][0]);
                if (first != letter)
                {
                    letter = first;
                    blocks.Add(Paragraph("IndexHeading", Letter(template, first), 1, isStyled));
                }
            }

            // A term nothing was marked against is a parent and nothing else: it heads the
            // subentries under it and carries no page of its own.
            var trailing = text is { Length: > 0 }
                ? entrySeparator + text
                : pages.Count == 0
                    ? string.Empty
                    : entrySeparator + string.Join(pageSeparator, pages);

            blocks.Add(Paragraph(
                $"Index{levels.Count}", levels[^1] + trailing, levels.Count, isStyled));
        }

        return blocks;
    }

    /// <summary>
    /// The heading a letter group carries: the letter, put where the template's own letter is.
    /// </summary>
    private static string Letter(string template, char letter) =>
        new([.. template.Select(c => char.IsLetter(c) ? letter : c)]);

    /// <summary>An entry paragraph, indented itself where the document defines no style for it.</summary>
    private static Paragraph Paragraph(string styleId, string text, int depth, Func<string, bool> isStyled)
    {
        var properties = new ParagraphProperties { StyleId = styleId };

        if (!isStyled(styleId) && depth > 1)
            properties.IndentLeftTwips = (depth - 1) * IndentPerLevelTwips;

        var paragraph = new Paragraph { Properties = properties };

        var run = new Run();
        run.Content.Add(new TextInline(text));
        paragraph.Runs.Add(run);

        return paragraph;
    }

    /// <summary>
    /// Sorts the marks into the tree the index reads as: terms in order, each with the pages it
    /// was marked on and the subentries under it.
    /// </summary>
    private static SortedDictionary<string, Entry> Gather(
        IEnumerable<(Mark Mark, int Page)> marks, string? type)
    {
        var root = new SortedDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (mark, page) in marks)
        {
            // An index gathers the entries of its own type and leaves the rest to another index.
            if (!string.Equals(mark.Type, type, StringComparison.OrdinalIgnoreCase)) continue;

            var level = root;
            Entry? entry = null;

            foreach (var name in mark.Levels)
            {
                if (!level.TryGetValue(name, out entry))
                {
                    entry = new Entry(name);
                    level[name] = entry;
                }

                level = entry.Children;
            }

            if (entry is null) continue;

            // A page marked twice over is one page number, which is what a term marked at the top
            // and the foot of the same page comes to.
            if (mark.Text is { Length: > 0 }) entry.Text = mark.Text;
            else if (page > 0) entry.Pages.Add(page);
        }

        return root;
    }

    /// <summary>Reads the tree back in the order it is written out, deepest entries last.</summary>
    private static IEnumerable<(List<string> Levels, List<int> Pages, string? Text)> Flatten(
        SortedDictionary<string, Entry> level, List<string>? path = null)
    {
        path ??= [];

        foreach (var (_, entry) in level)
        {
            path.Add(entry.Name);

            yield return ([.. path], [.. entry.Pages.Order()], entry.Text);

            foreach (var child in Flatten(entry.Children, path)) yield return child;

            path.RemoveAt(path.Count - 1);
        }
    }

    private sealed class Entry(string name)
    {
        public string Name { get; } = name;

        public SortedSet<int> Pages { get; } = [];

        public string? Text { get; set; }

        public SortedDictionary<string, Entry> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// How far a subentry is indented where the document defines no style to say. It is only
    /// reached for a document that asks for an index without ever having had one.
    /// </summary>
    private const int IndentPerLevelTwips = 220;
}
