using System.Globalization;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// What a field needs to know beyond its own instruction: where it is in the document, what the
/// document says about itself, and the counters that run through it.
/// </summary>
internal sealed class FieldEnvironment
{
    /// <summary>The page the field is on, counting from one. Zero where it is not yet known.</summary>
    public int Page { get; set; }

    public int TotalPages { get; set; }

    /// <summary>The section the field is in, counting from one, and how many pages it has.</summary>
    public int Section { get; set; }

    public int SectionPages { get; set; }

    public DocumentProperties Properties { get; set; } = new();

    /// <summary>The name of the file being converted, where the caller knows it.</summary>
    public string? FileName { get; set; }

    /// <summary>The instant DATE and TIME report.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Now;

    /// <summary>The page a bookmark is on, counting from one, or zero where it is unknown.</summary>
    public Func<string, int> PageOfBookmark { get; set; } = _ => 0;

    /// <summary>The text a bookmark covers, or null where there is no such bookmark.</summary>
    public Func<string, string?> TextOfBookmark { get; set; } = _ => null;

    /// <summary>
    /// The counters SEQ keeps, by name. They belong to the document rather than to any one field,
    /// which is what makes a run of SEQ fields count.
    /// </summary>
    public Dictionary<string, int> Sequences { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The text of the paragraph a STYLEREF picks up. Answered by whatever is laying the document
    /// out, since which paragraph that is depends on where the pages fall.
    /// </summary>
    public Func<FieldInstruction, string?> StyleReference { get; set; } = _ => null;

    /// <summary>
    /// The cells a formula can read, which is the table it stands in. Null outside one, where a
    /// formula has only the numbers written into it to work with.
    /// </summary>
    public IFormulaCells? Cells { get; set; }

    /// <summary>
    /// The record the merge fields are filled from, where the conversion was given one. Null
    /// leaves each field showing its own name, which is what Word shows before a merge is run.
    /// </summary>
    public MailMergeRecord? Merge { get; set; }
}

/// <summary>
/// Works out what a field displays.
/// </summary>
/// <remarks>
/// A field carries both an instruction and the result Word last computed for it. Anything this
/// cannot work out is left to that cached result, which is the honest answer: showing nothing
/// would lose text the document has, and guessing would show something the document never said.
///
/// Word itself recalculates only the fields that depend on where they fall — page numbers,
/// sections, sequences — when it prints or exports, and leaves the rest showing their cached
/// results until they are updated by hand. That is why the fields fixture's reference is made with
/// its fields updated first: it is the only way to see Word's own answer for the others.
/// </remarks>
internal static class FieldEvaluator
{
    /// <summary>
    /// The fields whose value depends on where in the document they land, and which therefore
    /// cannot be worked out until the whole document has been laid out once.
    /// </summary>
    public static bool DependsOnPagination(string keyword) =>
        keyword is "PAGE" or "NUMPAGES" or "SECTION" or "SECTIONPAGES" or "PAGEREF";

    /// <summary>
    /// The text a field shows, or null where nothing here can work it out and the result Word last
    /// computed should stand.
    /// </summary>
    public static string? Evaluate(FieldInstruction instruction, FieldEnvironment environment)
    {
        var value = Value(instruction, environment);
        if (value is null) return null;

        return FieldFormats.Case(value, instruction.SwitchValue('*'));
    }

    private static string? Value(FieldInstruction instruction, FieldEnvironment environment)
    {
        var properties = environment.Properties;

        return instruction.Keyword switch
        {
            "PAGE" => Number(environment.Page, instruction),
            "NUMPAGES" => Number(environment.TotalPages, instruction),
            "SECTION" => Number(environment.Section, instruction),
            "SECTIONPAGES" => Number(environment.SectionPages, instruction),

            "PAGEREF" => instruction.Argument is { } bookmark
                ? Number(environment.PageOfBookmark(bookmark), instruction)
                : null,

            "REF" => instruction.Argument is { } named ? environment.TextOfBookmark(named) : null,

            "SEQ" => Sequence(instruction, environment),
            "IF" => Condition(instruction),
            "=" => Formula(instruction, environment),

            "MERGEFIELD" => MergeField(instruction, environment),
            "MERGEREC" => Placeholder(environment.Merge?.Number, "Merge Record #"),
            "MERGESEQ" => Placeholder(environment.Merge?.Sequence, "Merge Sequence #"),

            // The fields that move a merge from one record to the next show nothing where they
            // stand, whether or not there is a merge to move: Word draws none of them.
            "NEXT" or "NEXTIF" or "SKIPIF" => string.Empty,

            // A question the merge asks whoever runs it. Nothing here can ask, so the answer it
            // was given a default of is the best there is.
            "FILLIN" or "ASK" => instruction.SwitchValue('d'),
            "STYLEREF" => environment.StyleReference(instruction),

            "AUTHOR" => properties.Creator,
            "TITLE" => properties.Title,
            "SUBJECT" => properties.Subject,
            "KEYWORDS" => properties.Keywords,
            "COMMENTS" => properties.Description,
            "LASTSAVEDBY" => properties.LastModifiedBy,
            "FILENAME" => FileName(instruction, environment),

            "DOCPROPERTY" => instruction.Argument is { } name ? Property(name, properties) : null,

            "CREATEDATE" => Date(properties.Created, instruction),
            "SAVEDATE" => Date(properties.Modified, instruction),
            "PRINTDATE" => Date(properties.LastPrinted, instruction),
            "DATE" or "TIME" => Date(environment.Now, instruction),

            // QUOTE is its own argument, which is how a document says something a field cannot.
            "QUOTE" => instruction.Argument,

            _ => null
        };
    }

    /// <summary>
    /// What a merge field shows: the value the record holds for it, or — where there is no record,
    /// which is a document that has not been merged — the name of the field in guillemets, the way
    /// Word shows it.
    /// </summary>
    /// <remarks>
    /// The text <c>\b</c> and <c>\f</c> put before and after it are only printed where the field
    /// has something to print. Word shows them around the placeholder of an unmerged field, so a
    /// letter written "Dear «Title»," reads that way before it is merged and simply "Dear," is
    /// never what it comes to.
    /// </remarks>
    private static string? MergeField(FieldInstruction instruction, FieldEnvironment environment)
    {
        if (instruction.Argument is not { Length: > 0 } name) return null;

        var before = instruction.SwitchValue('b') ?? string.Empty;
        var after = instruction.SwitchValue('f') ?? string.Empty;

        if (environment.Merge?.Value(name) is not { } value) return $"{before}«{name}»{after}";

        return value.Length == 0 ? string.Empty : before + value + after;
    }

    /// <summary>
    /// A number the merge would have supplied, or the name of what it stands for in guillemets
    /// where there is no merge to supply it.
    /// </summary>
    private static string Placeholder(int? value, string name) =>
        value is { } number ? number.ToString(CultureInfo.InvariantCulture) : $"«{name}»";

    /// <summary>
    /// What a formula comes to, spelled the way its <c>\#</c> picture asks, or to two decimal
    /// places with the zeros at the end dropped where it names none.
    /// </summary>
    private static string? Formula(FieldInstruction instruction, FieldEnvironment environment)
    {
        if (instruction.Argument is not { Length: > 0 } expression) return null;
        if (FieldFormula.Evaluate(expression, environment.Cells) is not { } value) return null;

        return instruction.SwitchValue('#') is { Length: > 0 } picture
            ? NumericPicture.Format(value, picture)
            : FieldFormula.Format(value);
    }

    /// <summary>
    /// The text an IF field chooses: two things compared, and one of two answers.
    /// </summary>
    /// <remarks>
    /// Numbers are compared as numbers and anything else as text, without regard to case. The
    /// text on the right of an equality may hold wildcards — <c>*</c> for any run of characters
    /// and <c>?</c> for one — which is how a field asks whether something begins with a word.
    /// </remarks>
    private static string? Condition(FieldInstruction instruction)
    {
        var arguments = instruction.Arguments;
        if (arguments.Count < 3) return null;

        // The operator is the argument written out of the comparison's own characters.
        var at = -1;
        for (var i = 1; i < arguments.Count - 1 && at < 0; i++)
        {
            if (arguments[i].Length > 0 && arguments[i].All(c => c is '=' or '<' or '>')) at = i;
        }

        if (at < 0) return null;

        var left = string.Join(' ', arguments.Take(at));
        var right = arguments[at + 1];

        var holds = Compare(left, right, arguments[at]);
        if (holds is null) return null;

        var answer = holds.Value ? at + 2 : at + 3;

        // A field that names no answer for the case it landed in shows nothing, which is an
        // answer of its own rather than something that could not be worked out.
        return answer < arguments.Count ? arguments[answer] : string.Empty;
    }

    private static bool? Compare(string left, string right, string op)
    {
        if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var first) &&
            double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var second))
        {
            return op switch
            {
                "=" => first == second,
                "<>" => first != second,
                "<" => first < second,
                ">" => first > second,
                "<=" => first <= second,
                ">=" => first >= second,
                _ => null
            };
        }

        var same = Matches(left, right);

        return op switch
        {
            "=" => same,
            "<>" => !same,
            "<" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) < 0,
            ">" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) > 0,
            "<=" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0,
            ">=" => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) >= 0,
            _ => null
        };
    }

    /// <summary>Whether text answers a pattern, in which * stands for any run and ? for one.</summary>
    private static bool Matches(string text, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(text, pattern, StringComparison.OrdinalIgnoreCase);

        var expression = new System.Text.StringBuilder("^");

        foreach (var c in pattern)
        {
            expression.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => System.Text.RegularExpressions.Regex.Escape(c.ToString())
            });
        }

        expression.Append('$');

        return System.Text.RegularExpressions.Regex.IsMatch(
            text, expression.ToString(), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// A number, spelled the way the field's <c>\*</c> switch asks. Zero means the value is not
    /// known — a bookmark nothing points at, or a page number before pagination — and leaves the
    /// cached result standing rather than showing a nought.
    /// </summary>
    private static string? Number(int value, FieldInstruction instruction)
    {
        if (value <= 0) return null;

        return FieldFormats.Number(value, instruction.SwitchValue('*'))
               ?? value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? Date(DateTimeOffset? value, FieldInstruction instruction) =>
        value is { } instant ? FieldDate.Format(instant, instruction.SwitchValue('@')) : null;

    private static string? Property(string name, DocumentProperties properties)
    {
        if (properties.Custom.TryGetValue(name, out var custom)) return custom;

        // The standard properties can also be reached by name, which is how a document refers to
        // one that has no field of its own.
        return name.ToUpperInvariant() switch
        {
            "TITLE" => properties.Title,
            "SUBJECT" => properties.Subject,
            "AUTHOR" or "CREATOR" => properties.Creator,
            "KEYWORDS" => properties.Keywords,
            "COMMENTS" or "DESCRIPTION" => properties.Description,
            "LASTSAVEDBY" => properties.LastModifiedBy,
            _ => null
        };
    }

    private static string? FileName(FieldInstruction instruction, FieldEnvironment environment)
    {
        if (environment.FileName is not { Length: > 0 } path) return null;

        // The \p switch asks for the whole path rather than the name alone.
        return instruction.HasSwitch('p') ? path : Path.GetFileName(path);
    }

    /// <summary>
    /// The next value of a named counter. <c>\r</c> restarts it at a given number, <c>\c</c> shows
    /// what it stands at without advancing it, and <c>\n</c> — the default — advances it.
    /// </summary>
    private static string? Sequence(FieldInstruction instruction, FieldEnvironment environment)
    {
        if (instruction.Argument is not { Length: > 0 } name) return null;

        var counters = environment.Sequences;
        var current = counters.GetValueOrDefault(name);

        if (instruction.SwitchValue('r') is { } reset &&
            int.TryParse(reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var at))
        {
            current = at;
        }
        else if (!instruction.HasSwitch('c'))
        {
            current++;
        }

        counters[name] = current;

        return FieldFormats.Number(current, instruction.SwitchValue('*'))
               ?? current.ToString(CultureInfo.InvariantCulture);
    }
}
