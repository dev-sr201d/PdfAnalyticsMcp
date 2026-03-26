using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PdfAnalyticsMcp.Tests;

public class ErrorHandlingVerificationTests : IDisposable
{
    private readonly Process _serverProcess;
    private readonly StringBuilder _stderrOutput = new();
    private int _requestId;

    // All 5 tools
    private static readonly string[] AllTools =
        ["get_pdf_info", "get_page_text", "get_page_graphics", "get_page_images", "render_page_preview"];

    // Page-level tools (exclude get_pdf_info)
    private static readonly string[] PageLevelTools =
        ["get_page_text", "get_page_graphics", "get_page_images", "render_page_preview"];

    public ErrorHandlingVerificationTests()
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

    // ========================================================
    // Error Message Consistency Tests
    // ========================================================

    [Fact]
    public async Task AllTools_EmptyPdfPath_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var messages = new Dictionary<string, string>();
        foreach (var tool in AllTools)
        {
            var args = BuildToolArgs(tool, pdfPath: "", page: 1);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = "pdfPath is required.";
        AssertAllMessagesEqual(expected, messages);
    }

    [Fact]
    public async Task AllTools_NonexistentFile_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var pdfPath = "C:\\nonexistent\\missing.pdf";
        var messages = new Dictionary<string, string>();
        foreach (var tool in AllTools)
        {
            var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 1);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = $"File not found: {pdfPath}";
        AssertAllMessagesEqual(expected, messages);
    }

    [Fact]
    public async Task AllTools_PathTraversal_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var pdfPath = "C:\\docs\\..\\secret.pdf";
        var messages = new Dictionary<string, string>();
        foreach (var tool in AllTools)
        {
            var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 1);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = "Invalid file path.";
        AssertAllMessagesEqual(expected, messages);
    }

    [Fact]
    public async Task AllTools_NonPdfFile_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("not-a-pdf.txt");
        var messages = new Dictionary<string, string>();
        foreach (var tool in AllTools)
        {
            var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 1);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = "The file could not be opened as a PDF.";
        AssertAllMessagesEqual(expected, messages);
    }

    [Fact]
    public async Task AllTools_LockedFile_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        using var fileLock = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var messages = new Dictionary<string, string>();
        foreach (var tool in AllTools)
        {
            var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 1);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = $"The file could not be accessed: {pdfPath}. It may be in use by another process.";
        AssertAllMessagesEqual(expected, messages);
    }

    [Fact]
    public async Task PageLevelTools_PageZero_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        var messages = new Dictionary<string, string>();
        foreach (var tool in PageLevelTools)
        {
            var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 0);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = "Page number must be 1 or greater.";
        AssertAllMessagesEqual(expected, messages);
    }

    [Fact]
    public async Task PageLevelTools_PageExceedingCount_ReturnIdenticalErrorMessage()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        // sample-with-metadata.pdf has 2 pages
        var messages = new Dictionary<string, string>();
        foreach (var tool in PageLevelTools)
        {
            var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 99);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);
            messages[tool] = ExtractValidationMessage(GetErrorText(response));
        }

        var expected = "Page 99 does not exist. The document has 2 pages.";
        AssertAllMessagesEqual(expected, messages);
    }

    // ========================================================
    // File Access vs. Invalid PDF Distinction Tests
    // ========================================================

    [Fact]
    public async Task FileAccessVsInvalidPdf_PdfPigTool_ProduceDifferentErrors()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        var nonPdfPath = GetTestDataPath("not-a-pdf.txt");

        // Non-PDF file → invalid PDF error
        var nonPdfResponse = await CallToolAsync("get_pdf_info", new { pdfPath = nonPdfPath });
        Assert.NotNull(nonPdfResponse);
        var invalidPdfMessage = ExtractValidationMessage(GetErrorText(nonPdfResponse));

        // Locked file → file access error
        using var fileLock = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var lockedResponse = await CallToolAsync("get_pdf_info", new { pdfPath });
        Assert.NotNull(lockedResponse);
        var fileAccessMessage = ExtractValidationMessage(GetErrorText(lockedResponse));
        fileLock.Dispose();

        Assert.NotEqual(invalidPdfMessage, fileAccessMessage);
        Assert.Equal("The file could not be opened as a PDF.", invalidPdfMessage);
        Assert.Equal($"The file could not be accessed: {pdfPath}. It may be in use by another process.", fileAccessMessage);
    }

    [Fact]
    public async Task FileAccessVsInvalidPdf_DocnetTool_ProduceDifferentErrors()
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        var nonPdfPath = GetTestDataPath("not-a-pdf.txt");

        // Non-PDF file → invalid PDF error
        var nonPdfResponse = await CallToolAsync("render_page_preview",
            new { pdfPath = nonPdfPath, page = 1, dpi = 150 });
        Assert.NotNull(nonPdfResponse);
        var invalidPdfMessage = ExtractValidationMessage(GetErrorText(nonPdfResponse));

        // Locked file → file access error
        using var fileLock = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var lockedResponse = await CallToolAsync("render_page_preview",
            new { pdfPath, page = 1, dpi = 150 });
        Assert.NotNull(lockedResponse);
        var fileAccessMessage = ExtractValidationMessage(GetErrorText(lockedResponse));
        fileLock.Dispose();

        Assert.NotEqual(invalidPdfMessage, fileAccessMessage);
        Assert.Equal("The file could not be opened as a PDF.", invalidPdfMessage);
        Assert.Equal($"The file could not be accessed: {pdfPath}. It may be in use by another process.", fileAccessMessage);
    }

    // ========================================================
    // Server Continuity Tests
    // ========================================================

    [Fact]
    public async Task ServerContinuity_SuccessAfterFailure_AcrossTools()
    {
        await PerformHandshakeAsync();

        var validPdfPath = GetTestDataPath("sample-with-metadata.pdf");

        // 1. Call get_pdf_info with invalid input → expect error
        var response1 = await CallToolAsync("get_pdf_info", new { pdfPath = "" });
        Assert.NotNull(response1);
        Assert.True(IsErrorResponse(response1));

        // 2. Call get_pdf_info with valid input → expect success
        var response2 = await CallToolAsync("get_pdf_info", new { pdfPath = validPdfPath });
        Assert.NotNull(response2);
        Assert.False(IsErrorResponse(response2));
        var content2 = GetToolResultContent(response2);
        var json2 = JsonDocument.Parse(content2);
        Assert.Equal(2, json2.RootElement.GetProperty("pageCount").GetInt32());

        // 3. Call get_page_text with invalid input → expect error
        var response3 = await CallToolAsync("get_page_text",
            new { pdfPath = validPdfPath, page = 0, granularity = "words" });
        Assert.NotNull(response3);
        Assert.True(IsErrorResponse(response3));

        // 4. Call get_page_text with valid input → expect success
        var response4 = await CallToolAsync("get_page_text",
            new { pdfPath = validPdfPath, page = 1, granularity = "words" });
        Assert.NotNull(response4);
        Assert.False(IsErrorResponse(response4));
        var content4 = GetToolResultContent(response4);
        Assert.False(string.IsNullOrWhiteSpace(content4));

        // 5. Call get_page_graphics with nonexistent file → expect error
        var response5 = await CallToolAsync("get_page_graphics",
            new { pdfPath = "C:\\nonexistent\\missing.pdf", page = 1 });
        Assert.NotNull(response5);
        Assert.True(IsErrorResponse(response5));

        // 6. Call get_page_graphics with valid input → expect success
        var response6 = await CallToolAsync("get_page_graphics",
            new { pdfPath = validPdfPath, page = 1 });
        Assert.NotNull(response6);
        Assert.False(IsErrorResponse(response6));
    }

    // ========================================================
    // Error Response Sanitization Tests
    // ========================================================

    [Theory]
    [InlineData("get_pdf_info")]
    [InlineData("get_page_text")]
    [InlineData("get_page_graphics")]
    [InlineData("get_page_images")]
    [InlineData("render_page_preview")]
    public async Task ErrorResponse_NonexistentFile_DoesNotLeakInternalDetails(string tool)
    {
        await PerformHandshakeAsync();

        var args = BuildToolArgs(tool, pdfPath: "C:\\nonexistent\\missing.pdf", page: 1);
        var response = await CallToolAsync(tool, args);
        Assert.NotNull(response);

        var errorText = GetErrorText(response);
        AssertNoInternalDetailsLeaked(errorText);
    }

    [Theory]
    [InlineData("get_pdf_info")]
    [InlineData("get_page_text")]
    [InlineData("get_page_graphics")]
    [InlineData("get_page_images")]
    [InlineData("render_page_preview")]
    public async Task ErrorResponse_NonPdfFile_DoesNotLeakInternalDetails(string tool)
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("not-a-pdf.txt");
        var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 1);
        var response = await CallToolAsync(tool, args);
        Assert.NotNull(response);

        var errorText = GetErrorText(response);
        AssertNoInternalDetailsLeaked(errorText);
    }

    [Theory]
    [InlineData("get_page_text")]
    [InlineData("get_page_graphics")]
    [InlineData("get_page_images")]
    [InlineData("render_page_preview")]
    public async Task ErrorResponse_PageOutOfRange_DoesNotLeakInternalDetails(string tool)
    {
        await PerformHandshakeAsync();

        var pdfPath = GetTestDataPath("sample-with-metadata.pdf");
        var args = BuildToolArgs(tool, pdfPath: pdfPath, page: 99);
        var response = await CallToolAsync(tool, args);
        Assert.NotNull(response);

        var errorText = GetErrorText(response);
        AssertNoInternalDetailsLeaked(errorText);
    }

    [Theory]
    [InlineData("get_pdf_info")]
    [InlineData("get_page_text")]
    [InlineData("get_page_graphics")]
    [InlineData("get_page_images")]
    [InlineData("render_page_preview")]
    public async Task ErrorResponse_EmptyPath_DoesNotLeakInternalDetails(string tool)
    {
        await PerformHandshakeAsync();

        var args = BuildToolArgs(tool, pdfPath: "", page: 1);
        var response = await CallToolAsync(tool, args);
        Assert.NotNull(response);

        var errorText = GetErrorText(response);
        AssertNoInternalDetailsLeaked(errorText);
    }

    [Theory]
    [InlineData("get_pdf_info")]
    [InlineData("get_page_text")]
    [InlineData("get_page_graphics")]
    [InlineData("get_page_images")]
    [InlineData("render_page_preview")]
    public async Task ErrorResponse_LockedFile_DoesNotLeakInternalDetails(string tool)
    {
        await PerformHandshakeAsync();

        // Copy to a dedicated temp file in TestData so the exclusive lock doesn't
        // interfere with parallel tests that also open sample-with-metadata.pdf.
        var sourcePath = GetTestDataPath("sample-with-metadata.pdf");
        var tempPath = GetTestDataPath($"locked-{Guid.NewGuid()}.pdf");
        File.Copy(sourcePath, tempPath);
        try
        {
            using var fileLock = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var args = BuildToolArgs(tool, pdfPath: tempPath, page: 1);
            var response = await CallToolAsync(tool, args);
            Assert.NotNull(response);

            var errorText = GetErrorText(response);
            AssertNoInternalDetailsLeaked(errorText);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    // ========================================================
    // Helper Methods
    // ========================================================

    private static void AssertNoInternalDetailsLeaked(string errorText)
    {
        // No stack trace indicators
        Assert.DoesNotContain("   at ", errorText);
        Assert.DoesNotContain("StackTrace", errorText);

        // No .NET exception type names
        Assert.DoesNotContain("NullReferenceException", errorText);
        Assert.DoesNotContain("ArgumentException", errorText);
        Assert.DoesNotContain("InvalidOperationException", errorText);
        Assert.DoesNotContain("IOException", errorText);
        Assert.DoesNotContain("PdfDocument", errorText);
        Assert.DoesNotContain("FileNotFoundException", errorText);

        // No internal system paths (unless they were the user-provided input)
        Assert.DoesNotContain("\\Users\\", errorText);
        Assert.DoesNotContain("/home/", errorText);
        Assert.DoesNotContain("\\AppData\\", errorText);
    }

    /// <summary>
    /// Extracts the validation message from the SDK-wrapped error format.
    /// The MCP SDK wraps McpException messages as: "An error occurred invoking '{toolName}': {message}"
    /// </summary>
    private static string ExtractValidationMessage(string errorText)
    {
        const string separator = "': ";
        var separatorIndex = errorText.IndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex >= 0)
            return errorText[(separatorIndex + separator.Length)..];

        return errorText;
    }

    private static void AssertAllMessagesEqual(string expected, Dictionary<string, string> messages)
    {
        foreach (var (tool, message) in messages)
        {
            Assert.Equal(expected, message);
        }
    }

    private static object BuildToolArgs(string tool, string pdfPath, int page) => tool switch
    {
        "get_pdf_info" => new { pdfPath },
        "get_page_text" => (object)new { pdfPath, page, granularity = "words" },
        "get_page_graphics" => new { pdfPath, page },
        "get_page_images" => new { pdfPath, page, includeData = false },
        "render_page_preview" => new { pdfPath, page, dpi = 150 },
        _ => throw new ArgumentException($"Unknown tool: {tool}")
    };

    private static string GetErrorText(JsonDocument response)
    {
        var resultElement = response.RootElement.GetProperty("result");
        Assert.True(resultElement.GetProperty("isError").GetBoolean(),
            $"Expected an error response but got success. Content: {resultElement}");

        var content = resultElement.GetProperty("content");
        return content[0].GetProperty("text").GetString()!;
    }

    private static bool IsErrorResponse(JsonDocument response)
    {
        var resultElement = response.RootElement.GetProperty("result");
        return resultElement.TryGetProperty("isError", out var isError) && isError.GetBoolean();
    }

    private static string GetToolResultContent(JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");
        var content = result.GetProperty("content");
        return content[0].GetProperty("text").GetString()!;
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
            while (true)
            {
                var line = await _serverProcess.StandardOutput.ReadLineAsync(cts.Token);
                if (line is null)
                    return null;

                var doc = JsonDocument.Parse(line);

                // Skip JSON-RPC notifications (no "id" field) — only return actual responses
                if (doc.RootElement.TryGetProperty("id", out _))
                    return doc;

                doc.Dispose();
            }
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

    private static string GetTestDataPath(string fileName)
    {
        var testAssemblyDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "tests", "TestData", fileName);
    }
}
