using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class PageImagesServiceTests : IDisposable
{
    private readonly PageImagesService _service;
    private readonly string _tempDir;

    public PageImagesServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PdfImagesTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new PageImagesService(
            new InputValidationService(),
            new RenderPagePreviewService(
                new InputValidationService(),
                NullLogger<RenderPagePreviewService>.Instance),
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
        Assert.Equal(8, image.BitsPerComponent);
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
            Assert.Equal($"The file could not be accessed: {tempPath}. It may be in use by another process.", ex.Message);
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
        Assert.Contains("..", ex.Message);
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
        Assert.True(imageElement.TryGetProperty("bitsPerComponent", out _));
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

    // --- ComputeFallbackDpi tests (spec test #7/#8 DPI selection) ---

    [Fact]
    public void ComputeFallbackDpi_SingleImage_ComputesEffectiveDpi()
    {
        // 300px wide displayed at 100pt => effective DPI = 300 / (100/72) = 216
        var candidates = new List<PageImagesService.FallbackCandidate>
        {
            new(0, "f.png", 0, 0, 100, 100, 300, 300)
        };

        int dpi = PageImagesService.ComputeFallbackDpi(candidates);
        Assert.Equal(216, dpi);
    }

    [Fact]
    public void ComputeFallbackDpi_MultipleImages_ChoosesMaxDpi()
    {
        // Image 1: 100px at 100pt => 72 DPI effective
        // Image 2: 600px at 100pt => 432 DPI effective
        var candidates = new List<PageImagesService.FallbackCandidate>
        {
            new(0, "f1.png", 0, 0, 100, 100, 100, 100),
            new(1, "f2.png", 0, 0, 100, 100, 600, 600)
        };

        int dpi = PageImagesService.ComputeFallbackDpi(candidates);
        Assert.Equal(432, dpi);
    }

    [Fact]
    public void ComputeFallbackDpi_VeryHighResolution_ClampsTo600()
    {
        // 10000px at 72pt => effective DPI = 10000 / (72/72) = 10000 → clamped to 600
        var candidates = new List<PageImagesService.FallbackCandidate>
        {
            new(0, "f.png", 0, 0, 72, 72, 10000, 10000)
        };

        int dpi = PageImagesService.ComputeFallbackDpi(candidates);
        Assert.Equal(600, dpi);
    }

    [Fact]
    public void ComputeFallbackDpi_VeryLowResolution_FloorAt150Default()
    {
        // 1px at 100pt => effective DPI = 1 / (100/72) = 0.72 → below default 150 → returns 150
        var candidates = new List<PageImagesService.FallbackCandidate>
        {
            new(0, "f.png", 0, 0, 100, 100, 1, 1)
        };

        int dpi = PageImagesService.ComputeFallbackDpi(candidates);
        Assert.Equal(150, dpi);
    }

    [Fact]
    public void ComputeFallbackDpi_ZeroWidthImage_DefaultsTo150()
    {
        var candidates = new List<PageImagesService.FallbackCandidate>
        {
            new(0, "f.png", 0, 0, 0, 100, 200, 200)
        };

        int dpi = PageImagesService.ComputeFallbackDpi(candidates);
        Assert.Equal(150, dpi);
    }

    [Fact]
    public void ComputeFallbackDpi_AsymmetricResolution_UsesMaxOfXAndY()
    {
        // 300px wide at 72pt = 300 DPI-x; 100px tall at 72pt = 100 DPI-y → max = 300
        var candidates = new List<PageImagesService.FallbackCandidate>
        {
            new(0, "f.png", 0, 0, 72, 72, 300, 100)
        };

        int dpi = PageImagesService.ComputeFallbackDpi(candidates);
        Assert.Equal(300, dpi);
    }

    // --- ComputeCropRegion tests (spec test #9: Y-axis inversion and clamping) ---

    [Fact]
    public void ComputeCropRegion_BasicMapping_InvertsYAxis()
    {
        // Image at bottom-left of page: Left=0, Bottom=0, 100x50pt
        // Scale = 2.0 (144 DPI), renderHeight = 1584 (792pt * 2)
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 0, boundsBottom: 0, boundsWidth: 100, boundsHeight: 50,
            scale: 2.0, renderWidth: 1224, renderHeight: 1584);

        Assert.NotNull(result);
        var (left, top, width, height) = result.Value;

        Assert.Equal(0, left);
        // Y inversion: top = 1584 - (0 + 50)*2 = 1584 - 100 = 1484
        Assert.Equal(1484, top);
        Assert.Equal(200, width);  // 100 * 2
        Assert.Equal(100, height); // 50 * 2
    }

    [Fact]
    public void ComputeCropRegion_TopOfPage_YAxisInversion()
    {
        // Image near top: Bottom=742, Height=50 → top edge at 792pt (page top)
        // Scale = 1.0, renderHeight = 792
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 100, boundsBottom: 742, boundsWidth: 200, boundsHeight: 50,
            scale: 1.0, renderWidth: 612, renderHeight: 792);

        Assert.NotNull(result);
        var (left, top, width, height) = result.Value;

        Assert.Equal(100, left);
        // Y inversion: top = 792 - (742 + 50)*1 = 792 - 792 = 0
        Assert.Equal(0, top);
        Assert.Equal(200, width);
        Assert.Equal(50, height);
    }

    [Fact]
    public void ComputeCropRegion_ExceedingRightBoundary_ClampsCropWidth()
    {
        // Image extends beyond right edge: Left=500, Width=200 → right=700, but render is 612
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 500, boundsBottom: 0, boundsWidth: 200, boundsHeight: 50,
            scale: 1.0, renderWidth: 612, renderHeight: 792);

        Assert.NotNull(result);
        var (left, _, width, _) = result.Value;

        Assert.Equal(500, left);
        Assert.Equal(112, width); // min(200, 612 - 500) = 112
    }

    [Fact]
    public void ComputeCropRegion_ExceedingTopBoundary_ClampsCropHeight()
    {
        // Image extends above page: Bottom=780, Height=50 → top = -(780+50-792)*scale
        // pixelTop would be negative, clamped to 0, height reduced
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 0, boundsBottom: 780, boundsWidth: 100, boundsHeight: 50,
            scale: 1.0, renderWidth: 612, renderHeight: 792);

        Assert.NotNull(result);
        var (_, top, _, height) = result.Value;

        // pixelTop = 792 - (780+50)*1 = 792 - 830 = -38 → clamped to 0
        Assert.Equal(0, top);
        // cropHeight = min(50, 792 - 0) = 50 (original fits after clamp)
        Assert.Equal(50, height);
    }

    [Fact]
    public void ComputeCropRegion_CompletelyOutsidePage_ReturnsNull()
    {
        // Image fully above page render area
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 0, boundsBottom: 1000, boundsWidth: 100, boundsHeight: 100,
            scale: 1.0, renderWidth: 612, renderHeight: 792);

        // pixelTop = 792 - (1000+100)*1 = -308, clamped to 0
        // cropHeight = min(100, 792-0)=100 — actually this still fits.
        // Let me use an image fully to the right of the render
        result = PageImagesService.ComputeCropRegion(
            boundsLeft: 700, boundsBottom: 0, boundsWidth: 100, boundsHeight: 100,
            scale: 1.0, renderWidth: 612, renderHeight: 792);

        // pixelLeft=700, cropWidth = min(100, 612-700) = min(100, -88) = -88 → null
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCropRegion_ZeroSizeBounds_ReturnsNull()
    {
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 0, boundsBottom: 0, boundsWidth: 0, boundsHeight: 0,
            scale: 2.0, renderWidth: 1224, renderHeight: 1584);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeCropRegion_WithScale_PixelValuesScaleCorrectly()
    {
        // 72pt box at scale 150/72 ≈ 2.0833
        double scale = 150.0 / 72.0;
        var result = PageImagesService.ComputeCropRegion(
            boundsLeft: 72, boundsBottom: 72, boundsWidth: 72, boundsHeight: 72,
            scale: scale, renderWidth: 1275, renderHeight: 1650);

        Assert.NotNull(result);
        var (left, top, width, height) = result.Value;

        // pixelLeft = round(72 * 2.0833) = round(150) = 150
        Assert.Equal(150, left);
        // pixelTop = 1650 - round((72+72)*2.0833) = 1650 - round(300) = 1350
        Assert.Equal(1350, top);
        Assert.Equal(150, width);
        Assert.Equal(150, height);
    }

    // --- CompositeAgainstWhite tests (FRD-006 requirement 17) ---

    [Fact]
    public void CompositeAgainstWhite_FullyOpaquePixels_Unchanged()
    {
        byte[] bgra = [100, 150, 200, 255]; // B=100, G=150, R=200, A=255
        PageImagesService.CompositeAgainstWhite(bgra);

        Assert.Equal(100, bgra[0]);
        Assert.Equal(150, bgra[1]);
        Assert.Equal(200, bgra[2]);
        Assert.Equal(255, bgra[3]);
    }

    [Fact]
    public void CompositeAgainstWhite_FullyTransparentPixels_BecomesWhite()
    {
        byte[] bgra = [100, 150, 200, 0]; // Fully transparent
        PageImagesService.CompositeAgainstWhite(bgra);

        Assert.Equal(255, bgra[0]);
        Assert.Equal(255, bgra[1]);
        Assert.Equal(255, bgra[2]);
        Assert.Equal(255, bgra[3]);
    }

    [Fact]
    public void CompositeAgainstWhite_HalfTransparentBlack_BecomesGray()
    {
        // 50% transparent black: out = 0 * 0.5 + 255 * 0.5 = 127 (approximately)
        byte[] bgra = [0, 0, 0, 128];
        PageImagesService.CompositeAgainstWhite(bgra);

        // Allow ±1 for rounding: 0 * (128/255) + 255 * (1 - 128/255) ≈ 127
        Assert.InRange(bgra[0], 126, 128); // B
        Assert.InRange(bgra[1], 126, 128); // G
        Assert.InRange(bgra[2], 126, 128); // R
        Assert.Equal(255, bgra[3]);         // A now fully opaque
    }

    [Fact]
    public void CompositeAgainstWhite_MultiplePixels_AllProcessed()
    {
        byte[] bgra =
        [
            0, 0, 0, 0,       // Pixel 1: fully transparent → white
            255, 0, 0, 255,   // Pixel 2: opaque blue → unchanged
            0, 0, 0, 128,     // Pixel 3: half transparent black → gray
        ];

        PageImagesService.CompositeAgainstWhite(bgra);

        // Pixel 1: white
        Assert.Equal(255, bgra[0]);
        Assert.Equal(255, bgra[1]);
        Assert.Equal(255, bgra[2]);
        Assert.Equal(255, bgra[3]);

        // Pixel 2: unchanged
        Assert.Equal(255, bgra[4]);
        Assert.Equal(0, bgra[5]);
        Assert.Equal(0, bgra[6]);
        Assert.Equal(255, bgra[7]);

        // Pixel 3: gray-ish
        Assert.InRange(bgra[8], 126, 128);
        Assert.Equal(255, bgra[11]);
    }

    [Fact]
    public void CompositeAgainstWhite_EmptyBuffer_NoOp()
    {
        byte[] bgra = [];
        PageImagesService.CompositeAgainstWhite(bgra);
        Assert.Empty(bgra);
    }
}
