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
        Assert.True(props.TryGetProperty("outputFile", out _));
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

    [Fact]
    public async Task GetPageText_OutputFile_WritesSummaryAndFile()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

        try
        {
            var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words", outputFile });
            Assert.NotNull(response);

            var resultText = GetToolResultContent(response);
            var sizeInBytes = Encoding.UTF8.GetByteCount(resultText);
            Assert.True(sizeInBytes < 1024, $"Summary response should be < 1 KB but was {sizeInBytes} bytes.");

            var summary = JsonDocument.Parse(resultText);
            var root = summary.RootElement;
            Assert.True(root.TryGetProperty("page", out _));
            Assert.True(root.TryGetProperty("elementCount", out var elementCount));
            Assert.True(elementCount.GetInt32() > 0);
            Assert.True(root.TryGetProperty("outputFile", out var outputFileProp));
            Assert.Equal(Path.GetFullPath(outputFile), outputFileProp.GetString());
            Assert.True(root.TryGetProperty("sizeBytes", out var sizeBytesProp));
            Assert.True(sizeBytesProp.GetInt64() > 0);

            // Verify file was written as CSV with header and data rows
            Assert.True(File.Exists(outputFile));
            var lines = File.ReadAllLines(outputFile);
            Assert.Equal("text,x,y,w,h,font,size,color,bold,italic", lines[0]);
            Assert.Equal(elementCount.GetInt32(), lines.Length - 1);

            // Verify bold elements have "true" in bold column, non-bold have empty
            var boldLine = lines.Skip(1).FirstOrDefault(l => l.StartsWith("Bold,"));
            Assert.NotNull(boldLine);
            Assert.Contains(",true,", boldLine); // bold column

            var helloLine = lines.Skip(1).First(l => l.StartsWith("Hello,"));
            // Hello is regular font — bold and italic columns should be empty (line ends with ,,)
            Assert.EndsWith(",,", helloLine);
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public async Task GetPageText_OutputFile_DensePage_SummaryUnder1KB()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateDenseTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text-dense.pdf");
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

        try
        {
            var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words", outputFile });
            Assert.NotNull(response);

            var resultText = GetToolResultContent(response);
            var summarySize = Encoding.UTF8.GetByteCount(resultText);
            Assert.True(summarySize < 1024, $"Summary response should be < 1 KB but was {summarySize} bytes.");

            // Verify CSV is smaller than equivalent JSON would be
            var csvSize = new FileInfo(outputFile).Length;
            // Get inline JSON size for comparison
            var inlineResponse = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
            var inlineText = GetToolResultContent(inlineResponse!);
            var jsonSize = Encoding.UTF8.GetByteCount(inlineText);
            Assert.True(csvSize < jsonSize, $"CSV size ({csvSize}) should be smaller than JSON size ({jsonSize}).");
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public async Task GetPageText_OutputFile_OverwritesExisting()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

        try
        {
            File.WriteAllText(outputFile, "old content");

            var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words", outputFile });
            Assert.NotNull(response);

            var fileContent = File.ReadAllText(outputFile);
            Assert.DoesNotContain("old content", fileContent);
            Assert.StartsWith("text,x,y,w,h,font,size,color,bold,italic", fileContent);
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public async Task GetPageText_NoOutputFile_ReturnsFullDataInline()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, granularity = "words" });
        Assert.NotNull(response);

        var resultText = GetToolResultContent(response);
        var doc = JsonDocument.Parse(resultText);
        Assert.True(doc.RootElement.TryGetProperty("elements", out _));
        // Should NOT have summary-specific fields
        Assert.False(doc.RootElement.TryGetProperty("elementCount", out _));
        Assert.False(doc.RootElement.TryGetProperty("outputFile", out _));
    }

    [Fact]
    public async Task GetPageText_OutputFile_RelativePath_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, outputFile = "relative/output.json" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("absolute path", text);
    }

    [Fact]
    public async Task GetPageText_OutputFile_PathTraversal_ReturnsError()
    {
        await PerformHandshakeAsync();

        TestPdfGenerator.CreateTextTestPdf();
        var pdfPath = TestPdfGenerator.GetTestDataPath("sample-text.pdf");

        var response = await CallToolAsync("get_page_text", new { pdfPath, page = 1, outputFile = "C:\\temp\\..\\output.json" });
        Assert.NotNull(response);

        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean());

        var text = resultElement.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("path traversal", text);
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
