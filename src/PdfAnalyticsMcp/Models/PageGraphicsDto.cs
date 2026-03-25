namespace PdfAnalyticsMcp.Models;

public record PageGraphicsDto(
    int Page,
    double Width,
    double Height,
    IReadOnlyList<RectangleDto> Rectangles,
    IReadOnlyList<LineDto> Lines,
    IReadOnlyList<PathDto> Paths);
