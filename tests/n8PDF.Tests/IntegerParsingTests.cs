using System.Xml.Linq;
using n8PDF.Ooxml;
using Xunit;

namespace n8PDF.Tests;

/// <summary>
/// Document integers are read invariantly and an out-of-range value is refused rather than
/// wrapping to int.MinValue (#148, #160).
/// </summary>
public class IntegerParsingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static XElement WithAttr(string value) =>
        new(W + "e", new XAttribute(W + "val", value));

    [Fact]
    public void An_out_of_range_decimal_measurement_is_refused()   // #148
    {
        // (int)Math.Round(1e20) wraps to int.MinValue; it is no measurement.
        Assert.Null(WithAttr("100000000000000000000").IntAttr("val"));
    }

    [Fact]
    public void A_decimal_is_parsed_invariantly_not_by_the_ambient_culture()   // #160
    {
        // "1,5" is not a valid invariant integer or float — a comma is a thousands separator at
        // most, never a decimal point, whatever locale the machine runs in.
        Assert.Equal(15, WithAttr("15").IntAttr("val"));
        Assert.Equal(3, WithAttr("3.4").IntAttr("val"));   // decimal fallback, invariant
    }

    [Fact]
    public void A_normal_integer_still_reads()
    {
        Assert.Equal(240, WithAttr("240").IntAttr("val"));
        Assert.Equal(-120, WithAttr("-120").IntAttr("val"));
    }
}
