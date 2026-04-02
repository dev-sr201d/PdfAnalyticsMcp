using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using PdfAnalyticsMcp.Models;

namespace PdfAnalyticsMcp.Services;

public class RenderPagePreviewService(IInputValidationService validationService, ILogger<RenderPagePreviewService> logger) : IRenderPagePreviewService
{
    private static readonly SemaphoreSlim _renderSemaphore = new(1, 1);

    public async Task<RenderPagePreviewResult> RenderAsync(string pdfPath, int page, int dpi, string format, int quality, CancellationToken cancellationToken = default)
    {
        // Validate format before acquiring semaphore
        string normalizedFormat = NormalizeFormat(format);
        string mimeType = normalizedFormat == "png" ? "image/png" : "image/jpeg";

        // Validate quality before acquiring semaphore
        if (quality < 1 || quality > 100)
        {
            throw new ArgumentException("Quality must be between 1 and 100.");
        }

        var raw = await RenderRawAsync(pdfPath, page, dpi, cancellationToken);

        byte[] imageData = normalizedFormat == "png"
            ? PngEncoder.Encode(raw.BgraData, raw.Width, raw.Height)
            : JpegEncoder.Encode(raw.BgraData, raw.Width, raw.Height, quality);

        return new RenderPagePreviewResult(page, dpi, normalizedFormat, quality, raw.Width, raw.Height, imageData, mimeType);
    }

    public async Task<RenderRawResult> RenderRawAsync(string pdfPath, int page, int dpi, CancellationToken cancellationToken = default)
    {
        validationService.ValidateFilePath(pdfPath);

        if (dpi < 72 || dpi > 600)
        {
            throw new ArgumentException("DPI must be between 72 and 600.");
        }

        double scalingFactor = dpi / 72.0;

        await _renderSemaphore.WaitAsync(cancellationToken);
        try
        {
            Docnet.Core.Readers.IDocReader docReader;
            try
            {
                docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ArgumentException($"The file could not be accessed: {pdfPath}. It may be in use by another process.");
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                // Docnet/PDFium uses native code that may not throw .NET I/O exceptions for locked files.
                // Probe file accessibility to distinguish I/O issues from format issues.
                try
                {
                    using var _ = File.Open(pdfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
                catch (Exception probeEx) when (probeEx is IOException or UnauthorizedAccessException)
                {
                    throw new ArgumentException($"The file could not be accessed: {pdfPath}. It may be in use by another process.");
                }

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
                catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
                {
                    throw new ArgumentException($"An error occurred rendering page {page}.");
                }

                if (rawBytes is null || rawBytes.Length == 0)
                {
                    throw new ArgumentException($"An error occurred rendering page {page}.");
                }

                logger.LogDebug("Rendered page {Page} at {Dpi} DPI: {Width}x{Height} pixels.", page, dpi, width, height);

                return new RenderRawResult(width, height, rawBytes);
            }
        }
        finally
        {
            _renderSemaphore.Release();
        }
    }

    private static string NormalizeFormat(string format)
    {
        if (string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
            return "png";
        if (string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase))
            return "jpeg";

        throw new ArgumentException("Format must be 'png', 'jpeg', or 'jpg'.");
    }
}
