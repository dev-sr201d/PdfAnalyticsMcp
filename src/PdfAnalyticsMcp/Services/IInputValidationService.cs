namespace PdfAnalyticsMcp.Services;

public interface IInputValidationService
{
    void ValidateFilePath(string? pdfPath);
    void ValidatePageNumber(int page, int pageCount);
}
