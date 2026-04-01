namespace PdfAnalyticsMcp.Models;

public record BookmarkDto(
    string Title,
    int? Page,
    IReadOnlyList<BookmarkDto>? Children);
