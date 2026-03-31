using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class RenderPagePreviewIntegrationTests : McpIntegrationTestBase
{
    [Fact]
    public async Task RenderPagePreview_ToolDiscovery_ReturnsToolWithSchema()
    {
        await PerformHandshakeAsync();

        var toolsListRequest = CreateJsonRpcRequest("tools/list", new { });
        await SendMessageAsync(toolsListRequest);

        var toolsResponse = await ReadResponseAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(toolsResponse);

        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        JsonElement? renderTool = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.GetProperty("name").GetString() == "render_page_preview")
            {
                renderTool = tool;
                break;
            }
        }

        Assert.NotNull(renderTool);
        Assert.True(renderTool.Value.TryGetProperty("description", out _));
        Assert.True(renderTool.Value.TryGetProperty("inputSchema", out var schema));
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("pdfPath", out _));
        Assert.True(props.TryGetProperty("page", out _));
        Assert.True(props.TryGetProperty("dpi", out _));
    }

    [Fact]
    public async Task RenderPagePreview_DefaultDpi_ReturnsTwoContentBlocks()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");
        Assert.True(File.Exists(pdfPath), $"Test data file not found: {pdfPath}");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var content = response.RootElement.GetProperty("result").GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());

        // Find image and text blocks
        var (imageBlock, textBlock) = FindContentBlocks(content);

        Assert.NotNull(imageBlock);
        Assert.Equal("image", imageBlock.Value.GetProperty("type").GetString());
        Assert.Equal("image/png", imageBlock.Value.GetProperty("mimeType").GetString());
        Assert.False(string.IsNullOrEmpty(imageBlock.Value.GetProperty("data").GetString()));

        Assert.NotNull(textBlock);
        Assert.Equal("text", textBlock.Value.GetProperty("type").GetString());

        var metadata = JsonDocument.Parse(textBlock.Value.GetProperty("text").GetString()!);
        Assert.Equal(150, metadata.RootElement.GetProperty("dpi").GetInt32());
        Assert.Equal(1, metadata.RootElement.GetProperty("page").GetInt32());
        Assert.True(metadata.RootElement.GetProperty("width").GetInt32() > 0);
        Assert.True(metadata.RootElement.GetProperty("height").GetInt32() > 0);
    }

    [Fact]
    public async Task RenderPagePreview_ImageDataValidity_ValidPngSignature()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var content = response.RootElement.GetProperty("result").GetProperty("content");
        var (imageBlock, _) = FindContentBlocks(content);
        Assert.NotNull(imageBlock);

        var base64Data = imageBlock.Value.GetProperty("data").GetString()!;
        var bytes = Convert.FromBase64String(base64Data);

        // PNG signature: 137 80 78 71 13 10 26 10
        Assert.True(bytes.Length >= 8);
        Assert.Equal(137, bytes[0]);
        Assert.Equal(80, bytes[1]);  // P
        Assert.Equal(78, bytes[2]);  // N
        Assert.Equal(71, bytes[3]);  // G
        Assert.Equal(13, bytes[4]);
        Assert.Equal(10, bytes[5]);
        Assert.Equal(26, bytes[6]);
        Assert.Equal(10, bytes[7]);
    }

    [Fact]
    public async Task RenderPagePreview_MetadataDimensions_PositiveAndPlausible()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var content = response.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock) = FindContentBlocks(content);
        Assert.NotNull(textBlock);

        var metadata = JsonDocument.Parse(textBlock.Value.GetProperty("text").GetString()!);
        var width = metadata.RootElement.GetProperty("width").GetInt32();
        var height = metadata.RootElement.GetProperty("height").GetInt32();

        // At 150 DPI, US Letter is ~1275x1650. Allow wide range for different page sizes.
        Assert.True(width > 100, $"Width {width} is too small.");
        Assert.True(height > 100, $"Height {height} is too small.");
        Assert.True(width < 10000, $"Width {width} is implausibly large.");
        Assert.True(height < 10000, $"Height {height} is implausibly large.");
    }

    [Fact]
    public async Task RenderPagePreview_CustomDpi72_SmallerThanDefault()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        // Render at default 150 DPI
        var response150 = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response150);
        var content150 = response150.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock150) = FindContentBlocks(content150);
        var meta150 = JsonDocument.Parse(textBlock150!.Value.GetProperty("text").GetString()!);
        var width150 = meta150.RootElement.GetProperty("width").GetInt32();

        // Render at 72 DPI
        var response72 = await CallToolAsync("render_page_preview", new { pdfPath, page = 1, dpi = 72 });
        Assert.NotNull(response72);
        var content72 = response72.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock72) = FindContentBlocks(content72);
        var meta72 = JsonDocument.Parse(textBlock72!.Value.GetProperty("text").GetString()!);
        var width72 = meta72.RootElement.GetProperty("width").GetInt32();

        Assert.Equal(72, meta72.RootElement.GetProperty("dpi").GetInt32());
        Assert.True(width72 < width150, $"72 DPI width ({width72}) should be smaller than 150 DPI width ({width150}).");
    }

    [Fact]
    public async Task RenderPagePreview_CustomDpi300_LargerThanDefault()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        // Render at default 150 DPI
        var response150 = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response150);
        var content150 = response150.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock150) = FindContentBlocks(content150);
        var meta150 = JsonDocument.Parse(textBlock150!.Value.GetProperty("text").GetString()!);
        var width150 = meta150.RootElement.GetProperty("width").GetInt32();

        // Render at 300 DPI
        var response300 = await CallToolAsync("render_page_preview", new { pdfPath, page = 1, dpi = 300 });
        Assert.NotNull(response300);
        var content300 = response300.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock300) = FindContentBlocks(content300);
        var meta300 = JsonDocument.Parse(textBlock300!.Value.GetProperty("text").GetString()!);
        var width300 = meta300.RootElement.GetProperty("width").GetInt32();

        Assert.Equal(300, meta300.RootElement.GetProperty("dpi").GetInt32());
        Assert.True(width300 > width150, $"300 DPI width ({width300}) should be larger than 150 DPI width ({width150}).");
    }

    [Fact]
    public async Task RenderPagePreview_DpiOmitted_DefaultsTo150()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        // Call without specifying dpi at all
        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var content = response.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock) = FindContentBlocks(content);
        Assert.NotNull(textBlock);

        var metadata = JsonDocument.Parse(textBlock.Value.GetProperty("text").GetString()!);
        Assert.Equal(150, metadata.RootElement.GetProperty("dpi").GetInt32());
    }

    [Fact]
    public async Task RenderPagePreview_Page2_ReportsCorrectPageNumber()
    {
        await PerformHandshakeAsync();

        // Use a multi-page test PDF (sample-with-metadata.pdf has 2 pages)
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");
        Assert.True(File.Exists(pdfPath), $"Test data file not found: {pdfPath}");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 2 });
        Assert.NotNull(response);

        var content = response.RootElement.GetProperty("result").GetProperty("content");
        var (_, textBlock) = FindContentBlocks(content);
        Assert.NotNull(textBlock);

        var metadata = JsonDocument.Parse(textBlock.Value.GetProperty("text").GetString()!);
        Assert.Equal(2, metadata.RootElement.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task RenderPagePreview_EmptyPath_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("render_page_preview", new { pdfPath = "", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("pdfPath is required", text);
    }

    [Fact]
    public async Task RenderPagePreview_FileNotFound_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("render_page_preview", new { pdfPath = "C:\\nonexistent\\missing.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("File not found", text);
    }

    [Fact]
    public async Task RenderPagePreview_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("render_page_preview", new { pdfPath = "C:\\docs\\..\\secret.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Invalid file path", text);
    }

    [Fact]
    public async Task RenderPagePreview_InvalidPdf_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");
        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("could not be opened as a PDF", text);
    }

    [Fact]
    public async Task RenderPagePreview_PageOutOfRange_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 99 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("does not exist", text);
    }

    [Fact]
    public async Task RenderPagePreview_PageZero_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 0 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Page number must be 1 or greater", text);
    }

    [Fact]
    public async Task RenderPagePreview_DpiTooLow_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1, dpi = 50 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("72", text);
        Assert.Contains("600", text);
    }

    [Fact]
    public async Task RenderPagePreview_DpiTooHigh_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");

        var response = await CallToolAsync("render_page_preview", new { pdfPath, page = 1, dpi = 700 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("72", text);
        Assert.Contains("600", text);
    }

    #region Helper Methods

    private static (JsonElement? ImageBlock, JsonElement? TextBlock) FindContentBlocks(JsonElement contentArray)
    {
        JsonElement? imageBlock = null;
        JsonElement? textBlock = null;

        foreach (var block in contentArray.EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            if (type == "image")
                imageBlock = block;
            else if (type == "text")
                textBlock = block;
        }

        return (imageBlock, textBlock);
    }

    #endregion
}
