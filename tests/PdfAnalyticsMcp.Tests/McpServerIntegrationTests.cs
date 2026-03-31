using System.Diagnostics;

namespace PdfAnalyticsMcp.Tests;

public class McpServerIntegrationTests : McpIntegrationTestBase
{
    [Fact]
    public async Task Server_ShutsDownCleanly_WhenStdinCloses()
    {
        Assert.False(ServerProcess.HasExited);

        ServerProcess.StandardInput.Close();

        var exited = await WaitForExitAsync(ServerProcess, TimeSpan.FromSeconds(10));
        Assert.True(exited, "Server should exit after stdin is closed.");
        Assert.Equal(0, ServerProcess.ExitCode);
    }

    [Fact]
    public async Task Server_WritesLogOutput_ToStderr()
    {
        // Poll for stderr output with a generous timeout instead of a fixed delay
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.IsNullOrWhiteSpace(StderrOutput))
                break;
            await Task.Delay(200);
        }

        var stderr = StderrOutput;
        Assert.False(string.IsNullOrWhiteSpace(stderr), "Server should write log output to stderr.");
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
