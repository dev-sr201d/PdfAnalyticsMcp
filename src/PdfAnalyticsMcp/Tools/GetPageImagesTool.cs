using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class GetPageImagesTool(IInputValidationService validationService, IPageImagesService pageImagesService)
{
    [McpServerTool, Description("Returns embedded images from a single PDF page with bounding boxes (x, y, w, h in PDF points), pixel dimensions (pixelWidth, pixelHeight), and bits per component. Image data is excluded by default to keep responses small. Set includeData to true to include base64-encoded PNG data for each image where conversion succeeds. Use this to understand text flow around images or to extract images for format conversion.")]
    public string GetPageImages(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to extract images from.")] int page,
        [Description("When true, base64-encoded PNG image data is included in the response for each image where conversion succeeds. Defaults to false.")] bool includeData = false)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            var result = pageImagesService.Extract(pdfPath, page, includeData);
            return JsonSerializer.Serialize(result, SerializerConfig.Options);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
