using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PdfAnalyticsMcp.Models;
using PDFiumCore;

namespace PdfAnalyticsMcp.Services;

public class RenderPagePreviewService(
    IPdfiumService pdfiumService,
    ILogger<RenderPagePreviewService> logger) : IRenderPagePreviewService
{
    public async Task<RenderPagePreviewResult> RenderAsync(string pdfPath, int page, int dpi, string format, int quality, CancellationToken cancellationToken = default)
    {
        // Validate format, DPI, and quality before acquiring semaphore
        string normalizedFormat = NormalizeFormat(format);
        string mimeType = normalizedFormat == "png" ? "image/png" : "image/jpeg";
        ValidateDpi(dpi);
        ValidateQuality(quality);

        var raw = await RenderRawAsync(pdfPath, page, dpi, cancellationToken);

        byte[] imageData = normalizedFormat == "png"
            ? PngEncoder.Encode(raw.BgraData, raw.Width, raw.Height, preserveAlpha: false)
            : JpegEncoder.Encode(raw.BgraData, raw.Width, raw.Height, quality);

        return new RenderPagePreviewResult(page, dpi, normalizedFormat, quality, raw.Width, raw.Height, imageData, mimeType);
    }

    public async Task<RenderRawResult> RenderRawAsync(string pdfPath, int page, int dpi, CancellationToken cancellationToken = default)
    {
        ValidateDpi(dpi);

        float scale = (float)(dpi / 72.0);

        return await pdfiumService.ExecuteAsync(pdfPath, page, (document, loadedPage) =>
        {
            var (pageWidth, pageHeight) = pdfiumService.GetPageSize(document, page - 1);
            int width = (int)(pageWidth * scale);
            int height = (int)(pageHeight * scale);

            var bitmap = fpdfview.FPDFBitmapCreateEx(width, height, 4, IntPtr.Zero, 0); // 4 = BGRA
            if (bitmap == null)
            {
                throw new ArgumentException($"An error occurred rendering page {page}.");
            }

            try
            {
                fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);

                using var matrix = new FS_MATRIX_();
                using var clipping = new FS_RECTF_();
                matrix.A = scale;
                matrix.B = 0;
                matrix.C = 0;
                matrix.D = scale;
                matrix.E = 0;
                matrix.F = 0;
                clipping.Left = 0;
                clipping.Bottom = 0;
                clipping.Right = width;
                clipping.Top = height;

                fpdfview.FPDF_RenderPageBitmapWithMatrix(bitmap, loadedPage, matrix, clipping, 0);

                IntPtr buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
                if (buffer == IntPtr.Zero)
                {
                    throw new ArgumentException($"An error occurred rendering page {page}.");
                }

                int stride = fpdfview.FPDFBitmapGetStride(bitmap);
                int rowBytes = width * 4;
                byte[] rawBytes = new byte[width * height * 4];

                if (stride == rowBytes)
                {
                    Marshal.Copy(buffer, rawBytes, 0, rawBytes.Length);
                }
                else
                {
                    // Copy row by row to remove alignment padding
                    for (int row = 0; row < height; row++)
                    {
                        Marshal.Copy(buffer + row * stride, rawBytes, row * rowBytes, rowBytes);
                    }
                }

                logger.LogDebug("Rendered page {Page} at {Dpi} DPI: {Width}x{Height} pixels.", page, dpi, width, height);

                return new RenderRawResult(width, height, rawBytes);
            }
            catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
            {
                throw new ArgumentException($"An error occurred rendering page {page}.");
            }
            finally
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }, cancellationToken);
    }

    private static void ValidateDpi(int dpi)
    {
        if (dpi < 72 || dpi > 600)
        {
            throw new ArgumentException("DPI must be between 72 and 600.");
        }
    }

    private static void ValidateQuality(int quality)
    {
        if (quality < 1 || quality > 100)
        {
            throw new ArgumentException("Quality must be between 1 and 100.");
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
