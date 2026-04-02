using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Models;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class RenderPagePreviewTool(IInputValidationService validationService, IRenderPagePreviewService renderService)
{
    [McpServerTool, Description("Renders a single PDF page as an image at a configurable DPI. Supports JPEG (lossy, smaller file size, default) and PNG (lossless) output formats. Returns a visual image that multimodal models can inspect directly to verify structural understanding of complex layouts. Also returns a metadata text block with page, dpi, format, quality, width, height, and sizeBytes. Default DPI is 150 (valid range: 72–600). Default format is 'jpeg'. Default quality is 80 (1–100; controls JPEG compression, ignored for PNG). JPEG is recommended for most use cases as it produces significantly smaller files than PNG.")]
    public async Task<IEnumerable<ContentBlock>> RenderPagePreview(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to render.")] int page,
        [Description("Rendering resolution in dots per inch. Default is 150. Valid range: 72–600. Lower values produce smaller images, higher values produce sharper images.")] int dpi = 150,
        [Description("Output image format: 'jpeg'/'jpg' (lossy, smaller file size, default) or 'png' (lossless). Case-insensitive.")] string format = "jpeg",
        [Description("Image quality from 1 (smallest file) to 100 (highest quality). Controls JPEG compression; ignored for PNG. Default is 80.")] int quality = 80,
        CancellationToken cancellationToken = default)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            validationService.ValidatePageMinimum(page);
            validationService.ValidateDpi(dpi);
            var result = await renderService.RenderAsync(pdfPath, page, dpi, format, quality, cancellationToken);

            var metadata = new RenderPagePreviewMetadataDto(result.Page, result.Dpi, result.Format, result.Quality, result.Width, result.Height, result.ImageData.Length);
            var metadataJson = JsonSerializer.Serialize(metadata, SerializerConfig.Options);

            return
            [
                ImageContentBlock.FromBytes(result.ImageData, result.MimeType),
                new TextContentBlock { Text = metadataJson }
            ];
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
