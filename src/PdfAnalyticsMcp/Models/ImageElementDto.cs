namespace PdfAnalyticsMcp.Models;

public record ImageElementDto(
    double X,
    double Y,
    double W,
    double H,
    int PixelWidth,
    int PixelHeight,
    int BitsPerComponent,
    string? File);
