using PDFiumCore;

namespace PdfAnalyticsMcp.Services;

public interface IPdfiumService
{
    Task<T> ExecuteAsync<T>(string pdfPath, int page, Func<FpdfDocumentT, FpdfPageT, T> callback, CancellationToken cancellationToken = default);

    int GetPageCount(FpdfDocumentT document);

    (double Width, double Height) GetPageSize(FpdfDocumentT document, int pageIndex);
}
