using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IPageGraphicsService
{
    PageGraphicsDto Extract(string pdfPath, int page);
}
