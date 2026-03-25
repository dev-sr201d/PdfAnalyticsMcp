using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IRenderPagePreviewService
{
    RenderPagePreviewResult Render(string pdfPath, int page, int dpi);
}
