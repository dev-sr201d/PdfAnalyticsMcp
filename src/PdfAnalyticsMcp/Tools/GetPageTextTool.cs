using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class GetPageTextTool(IInputValidationService validationService, IPageTextService pageTextService)
{
    [McpServerTool, Description("Returns text elements from a single PDF page with position, font, size, and color metadata. Each element includes a bounding box (x, y, w, h), font name, font size, and optional color/bold/italic flags. Use granularity 'words' (default) for layout analysis or 'letters' for character-level detail. When outputFile is provided, element data is written as CSV to that file and a compact summary is returned inline, enabling callers to handle large pages without payload limits and with reduced token consumption.")]
    public string GetPageText(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath,
        [Description("1-based page number to extract text from.")] int page,
        [Description("Level of detail: 'words' (default, ~5× smaller) or 'letters' (character-level, ~5× more data).")] string granularity = "words",
        [Description("Optional absolute path to write element data as CSV. When provided, element data is written as CSV to this file instead of returning full JSON inline, and a compact summary is returned.")] string? outputFile = null)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            validationService.ValidatePageMinimum(page);
            validationService.ValidateGranularity(granularity);

            if (outputFile is not null)
            {
                var summary = pageTextService.ExtractToFile(pdfPath, page, granularity, outputFile);
                return JsonSerializer.Serialize(summary, SerializerConfig.Options);
            }

            var result = pageTextService.Extract(pdfPath, page, granularity);
            return JsonSerializer.Serialize(result, SerializerConfig.Options);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
