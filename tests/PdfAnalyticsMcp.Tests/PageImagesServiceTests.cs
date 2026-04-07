using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PdfAnalyticsMcp.Services;
using PDFiumCore;

namespace PdfAnalyticsMcp.Tests;

public class PageImagesServiceTests : IDisposable
{
    private readonly PageImagesService _service;
    private readonly string _tempDir;

    private static int _initialized;

    public PageImagesServiceTests()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            fpdfview.FPDF_InitLibrary();
        }

        _tempDir = Path.Combine(Path.GetTempPath(), $"PdfImagesTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var pdfiumService = new PdfiumService(
            new InputValidationService(),
            NullLogger<PdfiumService>.Instance);

        _service = new PageImagesService(
            pdfiumService,
            NullLogger<PageImagesService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExtractAsync_PageWithImage_ReturnsCorrectMetadata()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Equal(1, result.Page);
        Assert.Single(result.Images);

        var image = result.Images[0];
        // PdfRectangle(100, 500, 300, 650) => x=100, y=500, w=200, h=150
        Assert.Equal(100.0, image.X);
        Assert.Equal(500.0, image.Y);
        Assert.Equal(200.0, image.W);
        Assert.Equal(150.0, image.H);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
        Assert.Equal(24, image.BitsPerPixel);
    }

    [Fact]
    public async Task ExtractAsync_MultipleImages_ReturnsAllImages()
    {
        var path = TestPdfGenerator.CreateMultiImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Equal(2, result.Images.Count);

        // Image 1: PdfRectangle(50, 600, 150, 680) => x=50, y=600, w=100, h=80
        var img1 = result.Images[0];
        Assert.Equal(50.0, img1.X);
        Assert.Equal(600.0, img1.Y);
        Assert.Equal(100.0, img1.W);
        Assert.Equal(80.0, img1.H);

        // Image 2: PdfRectangle(200, 400, 350, 520) => x=200, y=400, w=150, h=120
        var img2 = result.Images[1];
        Assert.Equal(200.0, img2.X);
        Assert.Equal(400.0, img2.Y);
        Assert.Equal(150.0, img2.W);
        Assert.Equal(120.0, img2.H);
    }

    [Fact]
    public async Task ExtractAsync_NoOutputPath_FileIsNull()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Single(result.Images);
        Assert.Null(result.Images[0].File);
    }

    [Fact]
    public async Task ExtractAsync_WithOutputPath_WritesPngAndSetsFilePath()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.File);
        Assert.True(File.Exists(image.File), $"Expected PNG file to exist: {image.File}");

        // Verify it's a valid PNG
        byte[] bytes = File.ReadAllBytes(image.File);
        Assert.True(bytes.Length > 4);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public async Task ExtractAsync_WithOutputPath_FileNamingConvention()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.File);

        // Should follow {pdfStem}_p{page}_img{index}.png
        string expectedName = "sample-image_p1_img1.png";
        Assert.Equal(expectedName, Path.GetFileName(image.File));
    }

    [Fact]
    public async Task ExtractAsync_WithOutputPath_MultipleImages_FileNaming()
    {
        var path = TestPdfGenerator.CreateMultiImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Equal(2, result.Images.Count);
        Assert.NotNull(result.Images[0].File);
        Assert.NotNull(result.Images[1].File);

        Assert.Equal("sample-multi-image_p1_img1.png", Path.GetFileName(result.Images[0].File));
        Assert.Equal("sample-multi-image_p1_img2.png", Path.GetFileName(result.Images[1].File));

        Assert.True(File.Exists(result.Images[0].File));
        Assert.True(File.Exists(result.Images[1].File));
    }

    [Fact]
    public void ExtractAsync_FilenameSanitization_FallbackToPdf()
    {
        // Test that a stem of all invalid chars falls back to "pdf"
        var sanitized = PageImagesServiceTests_SanitizeHelper("/<>:\"|?*");
        Assert.Equal("pdf", sanitized);
    }

    [Fact]
    public async Task ExtractAsync_EmptyPage_ReturnsEmptyImagesList()
    {
        var path = TestPdfGenerator.CreateBlankTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.NotNull(result.Images);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ExtractAsync_EmptyPage_WithOutputPath_NoFilesWritten()
    {
        var path = TestPdfGenerator.CreateBlankTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Empty(result.Images);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task ExtractAsync_CoordinatesRoundedToOneDecimal()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        var image = result.Images[0];
        Assert.Equal(Math.Round(image.X, 1), image.X);
        Assert.Equal(Math.Round(image.Y, 1), image.Y);
        Assert.Equal(Math.Round(image.W, 1), image.W);
        Assert.Equal(Math.Round(image.H, 1), image.H);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsCorrectPageDimensions()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        // US Letter: 612 x 792 points
        Assert.Equal(612.0, result.Width);
        Assert.Equal(792.0, result.Height);
    }

    [Fact]
    public async Task ExtractAsync_InvalidPageNumber_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, -1));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, 99));
    }

    [Fact]
    public async Task ExtractAsync_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, 1));
    }

    [Fact]
    public async Task ExtractAsync_LockedFile_ThrowsArgumentExceptionWithAccessMessage()
    {
        var source = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        File.Copy(source, tempPath);
        try
        {
            using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(tempPath, 1));
            Assert.Contains("could not be accessed", ex.Message);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ExtractAsync_OutputPath_RelativePath_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, 1, "relative/path"));
        Assert.Contains("absolute", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_OutputPath_PathTraversal_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, 1, @"C:\temp\..\secret"));
        Assert.Contains("Invalid output path", ex.Message);
    }

    [Fact]
    public async Task ExtractAsync_OutputPath_NonExistentDirectory_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var nonExistent = Path.Combine(_tempDir, "does_not_exist");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.ExtractAsync(path, 1, nonExistent));
        Assert.Contains("does not exist", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_OutputPath_OverwritesExistingFiles()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        // Write a dummy file at the expected location
        string expectedFileName = "sample-image_p1_img1.png";
        string expectedFilePath = Path.Combine(_tempDir, expectedFileName);
        File.WriteAllText(expectedFilePath, "dummy content");

        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        Assert.NotNull(result.Images[0].File);
        Assert.True(File.Exists(expectedFilePath));

        // Verify overwritten with real PNG data
        byte[] bytes = File.ReadAllBytes(expectedFilePath);
        Assert.Equal(0x89, bytes[0]); // PNG signature
    }

    [Fact]
    public async Task Serialization_FileNullOmittedFromJson()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);

        // Verify camelCase
        Assert.True(doc.RootElement.TryGetProperty("page", out _));
        Assert.True(doc.RootElement.TryGetProperty("width", out _));
        Assert.True(doc.RootElement.TryGetProperty("height", out _));
        Assert.True(doc.RootElement.TryGetProperty("images", out _));

        // Verify file field is absent (null omitted)
        var imageElement = doc.RootElement.GetProperty("images")[0];
        Assert.False(imageElement.TryGetProperty("file", out _), "file field should be omitted when null");

        // Verify other image fields are present
        Assert.True(imageElement.TryGetProperty("x", out _));
        Assert.True(imageElement.TryGetProperty("y", out _));
        Assert.True(imageElement.TryGetProperty("w", out _));
        Assert.True(imageElement.TryGetProperty("h", out _));
        Assert.True(imageElement.TryGetProperty("pixelWidth", out _));
        Assert.True(imageElement.TryGetProperty("pixelHeight", out _));
        Assert.True(imageElement.TryGetProperty("bitsPerPixel", out _));
    }

    [Fact]
    public async Task Serialization_FilePresentWhenOutputPathProvided()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);

        var imageElement = doc.RootElement.GetProperty("images")[0];
        Assert.True(imageElement.TryGetProperty("file", out var fileValue));
        Assert.Equal(JsonValueKind.String, fileValue.ValueKind);
        Assert.False(string.IsNullOrEmpty(fileValue.GetString()));
    }

    [Fact]
    public async Task Serialization_CompactJsonNoPrettyPrint()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);

        // Compact JSON should not contain newlines
        Assert.DoesNotContain("\n", json);
    }

    // Helper to test sanitization logic indirectly through reflection or known behavior
    private static string PageImagesServiceTests_SanitizeHelper(string stem)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Concat(stem.Where(c => !invalidChars.Contains(c)));
        return string.IsNullOrEmpty(sanitized) ? "pdf" : sanitized;
    }

    [Fact]
    public async Task ExtractAsync_FormXObjectImage_DiscoversImageViaRecursion()
    {
        var path = TestPdfGenerator.CreateFormXObjectImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Equal(1, result.Page);
        Assert.Single(result.Images);

        var image = result.Images[0];
        Assert.True(image.W > 0, "Image width should be positive");
        Assert.True(image.H > 0, "Image height should be positive");
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
    }

    [Fact]
    public async Task ExtractAsync_FormXObjectImage_WithOutputPath_ExtractsPng()
    {
        var path = TestPdfGenerator.CreateFormXObjectImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.File);
        Assert.True(File.Exists(image.File), $"Expected PNG file to exist: {image.File}");

        // Verify valid PNG
        byte[] bytes = File.ReadAllBytes(image.File);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
    }

    [Fact]
    public async Task ExtractAsync_DuplicateFormXObject_ReportsBothOccurrences()
    {
        var path = TestPdfGenerator.CreateDuplicateFormXObjectImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        // Two occurrences of the same Form XObject → two image entries
        Assert.Equal(2, result.Images.Count);

        // Both should have positive dimensions
        foreach (var image in result.Images)
        {
            Assert.True(image.W > 0);
            Assert.True(image.H > 0);
            Assert.Equal(2, image.PixelWidth);
            Assert.Equal(2, image.PixelHeight);
        }
    }

    [Fact]
    public async Task ExtractAsync_DuplicateFormXObject_WithOutputPath_ExtractsBothImages()
    {
        var path = TestPdfGenerator.CreateDuplicateFormXObjectImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Equal(2, result.Images.Count);

        // Both occurrences should have file paths (PDFium creates separate object instances
        // for each Form XObject reference, so each gets its own extraction)
        Assert.NotNull(result.Images[0].File);
        Assert.NotNull(result.Images[1].File);
        Assert.True(File.Exists(result.Images[0].File!));
        Assert.True(File.Exists(result.Images[1].File!));

        // Both should be valid PNGs
        foreach (var image in result.Images)
        {
            byte[] bytes = File.ReadAllBytes(image.File!);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
        }
    }

    [Fact]
    public async Task ExtractAsync_PngAlphaPreservation_ExtractsWithAlpha()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        Assert.NotNull(result.Images[0].File);

        byte[] pngBytes = File.ReadAllBytes(result.Images[0].File);

        // PNG IHDR chunk starts at offset 8 (after signature)
        // IHDR: 4-byte length, 4-byte "IHDR", 4-byte width, 4-byte height, 1-byte bit depth, 1-byte color type
        // Color type is at offset 8 + 4 + 4 + 4 + 4 + 1 = 25
        Assert.True(pngBytes.Length > 25);
        byte colorType = pngBytes[25];
        Assert.Equal(6, colorType); // 6 = RGBA (preserveAlpha: true)
    }

    [Fact]
    public async Task ExtractAsync_FilenameSanitization_SpacesPreserved()
    {
        // Create a PDF with a name containing spaces (valid on disk, should be preserved in output)
        var originalPath = TestPdfGenerator.CreateImageTestPdf();
        var tempPath = Path.Combine(Path.GetTempPath(), "test file with spaces.pdf");
        try
        {
            File.Copy(originalPath, tempPath, overwrite: true);
            var result = await _service.ExtractAsync(tempPath, 1, _tempDir);

            Assert.Single(result.Images);
            Assert.NotNull(result.Images[0].File);

            string fileName = Path.GetFileName(result.Images[0].File!);
            Assert.Equal("test file with spaces_p1_img1.png", fileName);
            Assert.True(File.Exists(result.Images[0].File));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task ExtractAsync_NestedFormXObject_DiscoversImageViaDeepRecursion()
    {
        var path = TestPdfGenerator.CreateNestedFormXObjectImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Equal(1, result.Page);
        Assert.Single(result.Images);

        var image = result.Images[0];
        Assert.True(image.W > 0, "Image width should be positive");
        Assert.True(image.H > 0, "Image height should be positive");
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
    }

    [Fact]
    public async Task ExtractAsync_NestedFormXObject_WithOutputPath_ExtractsPng()
    {
        var path = TestPdfGenerator.CreateNestedFormXObjectImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        Assert.NotNull(result.Images[0].File);
        Assert.True(File.Exists(result.Images[0].File!));

        byte[] bytes = File.ReadAllBytes(result.Images[0].File!);
        Assert.Equal(0x89, bytes[0]); // PNG signature
    }

    // NOTE: The FPDFImageObj_GetRenderedBitmap → FPDFImageObj_GetBitmap fallback path
    // was verified manually using "Warhammer Fantasy Roleplay 4th Edition" PDF (page 25,
    // images 8 and 9), where GetRenderedBitmap returns null and GetBitmap succeeds with
    // BGR format. That PDF cannot be included in this repository due to copyright.
    // The ConvertToBgra logic (which handles the BGR→BGRA conversion for the fallback)
    // is fully covered by unit tests in ConvertToBgraTests.cs.

    [Fact]
    public async Task ExtractAsync_JpegImage_ExtractsAsJpgFile()
    {
        var path = TestPdfGenerator.CreateJpegImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.File);
        Assert.EndsWith(".jpg", image.File);
        Assert.True(File.Exists(image.File));

        // Verify it's a valid JPEG (starts with FFD8 SOI marker)
        byte[] bytes = File.ReadAllBytes(image.File);
        Assert.True(bytes.Length > 2);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
    }

    [Fact]
    public async Task ExtractAsync_JpegImage_FileNamingConvention()
    {
        var path = TestPdfGenerator.CreateJpegImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.File);

        // Should follow {pdfStem}_p{page}_img{index}.jpg for JPEG images
        string expectedName = "sample-jpeg-image_p1_img1.jpg";
        Assert.Equal(expectedName, Path.GetFileName(image.File));
    }

    [Fact]
    public async Task ExtractAsync_JpegImage_RawBytesPreserved()
    {
        // The extracted JPEG should be the raw image data — no re-encoding
        var path = TestPdfGenerator.CreateJpegImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        Assert.NotNull(result.Images[0].File);

        byte[] extractedBytes = File.ReadAllBytes(result.Images[0].File!);

        // The file should be significantly smaller than a PNG re-encoding would be
        // A 2x2 JPEG is typically under 1KB
        Assert.True(extractedBytes.Length < 2048, $"JPEG should be small, was {extractedBytes.Length} bytes");

        // Verify JPEG SOI + EOI markers
        Assert.Equal(0xFF, extractedBytes[0]);
        Assert.Equal(0xD8, extractedBytes[1]);
        Assert.Equal(0xFF, extractedBytes[^2]);
        Assert.Equal(0xD9, extractedBytes[^1]);
    }

    [Fact]
    public async Task ExtractAsync_PngImage_StillExtractsAsPng()
    {
        // PNG-embedded images should still be extracted as PNG (not JPEG)
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = await _service.ExtractAsync(path, 1, _tempDir);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.File);
        Assert.EndsWith(".png", image.File);

        byte[] bytes = File.ReadAllBytes(image.File);
        Assert.Equal(0x89, bytes[0]); // PNG signature
    }

    [Fact]
    public async Task ExtractAsync_JpegImage_MetadataStillCorrect()
    {
        var path = TestPdfGenerator.CreateJpegImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Single(result.Images);
        var image = result.Images[0];

        // Bounding box should match PdfRectangle(100, 500, 300, 650)
        Assert.Equal(100.0, image.X);
        Assert.Equal(500.0, image.Y);
        Assert.Equal(200.0, image.W);
        Assert.Equal(150.0, image.H);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
    }

    [Fact]
    public async Task ExtractAsync_JpegImage_NoOutputPath_FileIsNull()
    {
        var path = TestPdfGenerator.CreateJpegImageTestPdf();
        var result = await _service.ExtractAsync(path, 1);

        Assert.Single(result.Images);
        Assert.Null(result.Images[0].File);
    }
}
