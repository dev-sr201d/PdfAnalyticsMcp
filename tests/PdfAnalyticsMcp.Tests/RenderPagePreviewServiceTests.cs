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
        var result = await _service.RenderAsync(path, 1, 150);

        Assert.Equal(1, result.Page);
        Assert.Equal(150, result.Dpi);
        Assert.True(result.Width > 0);
        Assert.True(result.Height > 0);
        Assert.True(result.PngData.Length >= 8);
        Assert.Equal(PngSignature, result.PngData[..8]);
    }

    [Fact]
    public async Task Render_At150Dpi_ReturnsExpectedDimensions()
    {
        // US Letter: 612×792 points, at 150 DPI => ~1275×1650 pixels
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 150);

        Assert.InRange(result.Width, 1270, 1280);
        Assert.InRange(result.Height, 1645, 1655);
    }

    [Fact]
    public async Task Render_At72Dpi_ReturnsSmallerDimensions()
    {
        // US Letter: 612×792 points, at 72 DPI => ~612×792 pixels
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 72);

        Assert.InRange(result.Width, 607, 617);
        Assert.InRange(result.Height, 787, 797);

        // Verify smaller than 150 DPI
        var result150 = await _service.RenderAsync(path, 1, 150);
        Assert.True(result.Width < result150.Width);
        Assert.True(result.Height < result150.Height);
    }

    [Fact]
    public async Task Render_At300Dpi_ReturnsLargerDimensions()
    {
        // US Letter: 612×792 points, at 300 DPI => ~2550×3300 pixels
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 300);

        Assert.InRange(result.Width, 2545, 2555);
        Assert.InRange(result.Height, 3295, 3305);

        // Verify larger than 150 DPI
        var result150 = await _service.RenderAsync(path, 1, 150);
        Assert.True(result.Width > result150.Width);
        Assert.True(result.Height > result150.Height);
    }

    [Fact]
    public async Task Render_Page2_ReturnsValidResult()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 2, 150);

        Assert.Equal(2, result.Page);
        Assert.True(result.PngData.Length >= 8);
        Assert.Equal(PngSignature, result.PngData[..8]);
    }

    [Fact]
    public async Task Render_DpiTooLow_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 50));
        Assert.Contains("72", ex.Message);
        Assert.Contains("600", ex.Message);
    }

    [Fact]
    public async Task Render_DpiTooHigh_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 700));
        Assert.Contains("72", ex.Message);
        Assert.Contains("600", ex.Message);
    }

    [Fact]
    public async Task Render_DpiAtMinBoundary_Succeeds()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 72);

        Assert.Equal(72, result.Dpi);
        Assert.True(result.PngData.Length > 0);
    }

    [Fact]
    public async Task Render_DpiAtMaxBoundary_Succeeds()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = await _service.RenderAsync(path, 1, 600);

        Assert.Equal(600, result.Dpi);
        Assert.True(result.PngData.Length > 0);
    }

    [Fact]
    public async Task Render_PageZero_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 0, 150));
    }

    [Fact]
    public async Task Render_PageBeyondCount_ThrowsArgumentException()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 999, 150));
    }

    [Fact]
    public async Task Render_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = GetTestDataPath("not-a-pdf.txt");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(path, 1, 150));
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

            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.RenderAsync(tempPath, 1, 150));
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
        var result = await _service.RenderAsync(path, 1, 150);

        // IHDR starts after signature (8 bytes) + 4 bytes length + 4 bytes "IHDR" type
        int ihdrDataOffset = 8 + 4 + 4;
        int parsedWidth = BinaryPrimitives.ReadInt32BigEndian(result.PngData.AsSpan(ihdrDataOffset, 4));
        int parsedHeight = BinaryPrimitives.ReadInt32BigEndian(result.PngData.AsSpan(ihdrDataOffset + 4, 4));

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
            () => _service.RenderAsync(path, 1, 150, cts.Token));
    }
}
