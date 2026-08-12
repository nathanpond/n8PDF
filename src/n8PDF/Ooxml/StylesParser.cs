using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>A single entry from <c>styles.xml</c>.</summary>
public sealed class Style
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
}

/// <summary>The contents of <c>styles.xml</c>: document defaults plus the named styles.</summary>
public sealed class StyleDefinitions
{
    /// <summary>Paragraph properties from <c>w:docDefaults/w:pPrDefault</c>.</summary>
    public ParagraphProperties DefaultParagraphProperties { get; set; } = new();

    /// <summary>Run properties from <c>w:docDefaults/w:rPrDefault</c>.</summary>
    public RunProperties DefaultRunProperties { get; set; } = new();

    public Dictionary<string, Style> ById { get; } = new(StringComparer.OrdinalIgnoreCase);

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
public sealed class DocumentTheme
{
    /// <summary>Latin font of the major scheme, used by headings.</summary>
    public string? MajorLatinFont { get; set; }

    /// <summary>Latin font of the minor scheme, used by body text.</summary>
    public string? MinorLatinFont { get; set; }

    /// <summary>Resolves a theme slot name to a font name.</summary>
    public string? Resolve(string? themeSlot) => themeSlot switch
    {
        null => null,
        "majorHAnsi" or "majorAscii" or "majorEastAsia" or "majorBidi" => MajorLatinFont,
        "minorHAnsi" or "minorAscii" or "minorEastAsia" or "minorBidi" => MinorLatinFont,
        _ => null
    };
}

public static class StylesParser
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
                RunProperties = rPr is null ? null : DocumentParser.ParseRunProperties(rPr)
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

    public static DocumentTheme ParseTheme(XDocument? xml)
    {
        var theme = new DocumentTheme();
        var fontScheme = xml?.Root?.Descendants(W.Drawing + "fontScheme").FirstOrDefault();
        if (fontScheme is null) return theme;

        theme.MajorLatinFont = fontScheme
            .Element(W.Drawing + "majorFont")?
            .Element(W.Drawing + "latin")?
            .Attribute("typeface")?.Value;

        theme.MinorLatinFont = fontScheme
            .Element(W.Drawing + "minorFont")?
            .Element(W.Drawing + "latin")?
            .Attribute("typeface")?.Value;

        return theme;
    }
}
