using PdfAnalyticsMcp.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace PdfAnalyticsMcp.Services;

public class PdfInfoService : IPdfInfoService
{
    public PdfInfoDto Extract(string pdfPath)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"The file could not be accessed: {pdfPath}. It may be in use by another process.");
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("The file could not be opened as a PDF.");
        }

        using (document)
        {
            var pageDims = new List<(int Number, double Width, double Height)>(document.NumberOfPages);
            for (int i = 1; i <= document.NumberOfPages; i++)
            {
                var page = document.GetPage(i);
                pageDims.Add((i,
                    FormatUtils.RoundCoordinate(page.Width),
                    FormatUtils.RoundCoordinate(page.Height)));
            }

            // Determine predominant page size: group by (width, height), pick largest group.
            // Tie-break: the group whose first page appears earliest wins.
            var predominant = pageDims
                .GroupBy(p => (p.Width, p.Height))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.First().Number)
                .First().Key;

            IReadOnlyList<PageSizeExceptionDto>? exceptions = null;
            var exceptionList = pageDims
                .Where(p => p.Width != predominant.Width || p.Height != predominant.Height)
                .Select(p => new PageSizeExceptionDto(p.Number, p.Width, p.Height))
                .ToList();
            if (exceptionList.Count > 0)
                exceptions = exceptionList;

            var info = document.Information;

            IReadOnlyList<BookmarkDto>? bookmarks = null;
            if (document.TryGetBookmarks(out Bookmarks? pdfBookmarks) && pdfBookmarks is not null)
            {
                var roots = MapBookmarks(pdfBookmarks.Roots);
                if (roots.Count > 0)
                    bookmarks = roots;
            }

            return new PdfInfoDto(
                document.NumberOfPages,
                predominant.Width,
                predominant.Height,
                exceptions,
                NullIfEmpty(info.Title),
                NullIfEmpty(info.Author),
                NullIfEmpty(info.Subject),
                NullIfEmpty(info.Keywords),
                NullIfEmpty(info.Creator),
                NullIfEmpty(info.Producer),
                bookmarks);
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static List<BookmarkDto> MapBookmarks(IReadOnlyList<BookmarkNode> nodes)
    {
        List<BookmarkDto> result = [];
        foreach (var node in nodes)
        {
            var children = node.Children.Count > 0
                ? MapBookmarks(node.Children)
                : null;

            if (children is { Count: 0 })
                children = null;

            int? page = node is DocumentBookmarkNode docNode
                ? docNode.PageNumber
                : null;

            result.Add(new BookmarkDto(
                node.Title,
                page,
                children));
        }
        return result;
    }
}
