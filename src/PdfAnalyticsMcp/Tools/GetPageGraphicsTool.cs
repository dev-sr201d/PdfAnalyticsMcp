using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class GetPageGraphicsTool(IInputValidationService validationService, IPageGraphicsService pageGraphicsService)
{
    [McpServerTool, Description("Returns classified graphic shapes from a single PDF page: rectangles, lines, and complex paths with fill/stroke colors, stroke width, and dash patterns. Raw PDF operations are pre-classified into meaningful shapes. Use this to identify table gridlines, sidebar backgrounds, callout box borders, section dividers, and shaded regions.")]
    public string GetPageGraphics(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to extract graphics from.")] int page)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            validationService.ValidatePageMinimum(page);
            var result = pageGraphicsService.Extract(pdfPath, page);
            return JsonSerializer.Serialize(result, SerializerConfig.Options);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
