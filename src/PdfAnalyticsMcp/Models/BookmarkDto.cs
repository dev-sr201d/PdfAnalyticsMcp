namespace PdfAnalyticsMcp.Models;

public record BookmarkDto(
    string Title,
    int? PageNumber,
    IReadOnlyList<BookmarkDto>? Children);
