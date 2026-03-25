namespace PdfAnalyticsMcp.Models;

public record LineDto(
    double X1,
    double Y1,
    double X2,
    double Y2,
    string? StrokeColor,
    double? StrokeWidth,
    string? DashPattern);
