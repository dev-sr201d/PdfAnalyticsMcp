using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class GetPdfInfoIntegrationTests : McpIntegrationTestBase
{
    [Fact]
    public async Task GetPdfInfo_ToolDiscovery_ReturnsToolWithSchema()
    {
        await PerformHandshakeAsync();

        var toolsListRequest = CreateJsonRpcRequest("tools/list", new { });
        await SendMessageAsync(toolsListRequest);

        var toolsResponse = await ReadResponseAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(toolsResponse);

        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        JsonElement? getPdfInfoTool = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.GetProperty("name").GetString() == "get_pdf_info")
            {
                getPdfInfoTool = tool;
                break;
            }
        }

        Assert.NotNull(getPdfInfoTool);
        Assert.True(getPdfInfoTool.Value.TryGetProperty("description", out _));
        Assert.True(getPdfInfoTool.Value.TryGetProperty("inputSchema", out var schema));
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("pdfPath", out _));
    }

    [Fact]
    public async Task GetPdfInfo_ValidPdf_ReturnsCorrectMetadata()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        var response = await CallToolAsync("get_pdf_info", new { pdfPath });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(2, root.GetProperty("pageCount").GetInt32());
        Assert.Equal("Test Document", root.GetProperty("title").GetString());
        Assert.Equal("Test Author", root.GetProperty("author").GetString());
        Assert.Equal("Test Subject", root.GetProperty("subject").GetString());
        Assert.Equal("test, pdf, sample", root.GetProperty("keywords").GetString());

        var pages = root.GetProperty("pages");
        Assert.Equal(2, pages.GetArrayLength());
        Assert.Equal(1, pages[0].GetProperty("number").GetInt32());
        Assert.Equal(612.0, pages[0].GetProperty("width").GetDouble());
        Assert.Equal(792.0, pages[0].GetProperty("height").GetDouble());
    }

    [Fact]
    public async Task GetPdfInfo_PdfWithBookmarks_ReturnsHierarchicalTree()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-bookmarks.pdf");
        var response = await CallToolAsync("get_pdf_info", new { pdfPath });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.True(root.TryGetProperty("bookmarks", out var bookmarks));
        Assert.Equal(2, bookmarks.GetArrayLength());

        var ch1 = bookmarks[0];
        Assert.Equal("Chapter 1", ch1.GetProperty("title").GetString());
        Assert.Equal(1, ch1.GetProperty("pageNumber").GetInt32());
        Assert.True(ch1.TryGetProperty("children", out var children));
        Assert.Equal(1, children.GetArrayLength());
        Assert.Equal("Section 1.1", children[0].GetProperty("title").GetString());

        var ch2 = bookmarks[1];
        Assert.Equal("Chapter 2", ch2.GetProperty("title").GetString());
        Assert.Equal(2, ch2.GetProperty("pageNumber").GetInt32());
    }

    [Fact]
    public async Task GetPdfInfo_PdfWithoutBookmarks_OmitsBookmarksField()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        var response = await CallToolAsync("get_pdf_info", new { pdfPath });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);

        Assert.False(json.RootElement.TryGetProperty("bookmarks", out _),
            "bookmarks field should be omitted when document has no bookmarks.");
    }

    [Fact]
    public async Task GetPdfInfo_MissingFile_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = "C:\\nonexistent\\missing.pdf";
        var response = await CallToolAsync("get_pdf_info", new { pdfPath });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var content = resultElement.GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        Assert.Contains("File not found", text);
    }

    [Fact]
    public async Task GetPdfInfo_EmptyPath_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_pdf_info", new { pdfPath = "" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var content = resultElement.GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        Assert.Contains("pdfPath is required", text);
    }

    [Fact]
    public async Task GetPdfInfo_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_pdf_info", new { pdfPath = "C:\\docs\\..\\secret.pdf" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var content = resultElement.GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        Assert.Contains("Invalid file path", text);
    }

    [Fact]
    public async Task GetPdfInfo_InvalidPdfFile_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("not-a-pdf.txt");
        var response = await CallToolAsync("get_pdf_info", new { pdfPath });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var content = resultElement.GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        Assert.Contains("The file could not be opened as a PDF", text);
    }
}
