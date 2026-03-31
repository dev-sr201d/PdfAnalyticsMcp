using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IPageImagesService
{
    Task<PageImagesDto> ExtractAsync(string pdfPath, int page, string? outputPath = null, CancellationToken cancellationToken = default);
}
