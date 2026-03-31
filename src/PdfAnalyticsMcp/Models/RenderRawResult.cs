namespace PdfAnalyticsMcp.Models;

public record RenderRawResult(
    int Width,
    int Height,
    byte[] BgraData);
