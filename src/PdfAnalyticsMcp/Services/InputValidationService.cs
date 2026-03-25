namespace PdfAnalyticsMcp.Services;

public class InputValidationService : IInputValidationService
{
    public void ValidateFilePath(string? pdfPath)
    {
        if (string.IsNullOrEmpty(pdfPath))
            throw new ArgumentException("pdfPath is required.");

        if (pdfPath.Contains(".."))
            throw new ArgumentException("Invalid file path.");

        if (!File.Exists(pdfPath))
            throw new ArgumentException($"File not found: {pdfPath}");
    }

    public void ValidatePageNumber(int page, int pageCount)
    {
        if (page < 1)
            throw new ArgumentException("Page number must be 1 or greater.");

        if (page > pageCount)
            throw new ArgumentException($"Page {page} does not exist. The document has {pageCount} pages.");
    }

    public void ValidatePageMinimum(int page)
    {
        if (page < 1)
            throw new ArgumentException("Page number must be 1 or greater.");
    }

    public void ValidateGranularity(string? granularity)
    {
        if (granularity is null ||
            (!string.Equals(granularity, "words", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(granularity, "letters", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Granularity must be 'words' or 'letters'.");
        }
    }

    public void ValidateDpi(int dpi)
    {
        if (dpi < 72 || dpi > 600)
            throw new ArgumentException("DPI must be between 72 and 600.");
    }
}
