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
internal readonly record struct FieldInstruction(
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
        // A formula is not written like the other fields: it is an equals sign and an expression,
        // with no keyword and no spaces to divide it. Everything up to its first switch belongs to
        // the expression, brackets, commas and all.
        var trimmed = instruction.TrimStart();
        if (trimmed.StartsWith('='))
        {
            var cut = SwitchAt(trimmed);
            var expression = (cut < 0 ? trimmed : trimmed[..cut])[1..].Trim();

            return new FieldInstruction(
                "=", [expression], ReadSwitches(cut < 0 ? [] : Tokenize(trimmed[cut..]), 0, "="));
        }

        var tokens = Tokenize(instruction);
        if (tokens.Count == 0) return None;

        var keyword = tokens[0].Text.ToUpperInvariant();
        var arguments = new List<string>();
        var switches = ReadSwitches(tokens, 1, keyword, arguments);

        return new FieldInstruction(keyword, arguments, switches);
    }

    /// <summary>Where a formula's switches begin, or -1 where it has none.</summary>
    private static int SwitchAt(string instruction)
    {
        for (var i = 0; i < instruction.Length - 1; i++)
        {
            if (instruction[i] == '\\' && (char.IsLetter(instruction[i + 1]) || instruction[i + 1] is '#' or '*' or '@'))
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Reads the switches from a token onwards, putting anything that is not one — and nothing a
    /// switch has taken for itself — into the arguments.
    /// </summary>
    private static List<(char Letter, string? Value)> ReadSwitches(
        List<(string Text, bool Quoted)> tokens, int from, string keyword, List<string>? arguments = null)
    {
        var switches = new List<(char, string?)>();

        for (var i = from; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token.Quoted || token.Text.Length < 2 || token.Text[0] != '\\')
            {
                arguments?.Add(token.Text);
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

        return switches;
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
    private static bool SwitchTakesValue(char letter, string keyword)
    {
        // Formatting and pictures mean the same on every field that takes them.
        if (letter is '*' or '@' or '#') return true;

        return keyword switch
        {
            "TOC" => letter is 'o' or 't' or 'b' or 'c' or 'a' or 'f' or 'l' or 'p' or 's' or 'd' or 'z',
            "INDEX" => letter is 'b' or 'c' or 'd' or 'e' or 'f' or 'g' or 'h' or 'k' or 'l' or 'p' or 's' or 'z',
            "XE" => letter is 'f' or 'r' or 't' or 'y',
            "SEQ" => letter is 'r' or 's',
            "MERGEFIELD" => letter is 'b' or 'f',
            _ => letter is 't' or 'r' or 'f' or 's' or 'd'
        };
    }

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
                // A backslash escapes a quote and nothing else. Everything else it is written
                // before belongs to the field rather than to the quoting: an index entry writes
                // "Ratio 3\:1" for a term holding a colon, and a file name is full of them.
                if (c == '\\' && i + 1 < instruction.Length && instruction[i + 1] == '"')
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
