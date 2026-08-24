using n8PDF.Pdf;
using Xunit;

namespace n8PDF.Tests;

/// <summary>
/// The writer never emits a token that would make the output malformed (#156).
/// </summary>
public class OutputSafetyTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_number_is_written_as_zero(double value)
    {
        Assert.Equal("0", PdfNumber.Format(value));
    }

    [Fact]
    public void A_finite_number_is_unaffected()
    {
        Assert.Equal("1.5", PdfNumber.Format(1.5));
        Assert.Equal("42", PdfNumber.Format(42, isInteger: true));
    }
}
