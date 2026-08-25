using n8PDF.Layout;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The formula-field expression parser is depth-bounded (#195): a deeply nested expression is
/// refused rather than overflowing the stack and killing the process.
/// </summary>
public class FormulaDepthTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private double? EvaluateBounded(string expression)
    {
        double? result = null;
        Exception? failure = null;

        // A small stack makes an unbounded parser overflow deterministically; the guard caps the
        // depth at 64, which fits, so the parse returns.
        var thread = new Thread(() =>
        {
            try { result = FieldFormula.Evaluate(expression, null); }
            catch (Exception e) { failure = e; }
        }, maxStackSize: 512 * 1024);

        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "the formula parse did not finish — not bounded");
        Assert.Null(failure);
        return result;
    }

    [Theory]
    [InlineData("parentheses")]
    [InlineData("powers")]
    [InlineData("unary")]
    public void A_deeply_nested_expression_does_not_overflow_the_stack(string kind)
    {
        var expression = kind switch
        {
            "parentheses" => new string('(', 20_000) + "1" + new string(')', 20_000),
            "powers" => string.Join("^", Enumerable.Repeat("1", 20_000)),
            _ => new string('-', 20_000) + "1"
        };

        var value = EvaluateBounded(expression);
        _output.WriteLine($"{kind}: nested 20,000 deep evaluated to {(value?.ToString() ?? "null")}");
        // A shallow, valid version still evaluates, proving the bound does not break real formulas.
        Assert.Equal(1, FieldFormula.Evaluate("(((1)))", null));
    }
}
