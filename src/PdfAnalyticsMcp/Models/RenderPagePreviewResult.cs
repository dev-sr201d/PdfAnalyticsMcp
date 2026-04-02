namespace PdfAnalyticsMcp.Models;

public record RenderPagePreviewResult(
    int Page,
    int Dpi,
    string Format,
    int Quality,
    int Width,
    int Height,
    byte[] ImageData,
    string MimeType);
