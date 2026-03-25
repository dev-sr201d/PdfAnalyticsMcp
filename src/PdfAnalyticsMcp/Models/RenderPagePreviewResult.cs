namespace PdfAnalyticsMcp.Models;

public record RenderPagePreviewResult(
    int Page,
    int Dpi,
    int Width,
    int Height,
    byte[] PngData);
