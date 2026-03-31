using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class GetPageImagesTool(IInputValidationService validationService, IPageImagesService pageImagesService)
{
    [McpServerTool, Description("Returns embedded images from a single PDF page with bounding boxes (x, y, w, h in PDF points), pixel dimensions (pixelWidth, pixelHeight), and bits per component. Only metadata is returned by default. When outputPath is provided, images are extracted as PNG files to that directory (using a render-based fallback for images that cannot be directly extracted) and file paths appear in the response. Use this to understand text flow around images or to extract images for format conversion.")]
    public async Task<string> GetPageImages(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to extract images from.")] int page,
        [Description("Absolute path to a directory where extracted images will be written as PNG files using deterministic names ({pdfStem}_p{page}_img{index}.png). When omitted, only image metadata is returned.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            validationService.ValidatePageMinimum(page);
            var result = await pageImagesService.ExtractAsync(pdfPath, page, outputPath, cancellationToken);
            return JsonSerializer.Serialize(result, SerializerConfig.Options);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
