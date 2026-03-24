using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public interface IPdfInfoService
{
    PdfInfoDto Extract(string pdfPath);
}
