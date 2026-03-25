namespace PdfAnalyticsMcp.Services;

public interface IInputValidationService
{
    void ValidateFilePath(string? pdfPath);
    void ValidatePageNumber(int page, int pageCount);
    void ValidatePageMinimum(int page);
    void ValidateGranularity(string? granularity);
    void ValidateDpi(int dpi);
}
