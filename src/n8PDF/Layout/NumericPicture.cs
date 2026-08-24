using System.Globalization;
using System.Text;

namespace n8PDF.Layout;

/// <summary>
/// Spells a number the way a field's <c>\#</c> picture asks for it.
/// </summary>
/// <remarks>
/// A picture is a pattern of places for digits with whatever else is to be printed around them.
/// The two kinds of place are not the same: <c>0</c> shows a nought where there is no digit to
/// put in it and <c>#</c> shows a space. Word's export of the formulas fixture is where that comes
/// from — a five against <c>$#,##0.00</c> comes out as "$   5.00", with the three empty places
/// standing open rather than closed up.
///
/// A picture can hold up to three patterns, divided by semicolons: one for a positive number, one
/// for a negative one, and one for nought. The negative pattern is given the number without its
/// sign, which is how "(5.00)" is written for minus five.
/// </remarks>
internal static class NumericPicture
{
    public static string Format(double value, string picture)
    {
        var sections = Split(picture);

        var pattern = value switch
        {
            < 0 when sections.Count > 1 => sections[1],
            0 when sections.Count > 2 => sections[2],
            _ => sections[0]
        };

        // The negative pattern spells the sign itself, so the number is given without one.
        if (value < 0 && sections.Count > 1) value = -value;

        return Apply(value, pattern);
    }

    /// <summary>Divides a picture at its semicolons, which are not part of any pattern.</summary>
    private static List<string> Split(string picture)
    {
        var sections = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var c in picture)
        {
            if (c == '\'') quoted = !quoted;

            if (c == ';' && !quoted)
            {
                sections.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        sections.Add(current.ToString());

        return sections;
    }

    private static string Apply(double value, string pattern)
    {
        var (integerPattern, decimalPattern) = Divide(pattern);

        // A double carries about fifteen significant digits; Math.Round throws past fifteen
        // places, and a picture's place count is document-stated and unbounded (#202).
        var places = Math.Min(decimalPattern.Count(c => c is '0' or '#'), 15);
        var rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);

        var digits = Math.Abs(rounded).ToString(
            places > 0 ? "F" + places.ToString(CultureInfo.InvariantCulture) : "F0",
            CultureInfo.InvariantCulture);

        var point = digits.IndexOf('.');
        var whole = point < 0 ? digits : digits[..point];
        var fraction = point < 0 ? string.Empty : digits[(point + 1)..];

        // A pattern with a comma in it groups every three digits, however many places it names.
        if (integerPattern.Contains(','))
            whole = long.TryParse(whole, out var grouped)
                ? grouped.ToString("#,##0", CultureInfo.InvariantCulture)
                : whole;

        var result = new StringBuilder();
        result.Append(Integer(integerPattern, whole, rounded < 0));

        if (decimalPattern.Length > 0) result.Append(Decimals(decimalPattern, fraction));

        return result.ToString();
    }

    /// <summary>Divides a pattern at the point that separates its whole part from its places.</summary>
    private static (string Integer, string Decimal) Divide(string pattern)
    {
        var quoted = false;

        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\'') quoted = !quoted;
            if (pattern[i] == '.' && !quoted) return (pattern[..i], pattern[(i + 1)..]);
        }

        return (pattern, string.Empty);
    }

    /// <summary>
    /// Fills the whole part in from the right, so that the digits end where the pattern ends and
    /// anything the pattern has room for and the number does not stands open or closed as its
    /// place asks. Digits the pattern has no room for are not lost: they run out to the left.
    /// </summary>
    private static string Integer(string pattern, string digits, bool negative)
    {
        var result = new List<char>();
        var next = digits.Length - 1;

        for (var i = pattern.Length - 1; i >= 0; i--)
        {
            var c = pattern[i];

            switch (c)
            {
                case '0' or '#':
                {
                    if (next >= 0)
                    {
                        // Everything left over goes into the first place the pattern gives, so
                        // that a pattern of two places still shows a number of seven digits.
                        var remaining = i == FirstPlace(pattern) ? digits[..(next + 1)] : digits[next].ToString();

                        result.AddRange(remaining.Reverse());
                        next -= remaining.Length;
                    }
                    else
                    {
                        result.Add(c == '0' ? '0' : ' ');
                    }

                    break;
                }

                case ',':
                    // A separator between places is part of the grouping the digits already
                    // carry, so it is only shown where there are digits either side of it.
                    break;

                case '\'':
                    break;

                default:
                    result.Add(c);
                    break;
            }
        }

        if (negative && !pattern.Contains('-')) result.Add('-');

        result.Reverse();

        return new string([.. result]);
    }

    private static int FirstPlace(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] is '0' or '#') return i;
        }

        return -1;
    }

    /// <summary>Fills the places after the point in from the left, which is the order they read in.</summary>
    private static string Decimals(string pattern, string digits)
    {
        var result = new StringBuilder(".");
        var next = 0;

        foreach (var c in pattern)
        {
            switch (c)
            {
                case '0' or '#':
                    if (next < digits.Length) result.Append(digits[next++]);
                    else result.Append(c == '0' ? '0' : ' ');

                    break;

                case '\'':
                    break;

                default:
                    result.Append(c);
                    break;
            }
        }

        return result.ToString();
    }
}
