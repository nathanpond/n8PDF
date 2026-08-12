using System.Text;

namespace n8PDF.Layout;

/// <summary>
/// A field's instruction, split into the keyword that names it, the arguments that follow, and the
/// switches that modify it.
/// </summary>
/// <remarks>
/// An instruction reads like a command line: <c>SEQ Figure \* ARABIC</c>, or
/// <c>DOCPROPERTY "Category"</c>. Arguments may be quoted, and a quoted argument may hold spaces
/// and escaped quotes. A switch is a backslash and one character, sometimes followed by a value of
/// its own — <c>\* roman</c> takes one, <c>\c</c> does not.
/// </remarks>
public readonly record struct FieldInstruction(
    string Keyword,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<(char Letter, string? Value)> Switches)
{
    public static readonly FieldInstruction None = new(string.Empty, [], []);

    /// <summary>The value of a switch, or null where it is absent or carries none.</summary>
    public string? SwitchValue(char letter)
    {
        foreach (var (candidate, value) in Switches)
        {
            if (candidate == letter) return value;
        }

        return null;
    }

    public bool HasSwitch(char letter)
    {
        foreach (var (candidate, _) in Switches)
        {
            if (candidate == letter) return true;
        }

        return false;
    }

    /// <summary>The first argument, which is what most fields that take one are about.</summary>
    public string? Argument => Arguments.Count > 0 ? Arguments[0] : null;

    public static FieldInstruction Parse(string instruction)
    {
        var tokens = Tokenize(instruction);
        if (tokens.Count == 0) return None;

        var keyword = tokens[0].Text.ToUpperInvariant();
        var arguments = new List<string>();
        var switches = new List<(char, string?)>();

        for (var i = 1; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Quoted || token.Text.Length < 2 || token.Text[0] != '\\')
            {
                arguments.Add(token.Text);
                continue;
            }

            var letter = token.Text[1];

            // A switch written against its value, as in "\*roman", is the same as one written
            // apart from it.
            if (token.Text.Length > 2)
            {
                switches.Add((letter, token.Text[2..]));
                continue;
            }

            // A switch takes the token after it as its value unless that token is another switch.
            var next = i + 1 < tokens.Count ? tokens[i + 1] : default;
            var takesValue = next.Text is { Length: > 0 } &&
                             (next.Quoted || next.Text[0] != '\\') &&
                             SwitchTakesValue(letter, keyword);

            switches.Add((letter, takesValue ? next.Text : null));
            if (takesValue) i++;
        }

        return new FieldInstruction(keyword, arguments, switches);
    }

    /// <summary>
    /// Which switches are followed by a value. The rest are flags, and taking the next token as
    /// their value would swallow an argument that belongs to the field.
    /// </summary>
    /// <remarks>
    /// The same letter can be either, depending on the field it is written on: a table of contents
    /// reads <c>\p</c> as the text to put between an entry and its page number, while a file name
    /// reads it as "the whole path" and takes nothing. There is no rule behind that, only the list
    /// of what each field means by it.
    /// </remarks>
    private static bool SwitchTakesValue(char letter, string keyword) => letter switch
    {
        // Formatting and pictures, which mean the same on every field that takes them.
        '*' or '@' or '#' => true,

        'o' or 'b' or 'a' or 'c' or 'p' or 'l' => keyword == "TOC",

        't' or 'r' or 'f' or 's' or 'd' => true,

        _ => false
    };

    private static List<(string Text, bool Quoted)> Tokenize(string instruction)
    {
        var tokens = new List<(string, bool)>();
        var current = new StringBuilder();
        var quoted = false;
        var inQuotes = false;

        for (var i = 0; i < instruction.Length; i++)
        {
            var c = instruction[i];

            if (inQuotes)
            {
                // Inside quotes a backslash escapes the character after it, which is how a quote
                // gets into a quoted argument.
                if (c == '\\' && i + 1 < instruction.Length)
                {
                    current.Append(instruction[++i]);
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(c);
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                quoted = true;
                continue;
            }

            if (!char.IsWhiteSpace(c))
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0 || quoted) tokens.Add((current.ToString(), quoted));
            current.Clear();
            quoted = false;
        }

        if (current.Length > 0 || quoted) tokens.Add((current.ToString(), quoted));

        return tokens;
    }
}
