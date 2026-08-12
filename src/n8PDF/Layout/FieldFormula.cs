using System.Globalization;

namespace n8PDF.Layout;

/// <summary>
/// The cells a formula can read, which is the table it stands in.
/// </summary>
public interface IFormulaCells
{
    /// <summary>
    /// The numbers running away from this cell in a direction — ABOVE, BELOW, LEFT or RIGHT.
    /// </summary>
    /// <remarks>
    /// The reading stops at the first cell that is not a number, which Word's own export shows:
    /// a column holding 10, "n/a" and 3 sums to 3 from below it, not to 13.
    /// </remarks>
    IReadOnlyList<double> InDirection(string direction);

    /// <summary>
    /// The numbers in a rectangle of cells named outright. Unlike a direction this reads the
    /// whole of it, passing over the cells that hold no number: the same column averaged as
    /// A1:A3 comes to 6.5 rather than 4.33, so a cell of text is skipped and not counted as zero.
    /// </summary>
    IReadOnlyList<double> InRange(string from, string to);

    /// <summary>The number in one cell, or null where it holds none.</summary>
    double? Cell(string reference);
}

/// <summary>
/// Works out what a formula field comes to.
/// </summary>
/// <remarks>
/// A formula field is an equals sign and an expression: arithmetic over numbers written into it,
/// over the cells of the table it stands in, or over both. It is the one field that is a language
/// rather than a lookup, so this is a parser — numbers, the five operators and their precedence,
/// comparisons, brackets, and the functions Word knows.
/// </remarks>
public static class FieldFormula
{
    /// <summary>What a formula comes to, or null where it cannot be worked out.</summary>
    public static double? Evaluate(string expression, IFormulaCells? cells)
    {
        var tokens = Tokenize(expression);
        if (tokens.Count == 0) return null;

        var parser = new Parser(tokens, cells);
        var value = parser.Comparison();

        return parser.AtEnd ? value : null;
    }

    /// <summary>
    /// How a formula reads with no picture to say otherwise: to two decimal places, with the
    /// zeros at the end of it dropped. Word shows 10/3 as 3.33 and 10/4 as 2.5.
    /// </summary>
    public static string Format(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);

        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // ----- reading the expression -----

    /// <summary>
    /// The kinds of thing an expression is made of. Nothing comes first, so that reading past the
    /// end of an expression gives back nothing rather than a nought — which is what would let
    /// "2+" come to two.
    /// </summary>
    private enum Kind { Nothing, Number, Name, Operator, Open, Close, Comma, Colon }

    private readonly record struct Token(Kind Kind, string Text, double Value);

    private static List<Token> Tokenize(string expression)
    {
        var tokens = new List<Token>();
        var index = 0;

        while (index < expression.Length)
        {
            var c = expression[index];

            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && index + 1 < expression.Length && char.IsDigit(expression[index + 1])))
            {
                var start = index;
                while (index < expression.Length && (char.IsDigit(expression[index]) || expression[index] == '.')) index++;

                if (!double.TryParse(expression[start..index], NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var number))
                {
                    return [];
                }

                // A number written as a percentage is that fraction of one, which is what makes
                // 50%*8 come to four.
                if (index < expression.Length && expression[index] == '%')
                {
                    number /= 100;
                    index++;
                }

                tokens.Add(new Token(Kind.Number, string.Empty, number));
                continue;
            }

            if (char.IsLetter(c))
            {
                var start = index;
                while (index < expression.Length && char.IsLetterOrDigit(expression[index])) index++;

                tokens.Add(new Token(Kind.Name, expression[start..index], 0));
                continue;
            }

            index++;

            switch (c)
            {
                case '(': tokens.Add(new Token(Kind.Open, "(", 0)); continue;
                case ')': tokens.Add(new Token(Kind.Close, ")", 0)); continue;
                case ',': tokens.Add(new Token(Kind.Comma, ",", 0)); continue;
                case ';': tokens.Add(new Token(Kind.Comma, ",", 0)); continue;
                case ':': tokens.Add(new Token(Kind.Colon, ":", 0)); continue;

                case '<' or '>':
                    // The two-character comparisons: <=, >= and <>.
                    if (index < expression.Length && expression[index] is '=' or '>')
                    {
                        tokens.Add(new Token(Kind.Operator, string.Concat(c, expression[index]), 0));
                        index++;
                        continue;
                    }

                    tokens.Add(new Token(Kind.Operator, c.ToString(), 0));
                    continue;

                case '+' or '-' or '*' or '/' or '^' or '=' or '%':
                    tokens.Add(new Token(Kind.Operator, c.ToString(), 0));
                    continue;

                default:
                    return [];
            }
        }

        return tokens;
    }

    private sealed class Parser(List<Token> tokens, IFormulaCells? cells)
    {
        private int _index;

        public bool AtEnd => _index >= tokens.Count;

        private Token Current => _index < tokens.Count ? tokens[_index] : default;

        /// <summary>A comparison, which comes to one where it holds and nothing where it does not.</summary>
        public double? Comparison()
        {
            var left = Sum();
            if (left is null) return null;

            if (Current.Kind != Kind.Operator || Current.Text is not ("=" or "<>" or "<" or ">" or "<=" or ">="))
                return left;

            var op = Current.Text;
            _index++;

            var right = Sum();
            if (right is null) return null;

            var holds = op switch
            {
                "=" => left == right,
                "<>" => left != right,
                "<" => left < right,
                ">" => left > right,
                "<=" => left <= right,
                _ => left >= right
            };

            return holds ? 1 : 0;
        }

        private double? Sum()
        {
            var value = Term();

            while (value is not null && Current.Kind == Kind.Operator && Current.Text is "+" or "-")
            {
                var op = Current.Text;
                _index++;

                var right = Term();
                if (right is null) return null;

                value = op == "+" ? value + right : value - right;
            }

            return value;
        }

        private double? Term()
        {
            var value = Power();

            while (value is not null && Current.Kind == Kind.Operator && Current.Text is "*" or "/")
            {
                var op = Current.Text;
                _index++;

                var right = Power();
                if (right is null) return null;

                if (op == "/" && right == 0) return null;

                value = op == "*" ? value * right : value / right;
            }

            return value;
        }

        private double? Power()
        {
            var value = Unary();
            if (value is null || Current.Kind != Kind.Operator || Current.Text != "^") return value;

            _index++;

            var exponent = Power();
            return exponent is null ? null : Math.Pow(value.Value, exponent.Value);
        }

        private double? Unary()
        {
            if (Current.Kind != Kind.Operator || Current.Text is not ("-" or "+")) return Primary();

            var negate = Current.Text == "-";
            _index++;

            var value = Unary();
            return value is null ? null : negate ? -value : value;
        }

        private double? Primary()
        {
            var token = Current;

            switch (token.Kind)
            {
                case Kind.Number:
                    _index++;
                    return token.Value;

                case Kind.Open:
                {
                    _index++;
                    var value = Comparison();
                    if (value is null || Current.Kind != Kind.Close) return null;

                    _index++;
                    return value;
                }

                case Kind.Name:
                {
                    _index++;

                    // A name followed by a bracket is a function; one on its own is a cell.
                    if (Current.Kind == Kind.Open) return Function(token.Text);

                    return cells?.Cell(token.Text);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// A function and what it is given. Its arguments are lists rather than single values,
        /// since a range or a direction stands for however many cells it covers.
        /// </summary>
        private double? Function(string name)
        {
            _index++;

            var arguments = new List<List<double>>();

            if (Current.Kind != Kind.Close)
            {
                while (true)
                {
                    var argument = Argument();
                    if (argument is null) return null;

                    arguments.Add(argument);

                    if (Current.Kind != Kind.Comma) break;
                    _index++;
                }
            }

            if (Current.Kind != Kind.Close) return null;
            _index++;

            return Apply(name, arguments);
        }

        private List<double>? Argument()
        {
            // A direction stands for the cells running away from this one.
            if (Current.Kind == Kind.Name && IsDirection(Current.Text))
            {
                var direction = Current.Text;
                _index++;

                return [.. cells?.InDirection(direction) ?? []];
            }

            // A range: two cells with a colon between them.
            if (Current.Kind == Kind.Name && _index + 2 < tokens.Count &&
                tokens[_index + 1].Kind == Kind.Colon && tokens[_index + 2].Kind == Kind.Name)
            {
                var from = Current.Text;
                var to = tokens[_index + 2].Text;
                _index += 3;

                return [.. cells?.InRange(from, to) ?? []];
            }

            var value = Comparison();
            return value is null ? null : [value.Value];
        }

        private static bool IsDirection(string name) =>
            name.Equals("ABOVE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("BELOW", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("RIGHT", StringComparison.OrdinalIgnoreCase);

        private static double? Apply(string name, List<List<double>> arguments)
        {
            var all = arguments.SelectMany(a => a).ToList();
            var first = arguments.Count > 0 && arguments[0].Count > 0 ? arguments[0][0] : (double?)null;

            switch (name.ToUpperInvariant())
            {
                case "SUM": return all.Sum();
                case "PRODUCT": return all.Aggregate(1.0, (a, b) => a * b);
                case "COUNT": return all.Count;
                case "AVERAGE": return all.Count == 0 ? null : all.Average();
                case "MIN": return all.Count == 0 ? null : all.Min();
                case "MAX": return all.Count == 0 ? null : all.Max();

                case "ABS": return first is { } abs ? Math.Abs(abs) : null;
                case "INT": return first is { } truncated ? Math.Truncate(truncated) : null;
                case "SIGN": return first is { } sign ? Math.Sign(sign) : null;

                case "ROUND":
                    return arguments.Count == 2 && first is { } value && arguments[1].Count == 1
                        ? Math.Round(value, Math.Clamp((int)arguments[1][0], 0, 15), MidpointRounding.AwayFromZero)
                        : null;

                case "MOD":
                    return arguments.Count == 2 && first is { } dividend && arguments[1].Count == 1 &&
                           arguments[1][0] != 0
                        ? dividend % arguments[1][0]
                        : null;

                case "AND": return all.Count > 0 && all.All(v => v != 0) ? 1 : 0;
                case "OR": return all.Any(v => v != 0) ? 1 : 0;
                case "NOT": return first is { } negated ? negated == 0 ? 1 : 0 : null;

                case "TRUE": return 1;
                case "FALSE": return 0;

                case "DEFINED": return first is not null ? 1 : 0;

                case "IF":
                    return arguments.Count == 3 && first is { } condition
                        ? condition != 0
                            ? arguments[1].Count > 0 ? arguments[1][0] : null
                            : arguments[2].Count > 0 ? arguments[2][0] : null
                        : null;

                default: return null;
            }
        }
    }
}
