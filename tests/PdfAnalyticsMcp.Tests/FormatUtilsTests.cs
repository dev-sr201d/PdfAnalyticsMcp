using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class FormatUtilsTests
{
    [Theory]
    [InlineData(123.456789, 123.5)]
    [InlineData(0.0, 0.0)]
    [InlineData(-45.678, -45.7)]
    [InlineData(100.0, 100.0)]
    [InlineData(99.95, 100.0)]
    [InlineData(99.94, 99.9)]
    [InlineData(0.05, 0.1)]
    [InlineData(-0.05, -0.1)]
    public void RoundCoordinate_RoundsToOneDecimalPlace(double input, double expected)
    {
        var result = FormatUtils.RoundCoordinate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(255, 0, 0, "#FF0000")]
    [InlineData(0, 255, 0, "#00FF00")]
    [InlineData(0, 0, 255, "#0000FF")]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    [InlineData(128, 64, 32, "#804020")]
    public void FormatColor_ReturnsHexString(byte r, byte g, byte b, string expected)
    {
        var result = FormatUtils.FormatColor(r, g, b);
        Assert.Equal(expected, result);
    }
}
