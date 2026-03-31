using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IRenderPagePreviewService
{
    Task<RenderPagePreviewResult> RenderAsync(string pdfPath, int page, int dpi, CancellationToken cancellationToken = default);
    Task<RenderRawResult> RenderRawAsync(string pdfPath, int page, int dpi, CancellationToken cancellationToken = default);
}
