using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Turns a TOC field into the paragraphs that stand for it.
/// </summary>
/// <remarks>
/// A table of contents is a field whose answer is a run of paragraphs rather than a few words, one
/// to each heading the document holds, and Word writes them in styles named for the level they
/// stand at: TOC1 for the topmost, TOC2 under it, and so on down. Each entry is the heading's own
/// text, a tab out to a right stop with a dotted leader, and the page the heading is on — which is
/// why a document with one in it is laid out twice.
///
/// Which paragraphs it gathers is what the instruction says: <c>\o "1-3"</c> takes the ones whose
/// outline level falls in that range, and <c>\t "Style,Level"</c> names styles to take as well,
/// whatever level they stand at. <c>\n</c> leaves the page numbers off.
/// </remarks>
internal static class TableOfContentsBuilder
{
    /// <summary>A heading the table gathers: what it says, how deep it is, and where it is.</summary>
    public sealed record Entry(int Level, string Text, int Page);

    /// <summary>
    /// What a TOC instruction gathers: a range of outline levels, and styles named outright.
    /// </summary>
    public readonly record struct Scope(int From, int To, IReadOnlyDictionary<string, int> Styles)
    {
        /// <summary>The level a paragraph enters the table at, or null where it does not.</summary>
        public int? LevelOf(int? outlineLevel, string? styleId)
        {
            if (styleId is not null && Styles.TryGetValue(styleId, out var named)) return named;

            // Outline levels count from zero and the instruction counts from one.
            if (outlineLevel is not { } outline) return null;

            var level = outline + 1;
            return level >= From && level <= To ? level : null;
        }
    }

    /// <summary>Reads what an instruction gathers, defaulting to the three levels Word defaults to.</summary>
    public static Scope ScopeOf(FieldInstruction instruction)
    {
        var from = 1;
        var to = 3;

        if (instruction.SwitchValue('o') is { Length: > 0 } levels)
        {
            var parts = levels.Split('-', 2);

            if (int.TryParse(parts[0], out var first)) from = first;
            to = parts.Length > 1 && int.TryParse(parts[1], out var last) ? last : from;
        }
        else if (instruction.HasSwitch('t') && !instruction.HasSwitch('o'))
        {
            // Named styles alone: nothing is taken by its outline level.
            from = 1;
            to = 0;
        }

        var styles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // "Style,Level;Style,Level" — a style with no level of its own enters at the first.
        if (instruction.SwitchValue('t') is { Length: > 0 } named)
        {
            foreach (var pair in named.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(',', 2);
                var style = parts[0].Trim();
                if (style.Length == 0) continue;

                styles[style] = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var level) ? level : 1;
            }
        }

        return new Scope(from, to, styles);
    }

    /// <summary>Whether the field asks for page numbers, which <c>\n</c> is what turns off.</summary>
    public static bool ShowsPageNumbers(FieldInstruction instruction) => !instruction.HasSwitch('n');

    /// <summary>
    /// The paragraph standing for one entry: the heading's text, a tab, and its page number.
    /// </summary>
    /// <param name="styled">
    /// Whether the document defines the TOC style for this level. Where it does not, the entry
    /// carries the indent and the leader itself — a table of contents with neither would run its
    /// page numbers straight up against its headings.
    /// </param>
    public static Paragraph Build(Entry entry, bool showPageNumber, bool styled, int tabPositionTwips)
    {
        var properties = new ParagraphProperties { StyleId = $"TOC{entry.Level}" };

        if (!styled)
        {
            properties.IndentLeftTwips = (entry.Level - 1) * IndentPerLevelTwips;
            properties.TabStops.Add(new TabStop(tabPositionTwips, TabAlignment.Right, TabLeader.Dot));
        }

        var paragraph = new Paragraph { Properties = properties };

        var text = new Run();
        text.Content.Add(new TextInline(entry.Text));
        paragraph.Runs.Add(text);

        if (!showPageNumber) return paragraph;

        var number = new Run();
        number.Content.Add(new TabInline());

        // A page of zero means the pass that would have said which page it is has not run yet.
        if (entry.Page > 0)
        {
            number.Content.Add(new TextInline(
                entry.Page.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        paragraph.Runs.Add(number);

        return paragraph;
    }

    /// <summary>
    /// How far each level is indented where the document defines no style to say. Word's own
    /// built-in TOC styles step by about this much, and it is only reached for a document that
    /// asks for a table of contents without ever having had one.
    /// </summary>
    private const int IndentPerLevelTwips = 220;
}
