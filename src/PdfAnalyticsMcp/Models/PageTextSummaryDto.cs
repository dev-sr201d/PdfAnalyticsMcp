namespace PdfAnalyticsMcp.Models;

public record PageTextSummaryDto(
    int Page,
    double Width,
    double Height,
    int ElementCount,
    string OutputFile,
    long SizeBytes);
