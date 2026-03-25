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
    public void ImagesService_PageOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _imagesService.Extract(path, 99, false));

        Assert.Contains("does not exist", ex.Message);
        Assert.DoesNotContain("An error occurred extracting images", ex.Message);
    }

    [Fact]
    public void ImagesService_PageZero_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _imagesService.Extract(path, 0, false));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
        Assert.DoesNotContain("An error occurred extracting images", ex.Message);
    }

    // --- RenderPagePreviewService: validation errors pass through unchanged ---

    [Fact]
    public void RenderService_PageOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var ex = Assert.Throws<ArgumentException>(() => _renderService.Render(path, 99, 150));

        Assert.Contains("does not exist", ex.Message);
        Assert.DoesNotContain("An error occurred rendering page", ex.Message);
    }

    [Fact]
    public void RenderService_PageZero_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var ex = Assert.Throws<ArgumentException>(() => _renderService.Render(path, 0, 150));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
        Assert.DoesNotContain("An error occurred rendering page", ex.Message);
    }

    [Fact]
    public void RenderService_DpiOutOfRange_ReturnsValidationError_NotPerPageError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var ex = Assert.Throws<ArgumentException>(() => _renderService.Render(path, 1, 50));

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
    public void ImagesService_ValidPage_SucceedsAfterValidationError()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        Assert.Throws<ArgumentException>(() => _imagesService.Extract(path, 99, false));

        var result = _imagesService.Extract(path, 1, false);
        Assert.NotNull(result);
    }

    [Fact]
    public void RenderService_ValidPage_SucceedsAfterValidationError()
    {
        var path = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        Assert.Throws<ArgumentException>(() => _renderService.Render(path, 99, 150));

        var result = _renderService.Render(path, 1, 150);
        Assert.True(result.PngData.Length > 0);
    }
}
