using System.Text;
using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class GetPageImagesIntegrationTests : McpIntegrationTestBase, IDisposable
{
    private readonly string _tempDir;

    public GetPageImagesIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PdfImagesIntegration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public new void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task GetPageImages_ToolDiscovery_ReturnsToolWithSchema()
    {
        await PerformHandshakeAsync();

        var toolsListRequest = CreateJsonRpcRequest("tools/list", new { });
        await SendMessageAsync(toolsListRequest);

        var toolsResponse = await ReadResponseAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(toolsResponse);

        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        JsonElement? getPageImagesTool = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.GetProperty("name").GetString() == "get_page_images")
            {
                getPageImagesTool = tool;
                break;
            }
        }

        Assert.NotNull(getPageImagesTool);
        Assert.True(getPageImagesTool.Value.TryGetProperty("description", out _));
        Assert.True(getPageImagesTool.Value.TryGetProperty("inputSchema", out var schema));
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("pdfPath", out _));
        Assert.True(props.TryGetProperty("page", out _));
        Assert.True(props.TryGetProperty("outputPath", out _));
    }

    [Fact]
    public async Task GetPageImages_ImageMetadataExtraction_ReturnsImageElements()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.True(root.GetProperty("width").GetDouble() > 0);
        Assert.True(root.GetProperty("height").GetDouble() > 0);

        var images = root.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        var image = images[0];
        Assert.True(image.TryGetProperty("x", out _));
        Assert.True(image.TryGetProperty("y", out _));
        Assert.True(image.TryGetProperty("w", out _));
        Assert.True(image.TryGetProperty("h", out _));
        Assert.True(image.TryGetProperty("pixelWidth", out _));
        Assert.True(image.TryGetProperty("pixelHeight", out _));
        Assert.True(image.TryGetProperty("bitsPerPixel", out _));
    }

    [Fact]
    public async Task GetPageImages_OutputPathOmitted_NoFileField()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        // Do NOT pass outputPath at all — test default behavior
        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        foreach (var image in images.EnumerateArray())
        {
            Assert.False(image.TryGetProperty("file", out _), "file field should be omitted when outputPath is not specified.");
        }
    }

    [Fact]
    public async Task GetPageImages_WithOutputPath_ExtractsFilesAndReturnsFilePaths()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = _tempDir });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        foreach (var image in images.EnumerateArray())
        {
            Assert.True(image.TryGetProperty("file", out var fileElement), "file field should be present when outputPath is provided.");
            var filePath = fileElement.GetString()!;
            Assert.True(Path.IsPathRooted(filePath), "file field should be an absolute path.");
            Assert.True(File.Exists(filePath), $"Expected PNG file to exist: {filePath}");
        }
    }

    [Fact]
    public async Task GetPageImages_WithOutputPath_FileNamingConvention()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = _tempDir });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        var file = images[0].GetProperty("file").GetString()!;
        Assert.Equal("sample-image_p1_img1.png", Path.GetFileName(file));
    }

    [Fact]
    public async Task GetPageImages_WithOutputPath_ExtractedFilesAreValidPngs()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = _tempDir });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");

        foreach (var image in images.EnumerateArray())
        {
            if (image.TryGetProperty("file", out var fileElement))
            {
                var bytes = File.ReadAllBytes(fileElement.GetString()!);
                Assert.True(bytes.Length >= 4);
                Assert.Equal(0x89, bytes[0]); // PNG signature
                Assert.Equal((byte)'P', bytes[1]);
                Assert.Equal((byte)'N', bytes[2]);
                Assert.Equal((byte)'G', bytes[3]);
            }
        }
    }

    [Fact]
    public async Task GetPageImages_WithOutputPath_ExtractedJpegFilesAreValid()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateJpegImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-jpeg-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = _tempDir });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        var image = images[0];
        Assert.True(image.TryGetProperty("file", out var fileElement), "file field should be present when outputPath is provided.");
        var filePath = fileElement.GetString()!;
        Assert.True(File.Exists(filePath), $"Expected JPEG file to exist: {filePath}");
        Assert.EndsWith(".jpg", filePath, StringComparison.OrdinalIgnoreCase);

        // Verify JPEG SOI and EOI markers
        var bytes = File.ReadAllBytes(filePath);
        Assert.True(bytes.Length >= 4, "JPEG file too small.");
        Assert.Equal(0xFF, bytes[0]); // SOI marker
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(0xFF, bytes[^2]); // EOI marker
        Assert.Equal(0xD9, bytes[^1]);
    }

    [Fact]
    public async Task GetPageImages_EmptyImagesPage_ReturnsEmptyArray()
    {
        await PerformHandshakeAsync();

        // Use a text-only PDF with no images
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-no-metadata.pdf");
        Assert.True(File.Exists(pdfPath), $"Test data file not found: {pdfPath}");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.Equal(0, images.GetArrayLength());
    }

    [Fact]
    public async Task GetPageImages_CoordinateRounding_AtMostOneDecimalPlace()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);

        // Check page dimensions
        AssertAtMostOneDecimalPlace(json.RootElement.GetProperty("width").GetDouble());
        AssertAtMostOneDecimalPlace(json.RootElement.GetProperty("height").GetDouble());

        // Check image coordinates
        foreach (var image in json.RootElement.GetProperty("images").EnumerateArray())
        {
            AssertAtMostOneDecimalPlace(image.GetProperty("x").GetDouble());
            AssertAtMostOneDecimalPlace(image.GetProperty("y").GetDouble());
            AssertAtMostOneDecimalPlace(image.GetProperty("w").GetDouble());
            AssertAtMostOneDecimalPlace(image.GetProperty("h").GetDouble());
        }
    }

    [Fact]
    public async Task GetPageImages_ResponseSizeWithoutOutputPath_Under30KB()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var sizeInBytes = Encoding.UTF8.GetByteCount(result);
        Assert.True(sizeInBytes < 30_000, $"Response size {sizeInBytes} bytes exceeds 30 KB limit.");
    }

    [Fact]
    public async Task GetPageImages_EmptyPath_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_images", new { pdfPath = "", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("pdfPath is required", text);
    }

    [Fact]
    public async Task GetPageImages_FileNotFound_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_images", new { pdfPath = "C:\\nonexistent\\missing.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("File not found", text);
    }

    [Fact]
    public async Task GetPageImages_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_images", new { pdfPath = "C:\\docs\\..\\secret.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Invalid file path", text);
    }

    [Fact]
    public async Task GetPageImages_InvalidPdf_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");
        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("The file could not be opened as a PDF", text);
    }

    [Fact]
    public async Task GetPageImages_PageOutOfRange_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 99 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("does not exist", text);
    }

    [Fact]
    public async Task GetPageImages_PageZero_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 0 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Page number must be 1 or greater", text);
    }

    [Fact]
    public async Task GetPageImages_InvalidOutputPath_RelativePath_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = "relative/path" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("absolute", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPageImages_InvalidOutputPath_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = @"C:\temp\..\secret" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Invalid output path", text);
    }

    [Fact]
    public async Task GetPageImages_InvalidOutputPath_NonExistentDirectory_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var nonExistent = Path.Combine(_tempDir, "does_not_exist_subdir");
        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = nonExistent });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("does not exist", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPageImages_FormXObjectRecursion_DiscoversImageInsideFormXObject()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateFormXObjectImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-formxobj-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = _tempDir });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("page").GetInt32());

        var images = root.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0, "Expected at least one image discovered inside the Form XObject.");

        var image = images[0];
        Assert.True(image.TryGetProperty("x", out _));
        Assert.True(image.TryGetProperty("y", out _));
        Assert.True(image.TryGetProperty("w", out _));
        Assert.True(image.TryGetProperty("h", out _));
        Assert.True(image.TryGetProperty("pixelWidth", out _));
        Assert.True(image.TryGetProperty("pixelHeight", out _));
        Assert.True(image.TryGetProperty("bitsPerPixel", out _));

        // Verify the image was extracted as a valid PNG file
        Assert.True(image.TryGetProperty("file", out var fileElement), "file field should be present when outputPath is provided.");
        var filePath = fileElement.GetString()!;
        Assert.True(File.Exists(filePath), $"Expected PNG file to exist: {filePath}");

        var bytes = File.ReadAllBytes(filePath);
        Assert.True(bytes.Length >= 4);
        Assert.Equal(0x89, bytes[0]); // PNG signature
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public async Task GetPageImages_DuplicateFormXObject_ReportsBothOccurrences()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateDuplicateFormXObjectImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-dupformxobj-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, outputPath = _tempDir });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");

        // The same Form XObject is referenced twice — PDFium creates separate object instances
        // for each reference, so both occurrences are reported with their own extraction
        Assert.True(images.GetArrayLength() >= 2, "Expected at least two image occurrences from duplicate Form XObject references.");

        // Both occurrences should have positive dimensions and valid metadata
        foreach (var image in images.EnumerateArray())
        {
            Assert.True(image.GetProperty("w").GetDouble() > 0);
            Assert.True(image.GetProperty("h").GetDouble() > 0);
            Assert.True(image.TryGetProperty("pixelWidth", out _));
            Assert.True(image.TryGetProperty("pixelHeight", out _));
            Assert.True(image.TryGetProperty("bitsPerPixel", out _));
        }

        // Both occurrences should have file paths since outputPath was provided
        foreach (var image in images.EnumerateArray())
        {
            Assert.True(image.TryGetProperty("file", out var fileElement), "file field should be present when outputPath is provided.");
            var filePath = fileElement.GetString()!;
            Assert.True(File.Exists(filePath), $"Expected PNG file to exist: {filePath}");

            // Verify valid PNG
            var bytes = File.ReadAllBytes(filePath);
            Assert.True(bytes.Length >= 4);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }
    }

    private static void AssertAtMostOneDecimalPlace(double value)
    {
        var rounded = Math.Round(value, 1);
        Assert.Equal(rounded, value, precision: 10);
    }
}
