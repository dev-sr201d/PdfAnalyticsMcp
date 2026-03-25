using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IPageImagesService
{
    PageImagesDto Extract(string pdfPath, int page, bool includeData);
}
