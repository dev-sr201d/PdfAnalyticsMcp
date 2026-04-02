using Microsoft.Extensions.Logging.Abstractions;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

/// <summary>
/// Verifies per-page failure resilience (Task 019 / FRD-007 FR5/FR6).
/// Because creating PDFs with corrupt content streams is impractical,
/// these tests verify:
/// 1. Validation ArgumentExceptions pass through unchanged (not masked by per-page catch).
/// 2. The error message format matches expectations for each service.
/// </summary>
public class PerPageFailureResilienceTests
{
    private readonly PageTextService _textService = new(new InputValidationService());
    private readonly PageGraphicsService _graphicsService = new(new InputValidationService());
    private readonly PageImagesService _imagesService = new(
        new InputValidationService(),
        new RenderPagePreviewService(
            new InputValidationService(),
            NullLogger<RenderPagePreviewService>.Instance),
        NullLogger<PageImagesService>.Instance);
    private readonly RenderPagePreviewService _renderService = new(
        new InputValidationService(),
        NullLogger<RenderPagePreviewService>.Instance);

    // --- PageTextService: validation errors pass through unchanged ---

    [Fact]
    public void TextService_PageOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _textService.Extract(path, 99, "words"));

        Assert.Contains("does not exist", ex.Message);
        Assert.DoesNotContain("An error occurred extracting text", ex.Message);
    }

    [Fact]
    public void TextService_PageZero_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _textService.Extract(path, 0, "words"));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
        Assert.DoesNotContain("An error occurred extracting text", ex.Message);
    }

    [Fact]
    public void TextService_InvalidGranularity_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _textService.Extract(path, 1, "invalid"));

        Assert.Equal("Granularity must be 'words' or 'letters'.", ex.Message);
    }

    // --- PageGraphicsService: validation errors pass through unchanged ---

    [Fact]
    public void GraphicsService_PageOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _graphicsService.Extract(path, 99));

        Assert.Contains("does not exist", ex.Message);
        Assert.DoesNotContain("An error occurred extracting graphics", ex.Message);
    }

    [Fact]
    public void GraphicsService_PageZero_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _graphicsService.Extract(path, 0));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
        Assert.DoesNotContain("An error occurred extracting graphics", ex.Message);
    }

    // --- PageImagesService: validation errors pass through unchanged ---

    [Fact]
    public async Task ImagesService_PageOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _imagesService.ExtractAsync(path, 99));

        Assert.Contains("does not exist", ex.Message);
        Assert.DoesNotContain("An error occurred extracting images", ex.Message);
    }

    [Fact]
    public async Task ImagesService_PageZero_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _imagesService.ExtractAsync(path, 0));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
        Assert.DoesNotContain("An error occurred extracting images", ex.Message);
    }

    // --- RenderPagePreviewService: validation errors pass through unchanged ---

    [Fact]
    public async Task RenderService_PageOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _renderService.RenderAsync(path, 99, 150, "png", 80));

        Assert.Contains("does not exist", ex.Message);
        Assert.DoesNotContain("An error occurred rendering page", ex.Message);
    }

    [Fact]
    public async Task RenderService_PageZero_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _renderService.RenderAsync(path, 0, 150, "png", 80));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
        Assert.DoesNotContain("An error occurred rendering page", ex.Message);
    }

    [Fact]
    public async Task RenderService_DpiOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _renderService.RenderAsync(path, 1, 50, "png", 80));

        Assert.Contains("72", ex.Message);
        Assert.Contains("600", ex.Message);
        Assert.DoesNotContain("An error occurred rendering page", ex.Message);
    }

    // --- Valid pages continue to work (server remains operational) ---

    [Fact]
    public void TextService_ValidPage_SucceedsAfterValidationError()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        // First call fails with validation error
        Assert.Throws<ArgumentException>(() => _textService.Extract(path, 99, "words"));

        // Second call succeeds — service is still operational
        var result = _textService.Extract(path, 1, "words");
        Assert.True(result.Elements.Count > 0);
    }

    [Fact]
    public void GraphicsService_ValidPage_SucceedsAfterValidationError()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();

        Assert.Throws<ArgumentException>(() => _graphicsService.Extract(path, 99));

        var result = _graphicsService.Extract(path, 1);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ImagesService_ValidPage_SucceedsAfterValidationError()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        await Assert.ThrowsAsync<ArgumentException>(() => _imagesService.ExtractAsync(path, 99));

        var result = await _imagesService.ExtractAsync(path, 1);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RenderService_ValidPage_SucceedsAfterValidationError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        await Assert.ThrowsAsync<ArgumentException>(() => _renderService.RenderAsync(path, 99, 150, "png", 80));

        var result = await _renderService.RenderAsync(path, 1, 150, "png", 80);
        Assert.True(result.ImageData.Length > 0);
    }

    // --- OperationCanceledException propagates through per-page handlers (FRD-007 FR #10) ---

    [Fact]
    public async Task RenderService_CancelledToken_PropagatesOperationCanceledException()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _renderService.RenderAsync(path, 1, 150, "png", 80, cts.Token));
    }

    [Fact]
    public async Task ImagesService_CancelledToken_PropagatesOperationCanceledException()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The cancellation may surface from the render-based fallback path or
        // from an async operation within ExtractAsync. With a pre-cancelled token,
        // the service must not swallow the OperationCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _imagesService.ExtractAsync(path, 1, null, cts.Token));
    }
}
