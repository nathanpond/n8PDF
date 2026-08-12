using System.Globalization;
using System.Text;

namespace n8PDF.Layout;

/// <summary>
/// Renders a date the way a field's <c>\@</c> picture asks for it.
/// </summary>
/// <remarks>
/// Word's picture strings and .NET's custom date formats agree about most of what they spell —
/// <c>d</c>, <c>MMMM</c>, <c>yyyy</c>, <c>HH</c>, <c>mm</c> all mean the same in both — so most of
/// the work here is telling a token apart from the literal text around it. The two disagree about
/// the morning and afternoon marker, which Word writes as <c>AM/PM</c> or <c>am/pm</c>, and about
/// what an unrecognised character means: .NET reads a bare letter as a token and would turn the
/// "of" in "d 'of' MMMM" into a day and a fraction of a second, so anything that is not a token is
/// quoted before being handed over.
/// </remarks>
public static class FieldDate
{
    /// <summary>
    /// What a date field shows when it names no picture of its own. Word takes this from the
    /// reader's system settings; this is what it produced on the machine the reference PDFs were
    /// made on, which is the only value there is anything to compare against.
    /// </summary>
    /// <remarks>Written as a picture rather than as a .NET format, since that is what it is.</remarks>
    public const string DefaultPicture = "M/d/yy h:mm:ss AM/PM";

    /// <summary>The tokens a picture can hold, longest first so that MMMM wins over MM.</summary>
    private static readonly string[] Tokens =
    [
        "dddd", "ddd", "dd", "d",
        "MMMM", "MMM", "MM", "M",
        "yyyy", "yy", "y",
        "HH", "H", "hh", "h",
        "mm", "m", "ss", "s",
        "AM/PM", "am/pm", "A/P", "a/p"
    ];

    public static string Format(DateTimeOffset value, string? picture)
    {
        var local = value.ToLocalTime();
        var format = Translate(picture is { Length: > 0 } ? picture : DefaultPicture);

        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Turns a Word picture into the .NET format that means the same thing.</summary>
    private static string Translate(string picture)
    {
        var result = new StringBuilder(picture.Length + 8);
        var index = 0;

        while (index < picture.Length)
        {
            // Text a picture quotes for itself stays as it is, quoted the way .NET quotes.
            if (picture[index] == '\'')
            {
                var end = picture.IndexOf('\'', index + 1);
                var literal = end < 0 ? picture[(index + 1)..] : picture[(index + 1)..end];

                Quote(result, literal);
                index = end < 0 ? picture.Length : end + 1;
                continue;
            }

            if (Match(picture, index) is { } token)
            {
                result.Append(token switch
                {
                    "AM/PM" or "am/pm" => "tt",
                    "A/P" or "a/p" => "t",

                    // Word's y is the year as written; .NET's single y is the year within the
                    // century without a leading zero, which is the same thing for every year a
                    // document is likely to carry.
                    _ => token
                });

                index += token.Length;
                continue;
            }

            // Everything else is literal, including the punctuation that a format would otherwise
            // read as a separator taken from the reader's own settings.
            var start = index;
            while (index < picture.Length && picture[index] != '\'' && Match(picture, index) is null) index++;

            Quote(result, picture[start..index]);
        }

        return result.ToString();
    }

    private static string? Match(string picture, int index)
    {
        foreach (var token in Tokens)
        {
            if (index + token.Length <= picture.Length &&
                string.CompareOrdinal(picture, index, token, 0, token.Length) == 0)
            {
                return token;
            }
        }

        return null;
    }

    private static void Quote(StringBuilder result, string literal)
    {
        foreach (var c in literal)
        {
            // A quote inside literal text has to be escaped rather than quoted, or it would close
            // the run it is inside.
            if (c == '"') result.Append("\\\"");
            else result.Append('"').Append(c).Append('"');
        }
    }
}
