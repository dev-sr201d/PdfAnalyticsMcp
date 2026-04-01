namespace PdfAnalyticsMcp.Models;

public record PdfInfoDto(
    int PageCount,
    double PredominantPageWidth,
    double PredominantPageHeight,
    IReadOnlyList<PageSizeExceptionDto>? PageSizeExceptions,
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    IReadOnlyList<BookmarkDto>? Bookmarks);
