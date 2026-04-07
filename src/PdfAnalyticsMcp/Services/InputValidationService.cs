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

    public void ValidateFormat(string? format)
    {
        if (format is null ||
            (!string.Equals(format, "png", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Format must be 'png', 'jpeg', or 'jpg'.");
        }
    }

    public void ValidateQuality(int quality)
    {
        if (quality < 1 || quality > 100)
            throw new ArgumentException("Quality must be between 1 and 100.");
    }

    public void ValidateOutputPath(string? outputPath)
    {
        if (outputPath is null || !Path.IsPathRooted(outputPath))
            throw new ArgumentException("outputPath must be an absolute path.");

        if (outputPath.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid output path.");

        if (!Directory.Exists(outputPath))
            throw new ArgumentException($"Output directory does not exist: {outputPath}");
    }

    public void ValidateOutputFile(string? outputFile)
    {
        if (outputFile is null || !Path.IsPathRooted(outputFile))
            throw new ArgumentException("Output file path must be an absolute path.");

        if (outputFile.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Output file path must not contain path traversal sequences.");

        var parentDir = Path.GetDirectoryName(outputFile);
        if (parentDir is null || !Directory.Exists(parentDir))
            throw new ArgumentException("The parent directory of the output file path does not exist.");
    }
}
