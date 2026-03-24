namespace PdfAnalyticsMcp.Models;

public record PdfInfoDto(
    int PageCount,
    IReadOnlyList<PageInfoDto> Pages,
    string? Title,
    string? Author,
    string? Subject,
    string? Keywords,
    string? Creator,
    string? Producer,
    IReadOnlyList<BookmarkDto>? Bookmarks);
