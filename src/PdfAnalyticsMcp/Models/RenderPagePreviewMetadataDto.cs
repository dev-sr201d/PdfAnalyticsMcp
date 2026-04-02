namespace PdfAnalyticsMcp.Models;

public record RenderPagePreviewMetadataDto(
    int Page,
    int Dpi,
    string Format,
    int Quality,
    int Width,
    int Height,
    int SizeBytes);
