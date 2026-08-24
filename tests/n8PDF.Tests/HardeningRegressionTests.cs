using n8PDF.Layout;
using n8PDF.Text;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Regressions for the newly-audited allocation/hang surfaces (#196, #202).
/// </summary>
public class HardeningRegressionTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void A_very_long_word_does_not_hang_the_hyphenator()   // #196
    {
        var word = new string('a', 100_000);

        var thread = new Thread(() => Hyphenator.Points(word));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)),
            "the hyphenator did not finish on a 100,000-letter word — its inner loop is not bounded");

        // A real word still hyphenates.
        Assert.NotEmpty(Hyphenator.Points("hyphenation"));
    }

    [Fact]
    public void A_picture_with_many_decimal_places_does_not_throw()   // #202
    {
        // Math.Round throws past fifteen places; a document-stated picture is unbounded.
        var picture = "0." + new string('0', 40);
        var ex = Record.Exception(() => NumericPicture.Format(1.5, picture));
        Assert.Null(ex);
        _output.WriteLine($"formatted to: {NumericPicture.Format(1.5, picture)}");

        // A normal picture still formats.
        Assert.Equal("5.00", NumericPicture.Format(5, "0.00"));
    }
}
