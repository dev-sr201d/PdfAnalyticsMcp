using PdfAnalyticsMcp.Services;
using PDFiumCore;

namespace PdfAnalyticsMcp.Tests;

public class PdfiumServiceTests : IDisposable
{
    private static readonly string TestDataDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "TestData");

    private static readonly string ValidPdf = Path.GetFullPath(Path.Combine(TestDataDir, "sample-with-metadata.pdf"));
    private static readonly string NotAPdf = Path.GetFullPath(Path.Combine(TestDataDir, "not-a-pdf.txt"));

    private static int _initialized;

    public PdfiumServiceTests()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
        {
            fpdfview.FPDF_InitLibrary();
        }
    }

    public void Dispose() { }

    private static PdfiumService CreateService()
    {
        var validationService = new InputValidationService();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfiumService>();
        return new PdfiumService(validationService, logger);
    }

    [Fact]
    public async Task ExecuteAsync_ValidPdf_InvokesCallbackAndReturnsPageCount()
    {
        var service = CreateService();

        var pageCount = await service.ExecuteAsync(ValidPdf, 1, (doc, page) =>
        {
            return service.GetPageCount(doc);
        });

        Assert.True(pageCount > 0);
    }

    [Fact]
    public async Task ExecuteAsync_Page1_ReturnsValidPageHandle()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(ValidPdf, 1, (doc, page) =>
        {
            // If we get here, the page handle is valid (non-null, validated by the service)
            return true;
        });

        Assert.True(result);
    }

    [Fact]
    public async Task ExecuteAsync_Page2_ReturnsValidPageHandle()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync(ValidPdf, 2, (doc, page) =>
        {
            return true;
        });

        Assert.True(result);
    }

    [Fact]
    public async Task ExecuteAsync_PageSize_ReturnsExpectedDimensions()
    {
        var service = CreateService();

        var (width, height) = await service.ExecuteAsync(ValidPdf, 1, (doc, page) =>
        {
            return service.GetPageSize(doc, 0);
        });

        // US Letter: 612 x 792 PDF points
        Assert.InRange(width, 600, 620);
        Assert.InRange(height, 780, 800);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPdfFile_ThrowsArgumentException()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExecuteAsync(NotAPdf, 1, (doc, page) => true));

        Assert.Contains("could not be opened as a PDF", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PageZero_ThrowsArgumentException()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExecuteAsync(ValidPdf, 0, (doc, page) => true));

        Assert.Contains("Page number must be 1 or greater", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_PageBeyondCount_ThrowsArgumentException()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExecuteAsync(ValidPdf, 9999, (doc, page) => true));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(ValidPdf, 1, (doc, page) => true, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_CallbackThrows_PropagatesExceptionAndSemaphoreIsReleased()
    {
        var service = CreateService();

        // First call: callback throws
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync<bool>(ValidPdf, 1, (doc, page) =>
            {
                throw new InvalidOperationException("Test callback error");
            }));

        // Second call: should succeed, proving semaphore was released
        var result = await service.ExecuteAsync(ValidPdf, 1, (doc, page) => true);
        Assert.True(result);
    }

    [Fact]
    public async Task ExecuteAsync_SequentialCalls_BothSucceed()
    {
        var service = CreateService();

        var result1 = await service.ExecuteAsync(ValidPdf, 1, (doc, page) => 1);
        var result2 = await service.ExecuteAsync(ValidPdf, 1, (doc, page) => 2);

        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
    }
}
