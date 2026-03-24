namespace PdfAnalyticsMcp.Models;

public record TextElementDto(
    string Text,
    double X,
    double Y,
    double W,
    double H,
    string Font,
    double Size,
    string? Color,
    bool? Bold,
    bool? Italic);
