namespace PdfAnalyticsMcp.Models;

public record RenderPagePreviewMetadataDto(
    int Page,
    int Dpi,
    int Width,
    int Height);
