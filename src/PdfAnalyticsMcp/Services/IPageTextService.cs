using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IPageTextService
{
    PageTextDto Extract(string pdfPath, int page, string granularity);
    PageTextSummaryDto ExtractToFile(string pdfPath, int page, string granularity, string outputFile);
}
