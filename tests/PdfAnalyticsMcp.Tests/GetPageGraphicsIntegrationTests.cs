using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class GetPageGraphicsIntegrationTests : IDisposable
{
    private readonly Process _serverProcess;
    private readonly StringBuilder _stderrOutput = new();
    private int _requestId;

    public GetPageGraphicsIntegrationTests()
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
    public async Task GetPageGraphics_ToolDiscovery_ReturnsToolWithSchema()
    {
        await PerformHandshakeAsync();

        var toolsListRequest = CreateJsonRpcRequest("tools/list", new { });
        await SendMessageAsync(toolsListRequest);

        var toolsResponse = await ReadResponseAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(toolsResponse);

        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        JsonElement? getPageGraphicsTool = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.GetProperty("name").GetString() == "get_page_graphics")
            {
                getPageGraphicsTool = tool;
                break;
            }
        }

        Assert.NotNull(getPageGraphicsTool);
        Assert.True(getPageGraphicsTool.Value.TryGetProperty("description", out _));
        Assert.True(getPageGraphicsTool.Value.TryGetProperty("inputSchema", out var schema));
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("pdfPath", out _));
        Assert.True(props.TryGetProperty("page", out _));
        // Should NOT have granularity parameter
        Assert.False(props.TryGetProperty("granularity", out _));
    }

    [Fact]
    public async Task GetPageGraphics_RectangleExtraction_ReturnsRectangles()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.True(root.GetProperty("width").GetDouble() > 0);
        Assert.True(root.GetProperty("height").GetDouble() > 0);

        var rectangles = root.GetProperty("rectangles");
        Assert.True(rectangles.GetArrayLength() > 0);

        var rect = rectangles[0];
        Assert.True(rect.TryGetProperty("x", out _));
        Assert.True(rect.TryGetProperty("y", out _));
        Assert.True(rect.TryGetProperty("w", out _));
        Assert.True(rect.TryGetProperty("h", out _));
    }

    [Fact]
    public async Task GetPageGraphics_LineExtraction_ReturnsLines()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var lines = json.RootElement.GetProperty("lines");
        Assert.True(lines.GetArrayLength() > 0);

        var line = lines[0];
        Assert.True(line.TryGetProperty("x1", out _));
        Assert.True(line.TryGetProperty("y1", out _));
        Assert.True(line.TryGetProperty("x2", out _));
        Assert.True(line.TryGetProperty("y2", out _));
        Assert.True(line.TryGetProperty("strokeColor", out _));
    }

    [Fact]
    public async Task GetPageGraphics_ComplexPathExtraction_ReturnsPaths()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 2 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetArrayLength() > 0);

        var path = paths[0];
        Assert.True(path.TryGetProperty("x", out _));
        Assert.True(path.TryGetProperty("y", out _));
        Assert.True(path.TryGetProperty("w", out _));
        Assert.True(path.TryGetProperty("h", out _));
        Assert.True(path.TryGetProperty("vertexCount", out _));
    }

    [Fact]
    public async Task GetPageGraphics_ShapeColorsPresent_ReturnsValidHexColors()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var rectangles = json.RootElement.GetProperty("rectangles");

        // At least one rectangle should have a stroke color (DrawRectangle only strokes)
        bool hasStrokeColor = false;
        foreach (var rect in rectangles.EnumerateArray())
        {
            if (rect.TryGetProperty("strokeColor", out var color))
            {
                Assert.Matches(@"^#[0-9A-F]{6}$", color.GetString()!);
                hasStrokeColor = true;
            }
            if (rect.TryGetProperty("fillColor", out var fillColor))
            {
                Assert.Matches(@"^#[0-9A-F]{6}$", fillColor.GetString()!);
            }
        }
        Assert.True(hasStrokeColor, "Expected at least one rectangle with a stroke color.");
    }

    [Fact]
    public async Task GetPageGraphics_StrokeColorPresent_ReturnsHexColorAndWidth()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var lines = json.RootElement.GetProperty("lines");

        bool hasStroke = false;
        foreach (var line in lines.EnumerateArray())
        {
            if (line.TryGetProperty("strokeColor", out var strokeColor))
            {
                Assert.Matches(@"^#[0-9A-F]{6}$", strokeColor.GetString()!);
                hasStroke = true;
            }
            if (line.TryGetProperty("strokeWidth", out var sw))
            {
                Assert.True(sw.GetDouble() > 0);
            }
        }
        Assert.True(hasStroke, "Expected at least one line with stroke color.");
    }

    [Fact]
    public async Task GetPageGraphics_ColorFormatValidation_AllColorsAreHexRrggbb()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);

        // Check all color fields across all shape types
        foreach (var rect in json.RootElement.GetProperty("rectangles").EnumerateArray())
        {
            if (rect.TryGetProperty("fillColor", out var fc))
                Assert.Matches(@"^#[0-9A-F]{6}$", fc.GetString()!);
            if (rect.TryGetProperty("strokeColor", out var sc))
                Assert.Matches(@"^#[0-9A-F]{6}$", sc.GetString()!);
        }

        foreach (var line in json.RootElement.GetProperty("lines").EnumerateArray())
        {
            if (line.TryGetProperty("strokeColor", out var sc))
                Assert.Matches(@"^#[0-9A-F]{6}$", sc.GetString()!);
        }
    }

    [Fact]
    public async Task GetPageGraphics_EmptyGraphicsPage_ReturnsEmptyArrays()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateBlankTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-blank.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(0, root.GetProperty("rectangles").GetArrayLength());
        Assert.Equal(0, root.GetProperty("lines").GetArrayLength());
        Assert.Equal(0, root.GetProperty("paths").GetArrayLength());
    }

    [Fact]
    public async Task GetPageGraphics_EmptyPath_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_graphics", new { pdfPath = "", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("pdfPath is required", text);
    }

    [Fact]
    public async Task GetPageGraphics_FileNotFound_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_graphics", new { pdfPath = "C:\\nonexistent\\missing.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("File not found", text);
    }

    [Fact]
    public async Task GetPageGraphics_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_graphics", new { pdfPath = "C:\\docs\\..\\secret.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Invalid file path", text);
    }

    [Fact]
    public async Task GetPageGraphics_InvalidPdf_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");
        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("The file could not be opened as a PDF", text);
    }

    [Fact]
    public async Task GetPageGraphics_PageOutOfRange_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 99 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("does not exist", text);
    }

    [Fact]
    public async Task GetPageGraphics_PageZero_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateGraphicsTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-graphics.pdf");

        var response = await CallToolAsync("get_page_graphics", new { pdfPath, page = 0 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Page number must be 1 or greater", text);
    }

    #region Helper Methods

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
