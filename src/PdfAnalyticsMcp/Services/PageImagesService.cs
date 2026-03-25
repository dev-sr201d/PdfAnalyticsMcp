using Microsoft.Extensions.Logging;
using PdfAnalyticsMcp.Models;
using UglyToad.PdfPig;

namespace PdfAnalyticsMcp.Services;

public class PageImagesService(IInputValidationService validationService, ILogger<PageImagesService> logger) : IPageImagesService
{
    public PageImagesDto Extract(string pdfPath, int page, bool includeData)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfPath);
        }
        catch (Exception)
        {
            throw new ArgumentException("The file could not be opened as a PDF.");
        }

        using (document)
        {
            validationService.ValidatePageNumber(page, document.NumberOfPages);

            var pdfPage = document.GetPage(page);
            var images = new List<ImageElementDto>();

            foreach (var image in pdfPage.GetImages())
            {
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

                    string? data = null;
                    if (includeData)
                    {
                        if (image.TryGetPng(out var pngBytes))
                        {
                            data = Convert.ToBase64String(pngBytes);
                        }
                    }

                    images.Add(new ImageElementDto(x, y, w, h, pixelWidth, pixelHeight, bitsPerComponent, data));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skipping image due to extraction error on page {Page}.", page);
                }
            }

            return new PageImagesDto(
                page,
                FormatUtils.RoundCoordinate(pdfPage.Width),
                FormatUtils.RoundCoordinate(pdfPage.Height),
                images);
        }
    }
}
