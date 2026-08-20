using System.Globalization;
using System.Text;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// The formatting a field's <c>\*</c> switch asks for: how to spell a number, and what case to put
/// a result in.
/// </summary>
/// <remarks>
/// Every spelling here is read from Word's own export of the fields fixture, where the same page
/// number is written out by each switch in turn.
/// </remarks>
internal static class FieldFormats
{
    /// <summary>
    /// Applies a <c>\*</c> switch to a number. Returns null where the switch names something else
    /// — a case to put text in, or a format this does not know — so that the caller can fall back
    /// to plain digits.
    /// </summary>
    public static string? Number(int value, string? format)
    {
        if (format is null) return null;

        return format switch
        {
            "arabic" or "ARABIC" or "Arabic" => value.ToString(CultureInfo.InvariantCulture),
            "roman" => NumberFormatter.Format(value, NumberFormat.LowerRoman),
            "ROMAN" or "Roman" => NumberFormatter.Format(value, NumberFormat.UpperRoman),
            "alphabetic" => NumberFormatter.Format(value, NumberFormat.LowerLetter),
            "ALPHABETIC" or "Alphabetic" => NumberFormatter.Format(value, NumberFormat.UpperLetter),
            "Ordinal" or "ordinal" or "ORDINAL" => Ordinal(value),
            "CardText" or "cardtext" or "CARDTEXT" => Words(value),
            "OrdText" or "ordtext" or "ORDTEXT" => OrdinalWords(value),
            "Hex" or "hex" or "HEX" => value.ToString("X", CultureInfo.InvariantCulture),
            "DollarText" or "dollartext" or "DOLLARTEXT" => $"{Words(value)} and 00/100",
            _ => null
        };
    }

    /// <summary>
    /// Applies a <c>\*</c> switch that names a case rather than a number format. Anything else is
    /// left alone, including MERGEFORMAT and CHARFORMAT, which are about keeping the formatting
    /// the field already had rather than changing its text.
    /// </summary>
    public static string Case(string text, string? format) => format switch
    {
        "Upper" or "UPPER" or "upper" => text.ToUpperInvariant(),
        "Lower" or "LOWER" or "lower" => text.ToLowerInvariant(),
        "FirstCap" or "firstcap" or "FIRSTCAP" => FirstCap(text),
        "Caps" or "caps" or "CAPS" => Caps(text),
        _ => text
    };

    private static string FirstCap(string text)
    {
        var lowered = text.ToLowerInvariant();

        for (var i = 0; i < lowered.Length; i++)
        {
            if (!char.IsLetter(lowered[i])) continue;

            return string.Concat(lowered[..i], char.ToUpperInvariant(lowered[i]), lowered[(i + 1)..]);
        }

        return lowered;
    }

    /// <summary>Every word capitalised, which is what Word means by Caps.</summary>
    private static string Caps(string text)
    {
        var result = new StringBuilder(text.Length);
        var atWordStart = true;

        foreach (var c in text)
        {
            result.Append(atWordStart ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            atWordStart = !char.IsLetter(c) && c != '\'';
        }

        return result.ToString();
    }

    private static string Ordinal(int value)
    {
        var suffix = (value % 100) is >= 11 and <= 13
            ? "th"
            : (value % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static readonly string[] Units =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    ];

    /// <summary>
    /// A number spelled out, as CardText asks for: "one", "twenty-one", "one hundred twenty-one".
    /// </summary>
    private static string Words(int value)
    {
        if (value < 0) return "minus " + Words(-value);
        if (value < 20) return Units[value];

        if (value < 100)
        {
            var tens = Tens[value / 10];
            return value % 10 == 0 ? tens : $"{tens}-{Units[value % 10]}";
        }

        if (value < 1000)
        {
            var hundreds = $"{Units[value / 100]} hundred";
            return value % 100 == 0 ? hundreds : $"{hundreds} {Words(value % 100)}";
        }

        if (value < 1_000_000)
        {
            var thousands = $"{Words(value / 1000)} thousand";
            return value % 1000 == 0 ? thousands : $"{thousands} {Words(value % 1000)}";
        }

        var millions = $"{Words(value / 1_000_000)} million";
        return value % 1_000_000 == 0 ? millions : $"{millions} {Words(value % 1_000_000)}";
    }

    /// <summary>
    /// The same spelled out as a position rather than a count, which is OrdText: "first",
    /// "twenty-first", "one hundred first".
    /// </summary>
    private static string OrdinalWords(int value)
    {
        var words = Words(value);

        var space = words.LastIndexOfAny([' ', '-']);
        var lead = space < 0 ? string.Empty : words[..(space + 1)];
        var last = space < 0 ? words : words[(space + 1)..];

        return lead + last switch
        {
            "zero" => "zeroth",
            "one" => "first",
            "two" => "second",
            "three" => "third",
            "five" => "fifth",
            "eight" => "eighth",
            "nine" => "ninth",
            "twelve" => "twelfth",
            _ => last.EndsWith('y') ? last[..^1] + "ieth" : last + "th"
        };
    }
}
