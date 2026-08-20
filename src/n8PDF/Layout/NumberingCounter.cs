using System.Globalization;
using System.Text;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Tracks where each list has counted to and produces the label for the next item.
/// </summary>
/// <remarks>
/// Counting is stateful and order-dependent: a list item's number depends on every item before it,
/// and advancing one level restarts the levels beneath it. Counters are kept per list rather than
/// per definition, because two lists sharing one definition still number independently.
/// </remarks>
internal sealed class NumberingCounter(NumberingDefinitions definitions)
{
    private readonly Dictionary<int, Dictionary<int, int>> _counters = [];

    /// <summary>
    /// Advances the given level of the given list and returns the label to print, or null when
    /// the level contributes none.
    /// </summary>
    public string? Advance(int numId, int level)
    {
        var definition = definitions.GetLevel(numId, level);
        if (definition is null) return null;

        if (!_counters.TryGetValue(numId, out var counters))
        {
            counters = [];
            _counters[numId] = counters;
        }

        counters[level] = counters.TryGetValue(level, out var current)
            ? current + 1
            : definitions.GetStart(numId, level);

        // Advancing a level restarts everything beneath it, which is what makes "1.1, 1.2, 2.1"
        // work. A level declaring lvlRestart="0" opts out and keeps counting across its parents.
        foreach (var deeper in counters.Keys.Where(l => l > level).ToList())
        {
            var deeperDefinition = definitions.GetLevel(numId, deeper);
            if (deeperDefinition?.RestartAfterLevel == 0) continue;

            counters.Remove(deeper);
        }

        if (definition.Format == NumberFormat.None) return null;

        return Format(definition, numId, level, counters);
    }

    /// <summary>
    /// Fills a level's template. <c>%1</c> to <c>%9</c> stand for the counters of levels one to
    /// nine, so a level-two template of "%1.%2." produces "2.3." — which is why the counters of
    /// the levels above have to be readable here and not just the current one.
    /// </summary>
    private string Format(NumberingLevel definition, int numId, int level, Dictionary<int, int> counters)
    {
        if (definition.Format == NumberFormat.Bullet) return definition.LevelText;

        var template = definition.LevelText;
        if (template.Length == 0) return string.Empty;

        var result = new StringBuilder(template.Length + 8);

        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '%' || i + 1 >= template.Length || !char.IsDigit(template[i + 1]))
            {
                result.Append(template[i]);
                continue;
            }

            var referenced = template[i + 1] - '1';
            i++;

            if (referenced < 0 || referenced > level)
            {
                // A template referring to a level below the one being printed has nothing to say.
                continue;
            }

            var value = counters.TryGetValue(referenced, out var counted)
                ? counted
                : definitions.GetStart(numId, referenced);

            var format = referenced == level
                ? definition.Format
                : definitions.GetLevel(numId, referenced)?.Format ?? NumberFormat.Decimal;

            result.Append(NumberFormatter.Format(value, format));
        }

        return result.ToString();
    }
}

/// <summary>Renders a counter value in one of the numbering formats.</summary>
internal static class NumberFormatter
{
    public static string Format(int value, NumberFormat format) => format switch
    {
        NumberFormat.DecimalZero => value.ToString("00", CultureInfo.InvariantCulture),
        NumberFormat.LowerLetter => Alphabetic(value, 'a'),
        NumberFormat.UpperLetter => Alphabetic(value, 'A'),
        NumberFormat.LowerRoman => Roman(value).ToLowerInvariant(),
        NumberFormat.UpperRoman => Roman(value),
        NumberFormat.None => string.Empty,
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// Spreadsheet-style lettering: a, b … z, aa, bb. Word repeats the letter rather than
    /// carrying like a base-26 number, so the 27th item is "aa" and not "ab".
    /// </summary>
    private static string Alphabetic(int value, char first)
    {
        if (value < 1) return string.Empty;

        var index = (value - 1) % 26;
        var repeats = (value - 1) / 26 + 1;

        return new string((char)(first + index), repeats);
    }

    private static readonly (int Value, string Symbol)[] RomanNumerals =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    ];

    private static string Roman(int value)
    {
        if (value < 1 || value > 3999) return value.ToString(CultureInfo.InvariantCulture);

        var result = new StringBuilder();
        foreach (var (numeral, symbol) in RomanNumerals)
        {
            while (value >= numeral)
            {
                result.Append(symbol);
                value -= numeral;
            }
        }

        return result.ToString();
    }
}
