using System.Buffers.Binary;
using Microsoft.Extensions.Logging.Abstractions;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class RenderPagePreviewServiceTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly RenderPagePreviewService _service = new(
        new InputValidationService(),
        NullLogger<RenderPagePreviewService>.Instance);

    private static string GetTestDataPath(string fileName)
    {
        var testAssemblyDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "tests", "TestData", fileName);
    }

    [Fact]
    public async Task Render_AtDefaultDpi_ReturnsValidResult()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, "png", 80);

        Assert.Equal(1, result.Page);
        Assert.Equal(150, result.Dpi);
        Assert.Equal("png", result.Format);
        Assert.Equal(80, result.Quality);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.Equal("image/png", result.MimeType);
        Assert.True(result.ImageData.Length >= 8);
        Assert.Equal(PngSignature, result.ImageData[..8]);
    }

    [Fact]
    public async Task Render_At150Dpi_ReturnsExpectedDimensions()
    {
        // US Letter: 612×792 points, at 150 DPI => ~1275×1650 pixels
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, "png", 80);

        Assert.InRange(result.Width, 1270, 1280);
        Assert.InRange(result.Height, 1645, 1655);
    }

    [Fact]
    public async Task Render_At72Dpi_ReturnsSmallerDimensions()
    {
        // US Letter: 612×792 points, at 72 DPI => ~612×792 pixels
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 72, "png", 80);

        Assert.InRange(result.Width, 607, 617);
        Assert.InRange(result.Height, 787, 797);

        // Verify smaller than 150 DPI
        var result150 = await _service.RenderAsync(path, 1, 150, "png", 80);
        Assert.True(result.Width < result150.Width);
        Assert.True(result.Height < result150.Height);
    }

    [Fact]
    public async Task Render_At300Dpi_ReturnsLargerDimensions()
    {
        // US Letter: 612×792 points, at 300 DPI => ~2550×3300 pixels
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 300, "png", 80);

        Assert.InRange(result.Width, 2545, 2555);
        Assert.InRange(result.Height, 3295, 3305);

        // Verify larger than 150 DPI
        var result150 = await _service.RenderAsync(path, 1, 150, "png", 80);
        Assert.True(result.Width > result150.Width);
        Assert.True(result.Height > result150.Height);
    }

    [Fact]
    public async Task Render_Page2_ReturnsValidResult()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 2, 150, "png", 80);

        Assert.Equal(2, result.Page);
        Assert.True(result.ImageData.Length >= 8);
        Assert.Equal(PngSignature, result.ImageData[..8]);
    }

    [Fact]
    public async Task Render_DpiTooLow_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 50, "png", 80));
        Assert.Contains("72", ex.Message);
        Assert.Contains("600", ex.Message);
    }

    [Fact]
    public async Task Render_DpiTooHigh_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 700, "png", 80));
        Assert.Contains("72", ex.Message);
        Assert.Contains("600", ex.Message);
    }

    [Fact]
    public async Task Render_DpiAtMinBoundary_Succeeds()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 72, "png", 80);

        Assert.Equal(72, result.Dpi);
        Assert.True(result.ImageData.Length > 0);
    }

    [Fact]
    public async Task Render_DpiAtMaxBoundary_Succeeds()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 600, "png", 80);

        Assert.Equal(600, result.Dpi);
        Assert.True(result.ImageData.Length > 0);
    }

    [Fact]
    public async Task Render_PageZero_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 0, 150, "png", 80));
    }

    [Fact]
    public async Task Render_PageBeyondCount_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 999, 150, "png", 80));
    }

    [Fact]
    public async Task Render_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = GetTestDataPath("not-a-pdf.txt");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 150, "png", 80));
        Assert.Equal("The file could not be opened as a PDF.", ex.Message);
    }

    [Fact]
    public async Task Render_LockedFile_ThrowsArgumentExceptionWithAccessMessage()
    {
        var source = GetTestDataPath("sample-with-metadata.pdf");
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        File.Copy(source, tempPath);
        try
        {
            using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(tempPath, 1, 150, "png", 80));
            Assert.Equal($"The file could not be accessed: {tempPath}. It may be in use by another process.", ex.Message);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task Render_PngDataContainsValidIhdr()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, "png", 80);

        // IHDR starts after signature (8 bytes) + 4 bytes length + 4 bytes "IHDR" type
        int ihdrDataOffset = 8 + 4 + 4;
        int parsedWidth = BinaryPrimitives.ReadInt32BigEndian(result.ImageData.AsSpan(ihdrDataOffset, 4));
        int parsedHeight = BinaryPrimitives.ReadInt32BigEndian(result.ImageData.AsSpan(ihdrDataOffset + 4, 4));

        Assert.Equal(result.Width, parsedWidth);
        Assert.Equal(result.Height, parsedHeight);
    }

    [Fact]
    public async Task Render_CancelledToken_ThrowsOperationCanceledException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.RenderAsync(path, 1, 150, "png", 80, cts.Token));
    }

    [Fact]
    public async Task RenderRaw_ReturnsCorrectBgraBuffer()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderRawAsync(path, 1, 150);

        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.InRange(result.Width, 1270, 1280);
        Assert.InRange(result.Height, 1645, 1655);
        Assert.Equal(result.Width * result.Height * 4, result.BgraData.Length);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(700)]
    public async Task RenderRaw_InvalidDpi_ThrowsArgumentException(int dpi)
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderRawAsync(path, 1, dpi));
    }

    [Fact]
    public async Task RenderRaw_PageZero_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderRawAsync(path, 0, 150));
    }

    [Fact]
    public async Task RenderRaw_DoesNotReturnPngEncodedData()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderRawAsync(path, 1, 150);

        // Raw BGRA buffer must NOT start with the PNG signature
        Assert.True(result.BgraData.Length >= 8);
        Assert.NotEqual(PngSignature, result.BgraData[..8]);
    }

    // --- Format support tests ---

    [Fact]
    public async Task Render_AsJpeg_ReturnsJpegData()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, "jpeg", 80);

        Assert.Equal("jpeg", result.Format);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.True(result.ImageData.Length >= 2);
        Assert.Equal(0xFF, result.ImageData[0]);
        Assert.Equal(0xD8, result.ImageData[1]);
    }

    [Fact]
    public async Task Render_JpgAlias_NormalizesToJpeg()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, "jpg", 80);

        Assert.Equal("jpeg", result.Format);
        Assert.Equal("image/jpeg", result.MimeType);
    }

    [Theory]
    [InlineData("PNG")]
    [InlineData("Jpeg")]
    [InlineData("JPG")]
    [InlineData("Png")]
    public async Task Render_FormatCaseInsensitive_Succeeds(string format)
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, format, 80);

        Assert.True(result.ImageData.Length > 0);
    }

    [Fact]
    public async Task Render_InvalidFormat_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 150, "bmp", 80));
        Assert.Contains("png", ex.Message);
        Assert.Contains("jpeg", ex.Message);
        Assert.Contains("jpg", ex.Message);
    }

    // --- Quality tests ---

    [Fact]
    public async Task Render_JpegQualityAffectsSize()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var resultLow = await _service.RenderAsync(path, 1, 150, "jpeg", 10);
        var resultHigh = await _service.RenderAsync(path, 1, 150, "jpeg", 100);

        Assert.True(resultHigh.ImageData.Length > resultLow.ImageData.Length,
            $"Quality=100 ({resultHigh.ImageData.Length} bytes) should be larger than quality=10 ({resultLow.ImageData.Length} bytes).");
    }

    [Fact]
    public async Task Render_PngIgnoresQuality()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var resultLow = await _service.RenderAsync(path, 1, 150, "png", 10);
        var resultHigh = await _service.RenderAsync(path, 1, 150, "png", 100);

        Assert.Equal(resultLow.ImageData, resultHigh.ImageData);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Render_QualityAtBoundaries_Succeeds(int quality)
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150, "png", quality);
        Assert.True(result.ImageData.Length > 0);
    }

    [Fact]
    public async Task Render_QualityBelowRange_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 150, "png", 0));
        Assert.Contains("1", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public async Task Render_QualityAboveRange_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 150, "png", 101));
        Assert.Contains("1", ex.Message);
        Assert.Contains("100", ex.Message);
    }

    [Fact]
    public async Task Render_ResultDto_ContainsCorrectSizeBytes()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var resultPng = await _service.RenderAsync(path, 1, 150, "png", 80);
        var resultJpeg = await _service.RenderAsync(path, 1, 150, "jpeg", 80);

        Assert.True(resultPng.ImageData.Length > 0);
        Assert.True(resultJpeg.ImageData.Length > 0);
    }
}
