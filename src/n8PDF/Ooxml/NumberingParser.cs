using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>How a list level's number is written.</summary>
public enum NumberFormat
{
    Decimal,
    DecimalZero,
    LowerLetter,
    UpperLetter,
    LowerRoman,
    UpperRoman,

    /// <summary>A fixed character rather than a counter.</summary>
    Bullet,

    /// <summary>The level contributes no label at all.</summary>
    None
}

/// <summary>What follows a list label before the paragraph's own text.</summary>
public enum NumberSuffix
{
    Tab,
    Space,
    Nothing
}

/// <summary>One level of a list definition.</summary>
public sealed class NumberingLevel
{
    public required int Level { get; init; }

    /// <summary>The value this level counts from.</summary>
    public int Start { get; init; } = 1;

    public NumberFormat Format { get; init; } = NumberFormat.Decimal;

    /// <summary>
    /// The label template. Placeholders <c>%1</c> to <c>%9</c> stand for the counter at that
    /// level, so a template of "%1.%2." on level two produces "1.3." and so on. For a bullet the
    /// template is the bullet character itself.
    /// </summary>
    public string LevelText { get; init; } = string.Empty;

    public NumberSuffix Suffix { get; init; } = NumberSuffix.Tab;

    /// <summary>Paragraph properties the level contributes, chiefly its indents.</summary>
    public ParagraphProperties? ParagraphProperties { get; init; }

    /// <summary>Run properties for the label, which is how bullets get their symbol font.</summary>
    public RunProperties? RunProperties { get; init; }

    /// <summary>
    /// The level whose advance restarts this one's counter. Null means the level above it, which
    /// is the default; zero means it never restarts.
    /// </summary>
    public int? RestartAfterLevel { get; init; }
}

/// <summary>A list definition, shared by every list that references it.</summary>
public sealed class AbstractNumbering
{
    public required int Id { get; init; }

    public Dictionary<int, NumberingLevel> Levels { get; } = [];
}

/// <summary>
/// A list in the document, referencing a definition and optionally overriding parts of it.
/// </summary>
/// <remarks>
/// Two lists can share one definition and still count independently, which is why paragraphs
/// reference this rather than the definition directly.
/// </remarks>
public sealed class NumberingInstance
{
    public required int NumId { get; init; }

    public required int AbstractNumId { get; init; }

    public Dictionary<int, NumberingLevel> Overrides { get; } = [];

    /// <summary>Levels whose counter this instance restarts, with the value to restart at.</summary>
    public Dictionary<int, int> StartOverrides { get; } = [];
}

/// <summary>The contents of <c>numbering.xml</c>.</summary>
public sealed class NumberingDefinitions
{
    public Dictionary<int, AbstractNumbering> Abstract { get; } = [];

    public Dictionary<int, NumberingInstance> Instances { get; } = [];

    public bool IsEmpty => Instances.Count == 0 && Abstract.Count == 0;

    /// <summary>Resolves a list and level to its definition, following the instance's overrides.</summary>
    public NumberingLevel? GetLevel(int numId, int level)
    {
        if (!Instances.TryGetValue(numId, out var instance)) return null;
        if (instance.Overrides.TryGetValue(level, out var overridden)) return overridden;

        return Abstract.TryGetValue(instance.AbstractNumId, out var abstractNumbering) &&
               abstractNumbering.Levels.TryGetValue(level, out var defined)
            ? defined
            : null;
    }

    /// <summary>The value a level counts from, honouring any per-instance override.</summary>
    public int GetStart(int numId, int level)
    {
        if (Instances.TryGetValue(numId, out var instance) &&
            instance.StartOverrides.TryGetValue(level, out var start))
        {
            return start;
        }

        return GetLevel(numId, level)?.Start ?? 1;
    }
}

public static class NumberingParser
{
    public static NumberingDefinitions Parse(XDocument? xml)
    {
        var definitions = new NumberingDefinitions();
        if (xml?.Root is null) return definitions;

        foreach (var element in xml.Root.Elements(W.Main + "abstractNum"))
        {
            var id = element.IntAttr("abstractNumId");
            if (id is null) continue;

            var abstractNumbering = new AbstractNumbering { Id = id.Value };

            foreach (var levelElement in element.Elements(W.Main + "lvl"))
            {
                var level = ParseLevel(levelElement);
                if (level is not null) abstractNumbering.Levels[level.Level] = level;
            }

            definitions.Abstract[id.Value] = abstractNumbering;
        }

        foreach (var element in xml.Root.Elements(W.Main + "num"))
        {
            var numId = element.IntAttr("numId");
            var abstractId = element.Element(W.Main + "abstractNumId")?.IntVal();
            if (numId is null || abstractId is null) continue;

            var instance = new NumberingInstance { NumId = numId.Value, AbstractNumId = abstractId.Value };

            foreach (var overrideElement in element.Elements(W.Main + "lvlOverride"))
            {
                var levelIndex = overrideElement.IntAttr("ilvl");
                if (levelIndex is null) continue;

                if (overrideElement.Element(W.Main + "startOverride")?.IntVal() is { } start)
                    instance.StartOverrides[levelIndex.Value] = start;

                var replacement = overrideElement.Element(W.Main + "lvl");
                if (replacement is not null && ParseLevel(replacement) is { } level)
                    instance.Overrides[levelIndex.Value] = level;
            }

            definitions.Instances[numId.Value] = instance;
        }

        return definitions;
    }

    private static NumberingLevel? ParseLevel(XElement element)
    {
        var index = element.IntAttr("ilvl");
        if (index is null) return null;

        var pPr = element.Element(W.Main + "pPr");
        var rPr = element.Element(W.Main + "rPr");

        return new NumberingLevel
        {
            Level = index.Value,
            Start = element.Element(W.Main + "start")?.IntVal() ?? 1,
            Format = ParseFormat(element.Element(W.Main + "numFmt")?.Val()),
            LevelText = element.Element(W.Main + "lvlText")?.Val() ?? string.Empty,
            Suffix = element.Element(W.Main + "suff")?.Val() switch
            {
                "space" => NumberSuffix.Space,
                "nothing" => NumberSuffix.Nothing,
                _ => NumberSuffix.Tab
            },
            RestartAfterLevel = element.Element(W.Main + "lvlRestart")?.IntVal(),
            ParagraphProperties = pPr is null ? null : DocumentParser.ParseParagraphProperties(pPr),
            RunProperties = rPr is null ? null : DocumentParser.ParseRunProperties(rPr)
        };
    }

    /// <summary>
    /// Reads a <c>w:numFmt</c> value. Public because note numbering uses the same vocabulary as
    /// list numbering, from a different part of the document.
    /// </summary>
    public static NumberFormat ParseNumberFormat(string? value) => ParseFormat(value);

    private static NumberFormat ParseFormat(string? value) => value switch
    {
        "bullet" => NumberFormat.Bullet,
        "none" => NumberFormat.None,
        "decimalZero" => NumberFormat.DecimalZero,
        "lowerLetter" => NumberFormat.LowerLetter,
        "upperLetter" => NumberFormat.UpperLetter,
        "lowerRoman" => NumberFormat.LowerRoman,
        "upperRoman" => NumberFormat.UpperRoman,
        _ => NumberFormat.Decimal
    };
}
