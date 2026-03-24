using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfAnalyticsMcp.Services;

public static class SerializerConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
