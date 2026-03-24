namespace PdfAnalyticsMcp.Services;

public static class FormatUtils
{
    public static double RoundCoordinate(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    public static string FormatColor(byte r, byte g, byte b) =>
        $"#{r:X2}{g:X2}{b:X2}";
}
