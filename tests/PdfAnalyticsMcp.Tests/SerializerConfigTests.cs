using System.Text.Json;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class SerializerConfigTests
{
    [Fact]
    public void Options_ProducesExpectedJsonFormat()
    {
        var obj = new { FirstName = "John", Address = (string?)null };
        var json = JsonSerializer.Serialize(obj, SerializerConfig.Options);

        // camelCase naming
        Assert.Contains("\"firstName\"", json);
        Assert.DoesNotContain("\"FirstName\"", json);

        // Nulls omitted
        Assert.DoesNotContain("address", json);
    }
}
