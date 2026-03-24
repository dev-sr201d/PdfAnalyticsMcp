using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tools;

[McpServerToolType]
public class GetPdfInfoTool(IInputValidationService validationService, IPdfInfoService pdfInfoService)
{
    [McpServerTool, Description("Returns document-level metadata from a PDF file including page count, page dimensions, title, author, and bookmarks/outline tree.")]
    public string GetPdfInfo(
        [Description("Absolute path to the PDF file on the local filesystem.")] string pdfPath)
    {
        try
        {
            validationService.ValidateFilePath(pdfPath);
            var result = pdfInfoService.Extract(pdfPath);
            return JsonSerializer.Serialize(result, SerializerConfig.Options);
        }
        catch (ArgumentException ex)
        {
            throw new McpException(ex.Message);
        }
    }
}
