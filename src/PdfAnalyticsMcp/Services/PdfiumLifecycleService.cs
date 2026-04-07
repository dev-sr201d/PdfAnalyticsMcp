using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PDFiumCore;

namespace PdfAnalyticsMcp.Services;

public class PdfiumLifecycleService(ILogger<PdfiumLifecycleService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        fpdfview.FPDF_InitLibrary();
        logger.LogInformation("PDFium library initialized.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        fpdfview.FPDF_DestroyLibrary();
        logger.LogInformation("PDFium library destroyed.");
        return Task.CompletedTask;
    }
}
