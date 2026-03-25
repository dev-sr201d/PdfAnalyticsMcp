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
            var pages = new List<PageInfoDto>(document.NumberOfPages);
            for (int i = 1; i <= document.NumberOfPages; i++)
            {
                var page = document.GetPage(i);
                pages.Add(new PageInfoDto(
                    i,
                    FormatUtils.RoundCoordinate(page.Width),
                    FormatUtils.RoundCoordinate(page.Height)));
            }

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
                pages,
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

            int? pageNumber = node is DocumentBookmarkNode docNode
                ? docNode.PageNumber
                : null;

            result.Add(new BookmarkDto(
                node.Title,
                pageNumber,
                children));
        }
        return result;
    }
}
