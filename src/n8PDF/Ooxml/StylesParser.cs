using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>A single entry from <c>styles.xml</c>.</summary>
internal sealed class Style
{
    public required string Id { get; init; }

    /// <summary>The human-readable name (<c>w:name</c>), such as "heading 1".</summary>
    public string? Name { get; init; }

    /// <summary>Style type: paragraph, character, table or numbering.</summary>
    public string Type { get; init; } = "paragraph";

    /// <summary>Id of the style this one inherits from.</summary>
    public string? BasedOn { get; init; }

    /// <summary>Style applied to the following paragraph when the user presses Enter.</summary>
    public string? Next { get; init; }

    /// <summary>True when this is the default style for its type.</summary>
    public bool IsDefault { get; init; }

    public ParagraphProperties? ParagraphProperties { get; init; }

    public RunProperties? RunProperties { get; init; }

    /// <summary>
    /// What a table style says, keyed by which part of the table it says it about. The style's
    /// own properties are the <see cref="TableConditional.WholeTable"/> entry; the rest come from
    /// its <c>w:tblStylePr</c> children. Empty for every other kind of style.
    /// </summary>
    public IReadOnlyDictionary<TableConditional, TableStyleFormat> TableFormats { get; init; } =
        new Dictionary<TableConditional, TableStyleFormat>();
}

/// <summary>
/// The parts of a table a style can describe separately, in the order Word applies them: each one
/// overrides the ones before it.
/// </summary>
/// <remarks>
/// The order was measured rather than read, from table-style-conditional-probe: every conditional
/// format in that fixture's style sets a different type size, so the size Word draws a cell at
/// names the format that reached it. Two of the answers are not the ones the specification's
/// ordering would give — banding down the columns wins over banding across the rows, and a first
/// row wins over a first column — and the corner cells beat everything.
///
/// Two orderings here are inferred rather than measured, because nothing in the fixture makes the
/// two formats meet in one cell: a last row against a last column, and the corners against each
/// other. Both follow the pattern of the pair beside them.
/// </remarks>
internal enum TableConditional
{
    WholeTable,
    Band2Horizontal,
    Band1Horizontal,
    Band2Vertical,
    Band1Vertical,
    LastColumn,
    FirstColumn,
    LastRow,
    FirstRow,
    SouthEastCell,
    SouthWestCell,
    NorthEastCell,
    NorthWestCell
}

/// <summary>Everything one conditional format of a table style has to say.</summary>
internal sealed class TableStyleFormat
{
    public ParagraphProperties? Paragraph { get; set; }

    public RunProperties? Run { get; set; }

    public TableProperties? Table { get; set; }

    public TableStyleRowProperties? Row { get; set; }

    public TableStyleCellProperties? Cell { get; set; }
}

/// <summary>The contents of <c>styles.xml</c>: document defaults plus the named styles.</summary>
internal sealed class StyleDefinitions
{
    /// <summary>Paragraph properties from <c>w:docDefaults/w:pPrDefault</c>.</summary>
    public ParagraphProperties DefaultParagraphProperties { get; set; } = new();

    /// <summary>Run properties from <c>w:docDefaults/w:rPrDefault</c>.</summary>
    public RunProperties DefaultRunProperties { get; set; } = new();

    public Dictionary<string, Style> ById { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The language the document defaults state (<c>w:lang/@w:val</c>), or null (#67).</summary>
    public string? Language { get; set; }

    /// <summary>Id of the style marked as the default paragraph style, usually "Normal".</summary>
    public string? DefaultParagraphStyleId { get; set; }

    public string? DefaultCharacterStyleId { get; set; }

    public Style? Find(string? styleId) =>
        styleId is not null && ById.TryGetValue(styleId, out var style) ? style : null;

    /// <summary>
    /// Walks a style's inheritance chain from the most general ancestor to the style itself.
    /// A malformed document can contain a cycle, so visited ids are tracked.
    /// </summary>
    public IReadOnlyList<Style> GetInheritanceChain(string? styleId)
    {
        var chain = new List<Style>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = Find(styleId);

        while (current is not null && visited.Add(current.Id))
        {
            chain.Add(current);
            current = Find(current.BasedOn);
        }

        chain.Reverse();
        return chain;
    }
}

/// <summary>
/// The theme's font scheme. Word documents reference fonts indirectly through theme slots far
/// more often than by name: a default Word paragraph asks for <c>minorHAnsi</c>, not "Calibri".
/// </summary>
internal sealed class DocumentTheme
{
    /// <summary>Latin font of the major scheme, used by headings.</summary>
    public string? MajorLatinFont { get; set; }

    /// <summary>Latin font of the minor scheme, used by body text.</summary>
    public string? MinorLatinFont { get; set; }

    /// <summary>
    /// The theme's colours, by the name the scheme gives each slot.
    /// </summary>
    /// <remarks>
    /// A shape drawn from Word's gallery names its fill and its outline by slot rather than
    /// outright, so a document's shapes come out in the wrong colours entirely without these.
    /// </remarks>
    public Dictionary<string, string> Colors { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a colour slot to what it stands for.
    /// </summary>
    /// <remarks>
    /// The four slots named for what they are used for — text and background, first and second —
    /// are the same four as the light and dark ones, under the names a drawing refers to them by.
    /// </remarks>
    public string? ResolveColor(string? slot)
    {
        if (slot is null) return null;

        var name = slot switch
        {
            "tx1" => "dk1",
            "bg1" => "lt1",
            "tx2" => "dk2",
            "bg2" => "lt2",
            _ => slot
        };

        return Colors.GetValueOrDefault(name);
    }

    /// <summary>Resolves a theme slot name to a font name.</summary>
    public string? Resolve(string? themeSlot) => themeSlot switch
    {
        null => null,
        "majorHAnsi" or "majorAscii" or "majorEastAsia" or "majorBidi" => MajorLatinFont,
        "minorHAnsi" or "minorAscii" or "minorEastAsia" or "minorBidi" => MinorLatinFont,
        _ => null
    };
}

internal static class StylesParser
{
    public static StyleDefinitions Parse(XDocument? xml)
    {
        var definitions = new StyleDefinitions();
        if (xml?.Root is null) return definitions;

        var docDefaults = xml.Root.Element(W.Main + "docDefaults");
        if (docDefaults is not null)
        {
            var pPr = docDefaults.Element(W.Main + "pPrDefault")?.Element(W.Main + "pPr");
            if (pPr is not null)
                definitions.DefaultParagraphProperties = DocumentParser.ParseParagraphProperties(pPr);

            var rPr = docDefaults.Element(W.Main + "rPrDefault")?.Element(W.Main + "rPr");
            if (rPr is not null)
                definitions.DefaultRunProperties = DocumentParser.ParseRunProperties(rPr);

            // The document's own language, for the PDF's /Lang (#67).
            definitions.Language = rPr?.Element(W.Main + "lang")?.Attr("val");
        }

        foreach (var element in xml.Root.Elements(W.Main + "style"))
        {
            var id = element.Attr("styleId");
            if (id is null) continue;

            var type = element.Attr("type") ?? "paragraph";
            var isDefault = element.BoolAttr("default") ?? false;

            var pPr = element.Element(W.Main + "pPr");
            var rPr = element.Element(W.Main + "rPr");

            var style = new Style
            {
                Id = id,
                Name = element.Element(W.Main + "name")?.Val(),
                Type = type,
                BasedOn = element.Element(W.Main + "basedOn")?.Val(),
                Next = element.Element(W.Main + "next")?.Val(),
                IsDefault = isDefault,
                ParagraphProperties = pPr is null ? null : DocumentParser.ParseParagraphProperties(pPr),
                RunProperties = rPr is null ? null : DocumentParser.ParseRunProperties(rPr),
                TableFormats = type == "table" ? ParseTableFormats(element) : new Dictionary<TableConditional, TableStyleFormat>()
            };

            definitions.ById[id] = style;

            if (!isDefault) continue;

            if (type == "paragraph") definitions.DefaultParagraphStyleId ??= id;
            else if (type == "character") definitions.DefaultCharacterStyleId ??= id;
        }

        // Word does not always mark a default paragraph style, but "Normal" is the universal
        // fallback and every real document defines it.
        definitions.DefaultParagraphStyleId ??= definitions.ById.ContainsKey("Normal") ? "Normal" : null;

        return definitions;
    }

    /// <summary>
    /// Reads what a table style says: its own properties, which describe the whole table, and one
    /// entry for each <c>w:tblStylePr</c> describing a part of it.
    /// </summary>
    private static Dictionary<TableConditional, TableStyleFormat> ParseTableFormats(XElement element)
    {
        var formats = new Dictionary<TableConditional, TableStyleFormat>
        {
            [TableConditional.WholeTable] = ReadTableFormat(element)
        };

        foreach (var conditional in element.Elements(W.Main + "tblStylePr"))
        {
            if (ConditionalOf(conditional.Attr("type")) is not { } which) continue;

            formats[which] = ReadTableFormat(conditional);
        }

        return formats;
    }

    /// <summary>
    /// Reads the five kinds of property a table style or one of its conditional formats can
    /// carry. They are the same five in both places, which is why this is written once.
    /// </summary>
    private static TableStyleFormat ReadTableFormat(XElement container) => new()
    {
        Paragraph = container.Element(W.Main + "pPr") is { } pPr
            ? DocumentParser.ParseParagraphProperties(pPr)
            : null,
        Run = container.Element(W.Main + "rPr") is { } rPr
            ? DocumentParser.ParseRunProperties(rPr)
            : null,
        Table = container.Element(W.Main + "tblPr") is { } tblPr
            ? DocumentParser.ParseTableProperties(tblPr)
            : null,
        Row = container.Element(W.Main + "trPr") is { } trPr
            ? DocumentParser.ParseTableStyleRowProperties(trPr)
            : null,
        Cell = container.Element(W.Main + "tcPr") is { } tcPr
            ? DocumentParser.ParseTableStyleCellProperties(tcPr)
            : null
    };

    private static TableConditional? ConditionalOf(string? type) => type switch
    {
        "wholeTable" => TableConditional.WholeTable,
        "band1Horz" => TableConditional.Band1Horizontal,
        "band2Horz" => TableConditional.Band2Horizontal,
        "band1Vert" => TableConditional.Band1Vertical,
        "band2Vert" => TableConditional.Band2Vertical,
        "firstCol" => TableConditional.FirstColumn,
        "lastCol" => TableConditional.LastColumn,
        "firstRow" => TableConditional.FirstRow,
        "lastRow" => TableConditional.LastRow,
        "nwCell" => TableConditional.NorthWestCell,
        "neCell" => TableConditional.NorthEastCell,
        "swCell" => TableConditional.SouthWestCell,
        "seCell" => TableConditional.SouthEastCell,
        _ => null
    };

    public static DocumentTheme ParseTheme(XDocument? xml)
    {
        var theme = new DocumentTheme();
        var fontScheme = xml?.Root?.Descendants(W.Drawing + "fontScheme").FirstOrDefault();

        theme.MajorLatinFont = fontScheme?
            .Element(W.Drawing + "majorFont")?
            .Element(W.Drawing + "latin")?
            .Attribute("typeface")?.Value;

        theme.MinorLatinFont = fontScheme?
            .Element(W.Drawing + "minorFont")?
            .Element(W.Drawing + "latin")?
            .Attribute("typeface")?.Value;

        var colorScheme = xml?.Root?.Descendants(W.Drawing + "clrScheme").FirstOrDefault();
        foreach (var slot in colorScheme?.Elements() ?? [])
        {
            // A slot holds one colour element: either a literal one or a system colour, which
            // carries what it last came to alongside the name of what it stands for.
            var value = slot.Element(W.Drawing + "srgbClr")?.Attribute("val")?.Value
                        ?? slot.Element(W.Drawing + "sysClr")?.Attribute("lastClr")?.Value;

            if (value is not null) theme.Colors[slot.Name.LocalName] = value;
        }

        return theme;
    }
}
