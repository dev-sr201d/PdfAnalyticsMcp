using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class GetPageTextIntegrationTests : IDisposable
{
    private readonly Process _serverProcess;
    private readonly StringBuilder _stderrOutput = new();
    private int _requestId;

    public GetPageTextIntegrationTests()
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
    public async Task GetPageText_ToolDiscovery_ReturnsToolWithSchema()
    {
        await PerformHandshakeAsync();

        var toolsListRequest = CreateJsonRpcRequest("tools/list", new { });
        await SendMessageAsync(toolsListRequest);

        var toolsResponse = await ReadResponseAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(toolsResponse);

        var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
        JsonElement? getPageTextTool = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (tool.GetProperty("name").GetString() == "get_page_text")
            {
                getPageTextTool = tool;
                break;
            }
        }

        Assert.NotNull(getPageTextTool);
        Assert.True(getPageTextTool.Value.TryGetProperty("description", out _));
        Assert.True(getPageTextTool.Value.TryGetProperty("inputSchema", out var schema));
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("pdfPath", out _));
        Assert.True(props.TryGetProperty("page", out _));
        Assert.True(props.TryGetProperty("granularity", out _));
    }

    [Fact]
    public async Task GetPageText_WordGranularity_ReturnsWordElements()
    {
        await PerformHandshakeAsync();

        // Ensure test PDF exists
        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(response);

        var result = GetToolResultContent(response);
        var json = JsonDocument.Parse(result);
        var root = json.RootElement;

        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.True(root.GetProperty("width").GetDouble() > 0);
        Assert.True(root.GetProperty("height").GetDouble() > 0);

        var elements = root.GetProperty("elements");
        Assert.True(elements.GetArrayLength() > 0);

        var first = elements[0];
        Assert.True(first.TryGetProperty("text", out _));
        Assert.True(first.TryGetProperty("x", out _));
        Assert.True(first.TryGetProperty("y", out _));
        Assert.True(first.TryGetProperty("w", out _));
        Assert.True(first.TryGetProperty("h", out _));
        Assert.True(first.TryGetProperty("font", out _));
        Assert.True(first.TryGetProperty("size", out _));
    }

    [Fact]
    public async Task GetPageText_LetterGranularity_ReturnsMoreElementsThanWords()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var wordResponse = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(wordResponse);
        var wordResult = JsonDocument.Parse(GetToolResultContent(wordResponse));
        var wordCount = wordResult.RootElement.GetProperty("elements").GetArrayLength();

        // Need a new server for the second call — but we can reuse the same process
        var letterResponse = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "letters" });
        Assert.NotNull(letterResponse);
        var letterResult = JsonDocument.Parse(GetToolResultContent(letterResponse));
        var letterCount = letterResult.RootElement.GetProperty("elements").GetArrayLength();

        Assert.True(letterCount > wordCount, $"Letter count ({letterCount}) should exceed word count ({wordCount}).");
    }

    [Fact]
    public async Task GetPageText_DefaultGranularity_ReturnsWordLevelOutput()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        // Call with explicit "words"
        var wordsResponse = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(wordsResponse);
        var wordsResult = JsonDocument.Parse(GetToolResultContent(wordsResponse));
        var wordsCount = wordsResult.RootElement.GetProperty("elements").GetArrayLength();

        // Call without granularity (default should be "words")
        var defaultResponse = await CallToolAsync("get_page_text", new { pdfPath, page = 1 });
        Assert.NotNull(defaultResponse);
        var defaultResult = JsonDocument.Parse(GetToolResultContent(defaultResponse));
        var defaultCount = defaultResult.RootElement.GetProperty("elements").GetArrayLength();

        Assert.Equal(wordsCount, defaultCount);
    }

    [Fact]
    public async Task GetPageText_ColorMetadata_NonBlackHasColorBlackOmitted()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(response);
        var result = JsonDocument.Parse(GetToolResultContent(response));
        var elements = result.RootElement.GetProperty("elements");

        // Find the "Red" word - should have color
        JsonElement? redElement = null;
        JsonElement? helloElement = null;
        foreach (var el in elements.EnumerateArray())
        {
            var text = el.GetProperty("text").GetString();
            if (text == "Red") redElement = el;
            if (text == "Hello") helloElement = el;
        }

        Assert.NotNull(redElement);
        Assert.True(redElement.Value.TryGetProperty("color", out var colorProp));
        var color = colorProp.GetString()!;
        Assert.Matches(@"^#[0-9A-F]{6}$", color);

        Assert.NotNull(helloElement);
        Assert.False(helloElement.Value.TryGetProperty("color", out _), "Black text should omit color field.");
    }

    [Fact]
    public async Task GetPageText_BoldItalicFlags_PresentWhenTrueOmittedWhenFalse()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(response);
        var result = JsonDocument.Parse(GetToolResultContent(response));
        var elements = result.RootElement.GetProperty("elements");

        JsonElement? boldElement = null;
        JsonElement? regularElement = null;
        foreach (var el in elements.EnumerateArray())
        {
            var text = el.GetProperty("text").GetString();
            if (text == "Bold") boldElement = el;
            if (text == "Hello") regularElement = el;
        }

        // Bold word should have bold = true
        Assert.NotNull(boldElement);
        Assert.True(boldElement.Value.TryGetProperty("bold", out var boldProp));
        Assert.True(boldProp.GetBoolean());

        // Regular word should omit bold and italic
        Assert.NotNull(regularElement);
        Assert.False(regularElement.Value.TryGetProperty("bold", out _));
        Assert.False(regularElement.Value.TryGetProperty("italic", out _));
    }

    [Fact]
    public async Task GetPageText_ResponseSize_Under30KB()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateLargeTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text-large.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(response);

        var resultText = GetToolResultContent(response);
        var sizeInBytes = Encoding.UTF8.GetByteCount(resultText);

        Assert.True(sizeInBytes <= 30 * 1024, $"Response size {sizeInBytes} bytes exceeds 30 KB limit.");
    }

    [Fact]
    public async Task GetPageText_EmptyPath_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_text", new { pdfPath = "", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("pdfPath is required", text);
    }

    [Fact]
    public async Task GetPageText_FileNotFound_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_text", new { pdfPath = "C:\\nonexistent\\missing.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("File not found", text);
    }

    [Fact]
    public async Task GetPageText_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        var response = await CallToolAsync("get_page_text", new { pdfPath = "C:\\docs\\..\\secret.pdf", page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Invalid file path", text);
    }

    [Fact]
    public async Task GetPageText_InvalidPdf_ReturnsError()
    {
        await PerformHandshakeAsync();

        var pdfPath = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");
        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("The file could not be opened as a PDF", text);
    }

    [Fact]
    public async Task GetPageText_PageOutOfRange_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 99 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("does not exist", text);
    }

    [Fact]
    public async Task GetPageText_PageZero_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 0 });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Page number must be 1 or greater", text);
    }

    [Fact]
    public async Task GetPageText_InvalidGranularity_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "paragraphs" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("Granularity must be 'words' or 'letters'.", text);
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
