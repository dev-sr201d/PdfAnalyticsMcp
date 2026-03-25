namespace PdfAnalyticsMcp.Models;

public record PathDto(
    double X,
    double Y,
    double W,
    double H,
    string? FillColor,
    string? StrokeColor,
    int VertexCount);
