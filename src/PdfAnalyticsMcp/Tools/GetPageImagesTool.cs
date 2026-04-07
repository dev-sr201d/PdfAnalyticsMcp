using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class GetPageImagesTool(IInputValidationService validationService, IPageImagesService pageImagesService)
{
    [McpServerTool, Description("Returns embedded images from a single PDF page with bounding boxes (x, y, w, h in PDF points), pixel dimensions (pixelWidth, pixelHeight), and bits per pixel. Only metadata is returned by default. When outputPath is provided, each image is extracted to that directory and file paths appear in the response. JPEG-encoded images are extracted as .jpg files (raw bytes, zero quality loss); all other encodings are rendered individually via PDFiumCore and written as .png files. Recursively discovers images inside Form XObjects. Use this to understand text flow around images or to extract images for format conversion.")]
    public async Task<string> GetPageImages(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to extract images from.")] int page,
        [Description("Absolute path to a directory where extracted images will be written using deterministic names ({pdfStem}_p{page}_img{index}.{ext}). JPEG-encoded images are saved as .jpg (raw data, no re-encoding); all others as .png. When omitted, only image metadata is returned.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            validationService.ValidatePageMinimum(page);
            if (outputPath is not null)
            {
                validationService.ValidateOutputPath(outputPath);
            }
            var result = await pageImagesService.ExtractAsync(pdfPath, page, outputPath, cancellationToken);
            return JsonSerializer.Serialize(result, SerializerConfig.Options);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
