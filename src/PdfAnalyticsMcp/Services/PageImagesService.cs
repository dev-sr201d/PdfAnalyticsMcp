using Microsoft.Extensions.Logging;
using PdfAnalyticsMcp.Models;
using UglyToad.PdfPig;

namespace PdfAnalyticsMcp.Services;

public class PageImagesService(
    IInputValidationService validationService,
    IRenderPagePreviewService renderService,
    ILogger<PageImagesService> logger) : IPageImagesService
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public async Task<PageImagesDto> ExtractAsync(string pdfPath, int page, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        if (outputPath is not null)
        {
            ValidateOutputPath(outputPath);
        }

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"The file could not be accessed: {pdfPath}. It may be in use by another process.");
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("The file could not be opened as a PDF.");
        }

        using (document)
        {
            validationService.ValidatePageNumber(page, document.NumberOfPages);

            var pdfPage = document.GetPage(page);
            var imageResults = new List<ImageElementDto>();
            var fallbackCandidates = new List<FallbackCandidate>();

            string? sanitizedStem = outputPath is not null ? SanitizeFileNameStem(pdfPath) : null;

            try
            {
                int imageIndex = 0;
                foreach (var image in pdfPage.GetImages())
                {
                    imageIndex++;
                    try
                    {
                        var bounds = image.BoundingBox;
                        double x = FormatUtils.RoundCoordinate(bounds.Left);
                        double y = FormatUtils.RoundCoordinate(bounds.Bottom);
                        double w = FormatUtils.RoundCoordinate(bounds.Width);
                        double h = FormatUtils.RoundCoordinate(bounds.Height);

                        int pixelWidth = image.WidthInSamples;
                        int pixelHeight = image.HeightInSamples;
                        int bitsPerComponent = image.BitsPerComponent;

                        string? filePath = null;

                        if (outputPath is not null)
                        {
                            string fileName = $"{sanitizedStem}_p{page}_img{imageIndex}.png";
                            string fullPath = Path.Combine(outputPath, fileName);

                            bool directSuccess = false;
                            try
                            {
                                if (image.TryGetPng(out var pngBytes))
                                {
                                    File.WriteAllBytes(fullPath, pngBytes);
                                    filePath = Path.GetFullPath(fullPath);
                                    directSuccess = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to write directly extracted PNG for image {Index} on page {Page}.", imageIndex, page);
                            }

                            if (!directSuccess)
                            {
                                fallbackCandidates.Add(new FallbackCandidate(
                                    ImageIndex: imageResults.Count,
                                    FilePath: fullPath,
                                    BoundsLeft: bounds.Left,
                                    BoundsBottom: bounds.Bottom,
                                    BoundsWidth: bounds.Width,
                                    BoundsHeight: bounds.Height,
                                    PixelWidth: pixelWidth,
                                    PixelHeight: pixelHeight));
                            }
                        }

                        imageResults.Add(new ImageElementDto(x, y, w, h, pixelWidth, pixelHeight, bitsPerComponent, filePath));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Skipping image due to extraction error on page {Page}.", page);
                    }
                }
            }
            catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
            {
                throw new ArgumentException($"An error occurred extracting images from page {page}.");
            }

            // Execute render-based fallback for images where TryGetPng failed
            if (fallbackCandidates.Count > 0)
            {
                await ExecuteFallbackAsync(pdfPath, page, fallbackCandidates, imageResults, cancellationToken);
            }

            return new PageImagesDto(
                page,
                FormatUtils.RoundCoordinate(pdfPage.Width),
                FormatUtils.RoundCoordinate(pdfPage.Height),
                imageResults);
        }
    }

    private async Task ExecuteFallbackAsync(
        string pdfPath,
        int page,
        List<FallbackCandidate> candidates,
        List<ImageElementDto> imageResults,
        CancellationToken cancellationToken)
    {
        int dpi = ComputeFallbackDpi(candidates);

        RenderRawResult rawResult;
        try
        {
            rawResult = await renderService.RenderRawAsync(pdfPath, page, dpi, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fallback rendering failed for page {Page}. Affected images will have file=null.", page);
            return;
        }

        double scale = dpi / 72.0;
        int renderWidth = rawResult.Width;
        int renderHeight = rawResult.Height;
        byte[] bgraData = rawResult.BgraData;

        foreach (var candidate in candidates)
        {
            try
            {
                var cropRegion = ComputeCropRegion(
                    candidate.BoundsLeft, candidate.BoundsBottom,
                    candidate.BoundsWidth, candidate.BoundsHeight,
                    scale, renderWidth, renderHeight);

                if (cropRegion is null)
                {
                    logger.LogWarning("Crop region has zero or negative dimensions for fallback image at index {Index} on page {Page}.", candidate.ImageIndex, page);
                    continue;
                }

                var (pixelLeft, pixelTop, cropWidth, cropHeight) = cropRegion.Value;

                // Extract crop from BGRA buffer
                byte[] croppedBgra = new byte[cropWidth * cropHeight * 4];
                int srcStride = renderWidth * 4;
                int dstStride = cropWidth * 4;

                for (int row = 0; row < cropHeight; row++)
                {
                    int srcOffset = (pixelTop + row) * srcStride + pixelLeft * 4;
                    int dstOffset = row * dstStride;
                    Buffer.BlockCopy(bgraData, srcOffset, croppedBgra, dstOffset, dstStride);
                }

                // Composite against white background (FRD-006 requirement 17)
                CompositeAgainstWhite(croppedBgra);

                byte[] pngBytes = PngEncoder.Encode(croppedBgra, cropWidth, cropHeight);
                File.WriteAllBytes(candidate.FilePath, pngBytes);

                // Update the image result with the file path
                var original = imageResults[candidate.ImageIndex];
                imageResults[candidate.ImageIndex] = original with { File = Path.GetFullPath(candidate.FilePath) };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract fallback image at index {Index} on page {Page}.", candidate.ImageIndex, page);
            }
        }
    }

    /// <summary>
    /// Computes the pixel crop region from PDF-point bounding box coordinates,
    /// inverting the Y-axis and clamping to render boundaries.
    /// Returns null if the clamped region has zero or negative dimensions.
    /// </summary>
    internal static (int Left, int Top, int Width, int Height)? ComputeCropRegion(
        double boundsLeft, double boundsBottom, double boundsWidth, double boundsHeight,
        double scale, int renderWidth, int renderHeight)
    {
        int pixelLeft = (int)Math.Round(boundsLeft * scale);
        int pixelTop = (int)Math.Round(renderHeight - (boundsBottom + boundsHeight) * scale);
        int cropWidth = (int)Math.Round(boundsWidth * scale);
        int cropHeight = (int)Math.Round(boundsHeight * scale);

        // Clamp to render boundaries
        pixelLeft = Math.Max(0, pixelLeft);
        pixelTop = Math.Max(0, pixelTop);
        cropWidth = Math.Min(cropWidth, renderWidth - pixelLeft);
        cropHeight = Math.Min(cropHeight, renderHeight - pixelTop);

        if (cropWidth <= 0 || cropHeight <= 0)
            return null;

        return (pixelLeft, pixelTop, cropWidth, cropHeight);
    }

    /// <summary>
    /// Composites BGRA pixel data against a white background, converting
    /// semi-transparent pixels to fully opaque against white.
    /// </summary>
    internal static void CompositeAgainstWhite(byte[] bgraData)
    {
        for (int i = 0; i < bgraData.Length; i += 4)
        {
            byte a = bgraData[i + 3];
            if (a == 255) continue; // Fully opaque — no compositing needed
            if (a == 0)
            {
                // Fully transparent — white
                bgraData[i] = 255;
                bgraData[i + 1] = 255;
                bgraData[i + 2] = 255;
                bgraData[i + 3] = 255;
                continue;
            }

            // Alpha blend against white: out = src * alpha + 255 * (1 - alpha)
            float alpha = a / 255f;
            float invAlpha = 1f - alpha;
            bgraData[i]     = (byte)(bgraData[i] * alpha + 255 * invAlpha);     // B
            bgraData[i + 1] = (byte)(bgraData[i + 1] * alpha + 255 * invAlpha); // G
            bgraData[i + 2] = (byte)(bgraData[i + 2] * alpha + 255 * invAlpha); // R
            bgraData[i + 3] = 255;                                               // A
        }
    }

    internal static int ComputeFallbackDpi(List<FallbackCandidate> candidates)
    {
        double maxDpi = 150.0;

        foreach (var c in candidates)
        {
            if (c.BoundsWidth > 0 && c.BoundsHeight > 0)
            {
                double dpiX = c.PixelWidth / (c.BoundsWidth / 72.0);
                double dpiY = c.PixelHeight / (c.BoundsHeight / 72.0);
                double effectiveDpi = Math.Max(dpiX, dpiY);
                maxDpi = Math.Max(maxDpi, effectiveDpi);
            }
        }

        return (int)Math.Clamp(Math.Round(maxDpi), 72, 600);
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (!Path.IsPathRooted(outputPath))
        {
            throw new ArgumentException("outputPath must be an absolute path.");
        }

        if (outputPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("outputPath must not contain path traversal sequences (..).");
        }

        if (!Directory.Exists(outputPath))
        {
            throw new ArgumentException($"outputPath directory does not exist: {outputPath}");
        }
    }

    private static string SanitizeFileNameStem(string pdfPath)
    {
        string stem = Path.GetFileNameWithoutExtension(pdfPath);
        string sanitized = string.Concat(stem.Where(c => !InvalidFileNameChars.Contains(c)));
        return string.IsNullOrEmpty(sanitized) ? "pdf" : sanitized;
    }

    internal record FallbackCandidate(
        int ImageIndex,
        string FilePath,
        double BoundsLeft,
        double BoundsBottom,
        double BoundsWidth,
        double BoundsHeight,
        int PixelWidth,
        int PixelHeight);
}
