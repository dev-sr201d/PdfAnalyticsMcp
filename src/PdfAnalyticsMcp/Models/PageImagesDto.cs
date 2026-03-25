namespace PdfAnalyticsMcp.Models;

public record PageImagesDto(
    int Page,
    double Width,
    double Height,
    IReadOnlyList<ImageElementDto> Images);
