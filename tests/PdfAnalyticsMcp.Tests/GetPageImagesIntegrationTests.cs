using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class GetPageImagesIntegrationTests : IDisposable
{
    private readonly Process _serverProcess;
    private readonly StringBuilder _stderrOutput = new();
    private int _requestId;

    public GetPageImagesIntegrationTests()
    {
        var serverExePath = GetServerExePath();

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = serverExePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _serverProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _stderrOutput.AppendLine(e.Data);
        };

        _serverProcess.Start();
        _serverProcess.BeginErrorReadLine();
    }

    public void Dispose()
    {
        try
        {
            if (!_serverProcess.HasExited)
            {
                _serverProcess.StandardInput.Close();
                _serverProcess.WaitForExit(5000);
                if (!_serverProcess.HasExited)
                    _serverProcess.Kill();
            }
        }
        catch
        {
            // Best-effort cleanup
        }

        _serverProcess.Dispose();
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
        Assert.True(props.TryGetProperty("includeData", out _));
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
        Assert.True(image.TryGetProperty("bitsPerComponent", out _));
    }

    [Fact]
    public async Task GetPageImages_IncludeDataOmitted_NoDataField()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        // Do NOT pass includeData at all — test default behavior
        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        foreach (var image in images.EnumerateArray())
        {
            Assert.False(image.TryGetProperty("data", out _), "data field should be omitted when includeData is not specified.");
        }
    }

    [Fact]
    public async Task GetPageImages_IncludeDataFalse_NoDataField()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, includeData = false });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        foreach (var image in images.EnumerateArray())
        {
            Assert.False(image.TryGetProperty("data", out _), "data field should be omitted when includeData is false.");
        }
    }

    [Fact]
    public async Task GetPageImages_IncludeDataTrue_ReturnsBase64Data()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, includeData = true });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");
        Assert.True(images.GetArrayLength() > 0);

        bool hasData = false;
        foreach (var image in images.EnumerateArray())
        {
            if (image.TryGetProperty("data", out var dataElement))
            {
                var dataStr = dataElement.GetString();
                Assert.False(string.IsNullOrEmpty(dataStr));
                hasData = true;
            }
        }
        Assert.True(hasData, "Expected at least one image with base64 data when includeData is true.");
    }

    [Fact]
    public async Task GetPageImages_Base64FormatValidation_NoPrefixAndValidPng()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateImageTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-image.pdf");

        var response = await CallToolAsync("get_page_images", new { pdfPath, page = 1, includeData = true });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var images = json.RootElement.GetProperty("images");

        foreach (var image in images.EnumerateArray())
        {
            if (image.TryGetProperty("data", out var dataElement))
            {
                var dataStr = dataElement.GetString()!;

                // Verify no data URI prefix
                Assert.DoesNotContain("data:", dataStr);
                Assert.DoesNotContain("base64,", dataStr);

                // Decode and verify PNG header
                var bytes = Convert.FromBase64String(dataStr);
                Assert.True(bytes.Length >= 4);
                Assert.Equal(0x89, bytes[0]); // PNG signature
                Assert.Equal((byte)'P', bytes[1]);
                Assert.Equal((byte)'N', bytes[2]);
                Assert.Equal((byte)'G', bytes[3]);
            }
        }
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
    public async Task GetPageImages_ResponseSizeWithoutData_Under30KB()
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

    #region Helper Methods

    private static void AssertAtMostOneDecimalPlace(double value)
    {
        var rounded = Math.Round(value, 1);
        Assert.Equal(rounded, value, precision: 10);
    }

    private async Task PerformHandshakeAsync()
    {
        var initializeRequest = CreateJsonRpcRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0.0" }
        });
        await SendMessageAsync(initializeRequest);
        await ReadResponseAsync(TimeSpan.FromSeconds(10));

        var initializedNotification = CreateJsonRpcNotification("notifications/initialized");
        await SendMessageAsync(initializedNotification);
    }

    private async Task<JsonDocument?> CallToolAsync(string toolName, object arguments)
    {
        var request = CreateJsonRpcRequest("tools/call", new
        {
            name = toolName,
            arguments
        });
        await SendMessageAsync(request);
        return await ReadResponseAsync(TimeSpan.FromSeconds(10));
    }

    private static string GetToolResultContent(JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");
        var content = result.GetProperty("content");
        return content[0].GetProperty("text").GetString()!;
    }

    private string CreateJsonRpcRequest(string method, object? @params)
    {
        var id = ++_requestId;
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };
        return JsonSerializer.Serialize(request);
    }

    private static string CreateJsonRpcNotification(string method)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method
        };
        return JsonSerializer.Serialize(notification);
    }

    private async Task SendMessageAsync(string message)
    {
        await _serverProcess.StandardInput.WriteLineAsync(message);
        await _serverProcess.StandardInput.FlushAsync();
    }

    private async Task<JsonDocument?> ReadResponseAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            var line = await _serverProcess.StandardOutput.ReadLineAsync(cts.Token);
            if (line is null)
                return null;

            return JsonDocument.Parse(line);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static string GetServerExePath()
    {
        var testAssemblyDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        var serverExe = Path.Combine(repoRoot, "src", "PdfAnalyticsMcp", "bin", "Debug", "net9.0",
            OperatingSystem.IsWindows() ? "PdfAnalyticsMcp.exe" : "PdfAnalyticsMcp");

        if (!File.Exists(serverExe))
        {
            throw new FileNotFoundException(
                $"Server executable not found at '{serverExe}'. Build the server project first.");
        }

        return serverExe;
    }

    #endregion
}
