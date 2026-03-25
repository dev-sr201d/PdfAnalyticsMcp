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
    [McpServerTool, Description("Renders a single PDF page as a PNG image at a configurable DPI. Returns a visual image that multimodal models can inspect directly to verify structural understanding of complex layouts. Also returns a metadata text block with page, dpi, width, and height. Default DPI is 150 (valid range: 72–600).")]
    public IEnumerable<ContentBlock> RenderPagePreview(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to render.")] int page,
        [Description("Rendering resolution in dots per inch. Default is 150. Valid range: 72–600. Lower values produce smaller images, higher values produce sharper images.")] int dpi = 150)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            var result = renderService.Render(pdfPath, page, dpi);

            var metadata = new RenderPagePreviewMetadataDto(result.Page, result.Dpi, result.Width, result.Height);
            var metadataJson = JsonSerializer.Serialize(metadata, SerializerConfig.Options);

            return
            [
                ImageContentBlock.FromBytes(result.PngData, "image/png"),
                new TextContentBlock { Text = metadataJson }
            ];
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
