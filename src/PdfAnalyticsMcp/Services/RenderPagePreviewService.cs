using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public class RenderPagePreviewService(IInputValidationService validationService, ILogger<RenderPagePreviewService> logger) : IRenderPagePreviewService
{
    public RenderPagePreviewResult Render(string pdfPath, int page, int dpi)
    {
        validationService.ValidateFilePath(pdfPath);

        if (dpi < 72 || dpi > 600)
        {
            throw new ArgumentException("DPI must be between 72 and 600.");
        }

        double scalingFactor = dpi / 72.0;

        Docnet.Core.Readers.IDocReader docReader;
        try
        {
            docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor));
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("The file could not be opened as a PDF.");
        }

        using (docReader)
        {
            int pageCount = docReader.GetPageCount();
            validationService.ValidatePageNumber(page, pageCount);

            using var pageReader = docReader.GetPageReader(page - 1);

            int width;
            int height;
            byte[] rawBytes;
            try
            {
                width = pageReader.GetPageWidth();
                height = pageReader.GetPageHeight();
                rawBytes = pageReader.GetImage();
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"An error occurred rendering page {page}.");
            }

            if (rawBytes is null || rawBytes.Length == 0)
            {
                throw new ArgumentException($"An error occurred rendering page {page}.");
            }

            logger.LogDebug("Rendered page {Page} at {Dpi} DPI: {Width}x{Height} pixels.", page, dpi, width, height);

            byte[] pngData = PngEncoder.Encode(rawBytes, width, height);

            return new RenderPagePreviewResult(page, dpi, width, height, pngData);
        }
    }
}
