namespace PdfAnalyticsMcp.Models;

public record RectangleDto(
    double X,
    double Y,
    double W,
    double H,
    string? FillColor,
    string? StrokeColor,
    double? StrokeWidth);
