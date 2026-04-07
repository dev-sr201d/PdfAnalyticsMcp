using Microsoft.Extensions.Logging;
using PDFiumCore;

namespace PdfAnalyticsMcp.Services;

public class PdfiumService(IInputValidationService validationService, ILogger<PdfiumService> logger) : IPdfiumService
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<T> ExecuteAsync<T>(string pdfPath, int page, Func<FpdfDocumentT, FpdfPageT, T> callback, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        FpdfDocumentT? document = null;
        FpdfPageT? loadedPage = null;
        try
        {
            document = fpdfview.FPDF_LoadDocument(pdfPath, null);
            if (document == null)
            {
                var errorCode = fpdfview.FPDF_GetLastError();
                if (errorCode == 2) // FPDF_ERR_FILE
                {
                    throw new ArgumentException($"The file could not be accessed: {pdfPath}. It may be in use by another process.");
                }
                // FPDF_ERR_FORMAT (3), FPDF_ERR_PASSWORD (4), or any other error
                throw new ArgumentException("The file could not be opened as a PDF.");
            }

            int pageCount = fpdfview.FPDF_GetPageCount(document);
            validationService.ValidatePageNumber(page, pageCount);

            loadedPage = fpdfview.FPDF_LoadPage(document, page - 1);
            if (loadedPage == null)
            {
                throw new InvalidOperationException("The page could not be loaded.");
            }

            logger.LogDebug("PDFium loaded page {Page} of {PdfPath}.", page, pdfPath);
            return callback(document, loadedPage);
        }
        finally
        {
            if (loadedPage != null)
            {
                fpdfview.FPDF_ClosePage(loadedPage);
            }
            if (document != null)
            {
                fpdfview.FPDF_CloseDocument(document);
            }
            _semaphore.Release();
        }
    }

    public int GetPageCount(FpdfDocumentT document)
    {
        return fpdfview.FPDF_GetPageCount(document);
    }

    public (double Width, double Height) GetPageSize(FpdfDocumentT document, int pageIndex)
    {
        double width = 0;
        double height = 0;
        fpdfview.FPDF_GetPageSizeByIndex(document, pageIndex, ref width, ref height);
        return (width, height);
    }
}
