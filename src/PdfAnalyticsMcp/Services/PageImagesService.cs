using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using PdfAnalyticsMcp.Models;
using PDFiumCore;

namespace PdfAnalyticsMcp.Services;

public class PageImagesService(
    IPdfiumService pdfiumService,
    ILogger<PageImagesService> logger) : IPageImagesService
{
    private const int MaxRecursionDepth = 64;
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public async Task<PageImagesDto> ExtractAsync(string pdfPath, int page, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (outputPath is not null)
        {
            ValidateOutputPath(outputPath);
        }

        string? sanitizedStem = outputPath is not null ? SanitizeFileNameStem(pdfPath) : null;

        return await pdfiumService.ExecuteAsync(pdfPath, page, (document, loadedPage) =>
        {
            var (pageWidth, pageHeight) = pdfiumService.GetPageSize(document, page - 1);

            // Dictionary: native object pointer -> (imageIndex, filePath)
            var uniqueImages = new Dictionary<IntPtr, (int Index, string? FilePath)>();
            var imageResults = new List<ImageElementDto>();

            try
            {
                EnumerateObjects(document, loadedPage, loadedPage, outputPath, sanitizedStem, page,
                    uniqueImages, imageResults, depth: 0);
            }
            catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
            {
                throw new ArgumentException($"An error occurred extracting images from page {page}.");
            }

            return new PageImagesDto(
                page,
                FormatUtils.RoundCoordinate(pageWidth),
                FormatUtils.RoundCoordinate(pageHeight),
                imageResults);
        }, cancellationToken);
    }

    private void EnumerateObjects(
        FpdfDocumentT document,
        FpdfPageT page,
        FpdfPageT topLevelPage,
        string? outputPath,
        string? sanitizedStem,
        int pageNumber,
        Dictionary<IntPtr, (int Index, string? FilePath)> uniqueImages,
        List<ImageElementDto> imageResults,
        int depth)
    {
        if (depth > MaxRecursionDepth)
            return;

        int objCount = fpdf_edit.FPDFPageCountObjects(page);
        for (int i = 0; i < objCount; i++)
        {
            var obj = fpdf_edit.FPDFPageGetObject(page, i);
            int objType = fpdf_edit.FPDFPageObjGetType(obj);

            if (objType == 3) // FPDF_PAGEOBJ_IMAGE
            {
                ProcessImageObject(document, topLevelPage, obj, outputPath, sanitizedStem, pageNumber,
                    uniqueImages, imageResults);
            }
            else if (objType == 5) // FPDF_PAGEOBJ_FORM
            {
                EnumerateFormObjects(document, obj, topLevelPage, outputPath, sanitizedStem, pageNumber,
                    uniqueImages, imageResults, depth + 1);
            }
        }
    }

    private void EnumerateFormObjects(
        FpdfDocumentT document,
        FpdfPageobjectT formObj,
        FpdfPageT topLevelPage,
        string? outputPath,
        string? sanitizedStem,
        int pageNumber,
        Dictionary<IntPtr, (int Index, string? FilePath)> uniqueImages,
        List<ImageElementDto> imageResults,
        int depth)
    {
        if (depth > MaxRecursionDepth)
            return;

        int childCount = fpdf_edit.FPDFFormObjCountObjects(formObj);
        for (uint i = 0; i < childCount; i++)
        {
            var child = fpdf_edit.FPDFFormObjGetObject(formObj, i);
            int childType = fpdf_edit.FPDFPageObjGetType(child);

            if (childType == 3) // FPDF_PAGEOBJ_IMAGE
            {
                ProcessImageObject(document, topLevelPage, child, outputPath, sanitizedStem, pageNumber,
                    uniqueImages, imageResults);
            }
            else if (childType == 5) // FPDF_PAGEOBJ_FORM
            {
                EnumerateFormObjects(document, child, topLevelPage, outputPath, sanitizedStem, pageNumber,
                    uniqueImages, imageResults, depth + 1);
            }
        }
    }

    private void ProcessImageObject(
        FpdfDocumentT document,
        FpdfPageT page,
        FpdfPageobjectT obj,
        string? outputPath,
        string? sanitizedStem,
        int pageNumber,
        Dictionary<IntPtr, (int Index, string? FilePath)> uniqueImages,
        List<ImageElementDto> imageResults)
    {
        try
        {
            // Extract bounding box
            float left = 0, bottom = 0, right = 0, top = 0;
            fpdf_edit.FPDFPageObjGetBounds(obj, ref left, ref bottom, ref right, ref top);

            double x = FormatUtils.RoundCoordinate(left);
            double y = FormatUtils.RoundCoordinate(bottom);
            double w = FormatUtils.RoundCoordinate(right - left);
            double h = FormatUtils.RoundCoordinate(top - bottom);

            // Extract pixel dimensions and bits per pixel
            var metadata = new FPDF_IMAGEOBJ_METADATA();
            fpdf_edit.FPDFImageObjGetImageMetadata(obj, page, metadata);

            int pixelWidth = (int)metadata.Width;
            int pixelHeight = (int)metadata.Height;
            int bitsPerPixel = (int)metadata.BitsPerPixel;

            // Check if we've seen this image object before (deduplication)
            IntPtr objPtr = obj.__Instance;
            string? filePath = null;

            if (uniqueImages.TryGetValue(objPtr, out var existing))
            {
                // Duplicate — reuse existing index and file path
                filePath = existing.FilePath;
            }
            else
            {
                // New unique image
                int imageIndex = uniqueImages.Count + 1;

                if (outputPath is not null)
                {
                    filePath = ExtractImageToDisk(document, page, obj, outputPath, sanitizedStem!, pageNumber, imageIndex);
                }

                uniqueImages[objPtr] = (imageIndex, filePath);
            }

            imageResults.Add(new ImageElementDto(x, y, w, h, pixelWidth, pixelHeight, bitsPerPixel, filePath));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skipping image due to metadata extraction error on page {Page}.", pageNumber);
        }
    }

    private string? ExtractImageToDisk(
        FpdfDocumentT document,
        FpdfPageT page,
        FpdfPageobjectT obj,
        string outputPath,
        string sanitizedStem,
        int pageNumber,
        int imageIndex)
    {
        try
        {
            // Determine if source is JPEG-encoded — if so, render via PDFium and re-encode as JPEG
            // to preserve smaller file sizes while applying PDF-level color corrections (Decode array,
            // colorspace mapping). Raw JPEG extraction is unsafe because it bypasses these corrections,
            // causing inverted colors for images with non-identity Decode arrays.
            bool isJpeg = IsJpegEncoded(obj);

            if (isJpeg)
            {
                return ExtractImageAsJpeg(document, page, obj, outputPath, sanitizedStem, pageNumber, imageIndex);
            }

            // Non-JPEG images: render via PDFium → PNG (preserves alpha)
            return ExtractImageAsPng(document, page, obj, outputPath, sanitizedStem, pageNumber, imageIndex);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract image {Index} on page {Page}.", imageIndex, pageNumber);
            return null;
        }
    }

    private static bool IsJpegEncoded(FpdfPageobjectT obj)
    {
        int filterCount = fpdf_edit.FPDFImageObjGetImageFilterCount(obj);
        if (filterCount != 1)
            return false;

        string? filterName = GetImageFilterName(obj, 0);
        return filterName == "DCTDecode";
    }

    private static string? GetImageFilterName(FpdfPageobjectT obj, int filterIndex)
    {
        ulong bufLen = fpdf_edit.FPDFImageObjGetImageFilter(obj, filterIndex, IntPtr.Zero, 0);
        if (bufLen == 0)
            return null;

        IntPtr buf = Marshal.AllocHGlobal((int)bufLen);
        try
        {
            fpdf_edit.FPDFImageObjGetImageFilter(obj, filterIndex, buf, (uint)bufLen);
            return Marshal.PtrToStringUTF8(buf);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private string? ExtractImageAsJpeg(
        FpdfDocumentT document,
        FpdfPageT page,
        FpdfPageobjectT obj,
        string outputPath,
        string sanitizedStem,
        int pageNumber,
        int imageIndex)
    {
        string fileName = $"{sanitizedStem}_p{pageNumber}_img{imageIndex}.jpg";
        string fullPath = Path.Combine(outputPath, fileName);

        FpdfBitmapT? bitmap = null;
        try
        {
            bitmap = fpdf_edit.FPDFImageObjGetRenderedBitmap(document, page, obj);
            if (bitmap == null)
            {
                logger.LogWarning("FPDFImageObj_GetRenderedBitmap returned null for image {Index} on page {Page}. Falling back to FPDFImageObj_GetBitmap.", imageIndex, pageNumber);
                bitmap = fpdf_edit.FPDFImageObjGetBitmap(obj);
                if (bitmap == null)
                {
                    logger.LogWarning("FPDFImageObj_GetBitmap also returned null for image {Index} on page {Page}.", imageIndex, pageNumber);
                    return null;
                }
            }

            int bitmapWidth = fpdfview.FPDFBitmapGetWidth(bitmap);
            int bitmapHeight = fpdfview.FPDFBitmapGetHeight(bitmap);
            IntPtr buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
            int stride = fpdfview.FPDFBitmapGetStride(bitmap);
            int format = fpdfview.FPDFBitmapGetFormat(bitmap);

            byte[] bgraData = ConvertToBgra(buffer, bitmapWidth, bitmapHeight, stride, format);

            byte[] jpegBytes = JpegEncoder.Encode(bgraData, bitmapWidth, bitmapHeight, quality: 90);
            File.WriteAllBytes(fullPath, jpegBytes);
            return Path.GetFullPath(fullPath);
        }
        finally
        {
            if (bitmap != null)
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
    }

    private string? ExtractImageAsPng(
        FpdfDocumentT document,
        FpdfPageT page,
        FpdfPageobjectT obj,
        string outputPath,
        string sanitizedStem,
        int pageNumber,
        int imageIndex)
    {
        string fileName = $"{sanitizedStem}_p{pageNumber}_img{imageIndex}.png";
        string fullPath = Path.Combine(outputPath, fileName);

        FpdfBitmapT? bitmap = null;
        try
        {
            bitmap = fpdf_edit.FPDFImageObjGetRenderedBitmap(document, page, obj);
            if (bitmap == null)
            {
                logger.LogWarning("FPDFImageObj_GetRenderedBitmap returned null for image {Index} on page {Page}. Falling back to FPDFImageObj_GetBitmap.", imageIndex, pageNumber);
                bitmap = fpdf_edit.FPDFImageObjGetBitmap(obj);
                if (bitmap == null)
                {
                    logger.LogWarning("FPDFImageObj_GetBitmap also returned null for image {Index} on page {Page}.", imageIndex, pageNumber);
                    return null;
                }
            }

            int bitmapWidth = fpdfview.FPDFBitmapGetWidth(bitmap);
            int bitmapHeight = fpdfview.FPDFBitmapGetHeight(bitmap);
            IntPtr buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
            int stride = fpdfview.FPDFBitmapGetStride(bitmap);
            int format = fpdfview.FPDFBitmapGetFormat(bitmap);

            byte[] bgraData = ConvertToBgra(buffer, bitmapWidth, bitmapHeight, stride, format);

            byte[] pngBytes = PngEncoder.Encode(bgraData, bitmapWidth, bitmapHeight, preserveAlpha: true);
            File.WriteAllBytes(fullPath, pngBytes);
            return Path.GetFullPath(fullPath);
        }
        finally
        {
            if (bitmap != null)
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }
        }
    }

    /// <summary>
    /// Converts raw bitmap data from any PDFium format to BGRA (4 bytes per pixel).
    /// </summary>
    internal static byte[] ConvertToBgra(IntPtr buffer, int width, int height, int stride, int format)
    {
        int bgraRowBytes = width * 4;
        byte[] bgraData = new byte[width * height * 4];

        switch (format)
        {
            case 4: // FPDFBitmap_BGRA — already BGRA
            case 3: // FPDFBitmap_BGRx — 4 bytes per pixel, alpha unused (treat as opaque)
            {
                if (stride == bgraRowBytes)
                {
                    Marshal.Copy(buffer, bgraData, 0, bgraData.Length);
                }
                else
                {
                    for (int row = 0; row < height; row++)
                    {
                        Marshal.Copy(buffer + row * stride, bgraData, row * bgraRowBytes, bgraRowBytes);
                    }
                }
                // For BGRx, set alpha to 255
                if (format == 3)
                {
                    for (int i = 3; i < bgraData.Length; i += 4)
                        bgraData[i] = 255;
                }
                break;
            }
            case 2: // FPDFBitmap_BGR — 3 bytes per pixel
            {
                int bgrRowBytes = width * 3;
                byte[] rowBuf = new byte[bgrRowBytes];
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(buffer + row * stride, rowBuf, 0, bgrRowBytes);
                    for (int x = 0; x < width; x++)
                    {
                        int srcIdx = x * 3;
                        int dstIdx = (row * width + x) * 4;
                        bgraData[dstIdx] = rowBuf[srcIdx];         // B
                        bgraData[dstIdx + 1] = rowBuf[srcIdx + 1]; // G
                        bgraData[dstIdx + 2] = rowBuf[srcIdx + 2]; // R
                        bgraData[dstIdx + 3] = 255;                // A (opaque)
                    }
                }
                break;
            }
            case 1: // FPDFBitmap_Gray — 1 byte per pixel
            {
                byte[] rowBuf = new byte[width];
                for (int row = 0; row < height; row++)
                {
                    Marshal.Copy(buffer + row * stride, rowBuf, 0, width);
                    for (int x = 0; x < width; x++)
                    {
                        int dstIdx = (row * width + x) * 4;
                        byte gray = rowBuf[x];
                        bgraData[dstIdx] = gray;     // B
                        bgraData[dstIdx + 1] = gray; // G
                        bgraData[dstIdx + 2] = gray; // R
                        bgraData[dstIdx + 3] = 255;  // A
                    }
                }
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported PDFium bitmap format: {format}");
        }

        return bgraData;
    }

    private static void ValidateOutputPath(string outputPath)
    {
        if (!Path.IsPathRooted(outputPath))
        {
            throw new ArgumentException("outputPath must be an absolute path.");
        }

        if (outputPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid output path.");
        }

        if (!Directory.Exists(outputPath))
        {
            throw new ArgumentException($"Output directory does not exist: {outputPath}");
        }
    }

    private static string SanitizeFileNameStem(string pdfPath)
    {
        string stem = Path.GetFileNameWithoutExtension(pdfPath);
        string sanitized = string.Concat(stem.Where(c => !InvalidFileNameChars.Contains(c)));
        return string.IsNullOrEmpty(sanitized) ? "pdf" : sanitized;
    }
}
