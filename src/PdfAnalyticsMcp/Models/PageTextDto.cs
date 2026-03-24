namespace PdfAnalyticsMcp.Models;

public record PageTextDto(
    int Page,
    double Width,
    double Height,
    IReadOnlyList<TextElementDto> Elements);
