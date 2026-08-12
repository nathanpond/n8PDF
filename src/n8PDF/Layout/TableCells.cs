using System.Globalization;
using System.Text;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// The cells of a table as a formula in one of them reads them.
/// </summary>
/// <remarks>
/// Cells are named the way a spreadsheet names them: a letter for the column and a number for the
/// row, counting from A and from one. A direction — ABOVE, BELOW, LEFT, RIGHT — stands for the
/// cells running that way from the one the formula is in.
///
/// A cell holding a formula of its own is worked out rather than read, so that a column of totals
/// can be totalled. The depth it will do that to is limited, since nothing stops a document
/// asking a cell for its own value.
/// </remarks>
public sealed class TableCells(Table table, int row, int column) : IFormulaCells
{
    private const int MaximumDepth = 8;

    private int _depth;

    public IReadOnlyList<double> InDirection(string direction)
    {
        var (dr, dc) = direction.ToUpperInvariant() switch
        {
            "ABOVE" => (-1, 0),
            "BELOW" => (1, 0),
            "LEFT" => (0, -1),
            _ => (0, 1)
        };

        var values = new List<double>();

        // The reading stops at the first cell that is not a number: a column of 10, "n/a" and 3
        // sums to 3 from below it rather than to 13, which is what Word's own export shows.
        for (int r = row + dr, c = column + dc; ; r += dr, c += dc)
        {
            if (ValueAt(r, c) is not { } value) break;

            values.Add(value);
        }

        // They are read outwards from the cell, and reading them back the way the table is
        // written keeps a sum the same and a first value the one nearest the top.
        values.Reverse();

        return values;
    }

    public IReadOnlyList<double> InRange(string from, string to)
    {
        if (Position(from) is not { } start || Position(to) is not { } end) return [];

        var values = new List<double>();

        for (var r = Math.Min(start.Row, end.Row); r <= Math.Max(start.Row, end.Row); r++)
        for (var c = Math.Min(start.Column, end.Column); c <= Math.Max(start.Column, end.Column); c++)
        {
            // Unlike a direction this reads the whole rectangle, passing over the cells that hold
            // no number rather than stopping at them.
            if (ValueAt(r, c) is { } value) values.Add(value);
        }

        return values;
    }

    public double? Cell(string reference) =>
        Position(reference) is { } at ? ValueAt(at.Row, at.Column) : null;

    /// <summary>The cell a name stands for, counting from zero.</summary>
    private static (int Row, int Column)? Position(string reference)
    {
        var index = 0;
        var column = 0;

        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = column * 26 + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        if (column == 0 || index == reference.Length) return null;

        return int.TryParse(reference[index..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) &&
               row > 0
            ? (row - 1, column - 1)
            : null;
    }

    /// <summary>What a cell comes to, whether it holds a number or works one out.</summary>
    private double? ValueAt(int row, int column)
    {
        if (row < 0 || row >= table.Rows.Count) return null;

        var cells = table.Rows[row].Cells;
        if (column < 0 || column >= cells.Count) return null;

        var content = cells[column].Content;

        if (Number(Text(content)) is { } number) return number;

        // A cell that holds a formula rather than a number is worked out, so that a total can be
        // read by another total.
        if (_depth >= MaximumDepth) return null;

        foreach (var formula in Formulas(content))
        {
            _depth++;
            try
            {
                if (FieldFormula.Evaluate(formula, new TableCells(table, row, column) { _depth = _depth })
                    is { } computed)
                {
                    return computed;
                }
            }
            finally
            {
                _depth--;
            }
        }

        return null;
    }

    private static IEnumerable<string> Formulas(IEnumerable<BlockElement> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is not Paragraph paragraph) continue;

            foreach (var run in paragraph.Runs)
            foreach (var content in run.Content)
            {
                if (content is not FieldInline field) continue;

                var instruction = FieldInstruction.Parse(field.Instruction);
                if (instruction.Keyword == "=" && instruction.Argument is { Length: > 0 } expression)
                    yield return expression;
            }
        }
    }

    private static string Text(IEnumerable<BlockElement> blocks)
    {
        var text = new StringBuilder();

        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) text.Append(paragraph.GetText());
        }

        return text.ToString();
    }

    /// <summary>
    /// The number a cell's text stands for. What is around it does not stop it being one: a cell
    /// reading "$1,200.00" is twelve hundred, and one reading "n/a" is no number at all.
    /// </summary>
    private static double? Number(string text)
    {
        var cleaned = new StringBuilder();
        var digits = false;

        foreach (var c in text)
        {
            if (char.IsDigit(c))
            {
                digits = true;
                cleaned.Append(c);
                continue;
            }

            if (c == '.' || (c == '-' && cleaned.Length == 0)) cleaned.Append(c);

            // A comma between digits is a thousands separator; anything else that is not a digit
            // means this cell is not a number at all.
            else if (c != ',' && !char.IsWhiteSpace(c) && c != '$' && c != '£' && c != '€' && c != '%')
                return null;
        }

        return digits &&
               double.TryParse(cleaned.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
