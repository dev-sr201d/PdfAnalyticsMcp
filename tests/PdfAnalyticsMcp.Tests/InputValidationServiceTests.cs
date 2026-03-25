using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class InputValidationServiceTests
{
    private readonly InputValidationService _service = new();

    [Fact]
    public void ValidateFilePath_NullPath_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateFilePath(null));
        Assert.Equal("pdfPath is required.", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_EmptyPath_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateFilePath(""));
        Assert.Equal("pdfPath is required.", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_PathWithTraversal_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateFilePath("C:\\docs\\..\\secret.pdf"));
        Assert.Equal("Invalid file path.", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_NonexistentFile_ThrowsWithExpectedMessage()
    {
        var path = "C:\\nonexistent\\file.pdf";
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateFilePath(path));
        Assert.Equal($"File not found: {path}", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_ExistingFile_DoesNotThrow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            _service.ValidateFilePath(tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidatePageNumber_LessThanOne_ThrowsWithExpectedMessage(int page)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidatePageNumber(page, 10));
        Assert.Equal("Page number must be 1 or greater.", ex.Message);
    }

    [Fact]
    public void ValidatePageNumber_ExceedsPageCount_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidatePageNumber(11, 10));
        Assert.Equal("Page 11 does not exist. The document has 10 pages.", ex.Message);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 10)]
    [InlineData(1, 1)]
    public void ValidatePageNumber_ValidPage_DoesNotThrow(int page, int pageCount)
    {
        _service.ValidatePageNumber(page, pageCount);
    }

    // ValidatePageMinimum tests

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void ValidatePageMinimum_ValidPage_DoesNotThrow(int page)
    {
        _service.ValidatePageMinimum(page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ValidatePageMinimum_InvalidPage_ThrowsWithExpectedMessage(int page)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidatePageMinimum(page));
        Assert.Equal("Page number must be 1 or greater.", ex.Message);
    }

    // ValidateGranularity tests

    [Theory]
    [InlineData("words")]
    [InlineData("letters")]
    [InlineData("Words")]
    [InlineData("LETTERS")]
    public void ValidateGranularity_ValidValue_DoesNotThrow(string granularity)
    {
        _service.ValidateGranularity(granularity);
    }

    [Theory]
    [InlineData("sentences")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateGranularity_InvalidValue_ThrowsWithExpectedMessage(string? granularity)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateGranularity(granularity));
        Assert.Equal("Granularity must be 'words' or 'letters'.", ex.Message);
    }

    // ValidateDpi tests

    [Theory]
    [InlineData(72)]
    [InlineData(150)]
    [InlineData(600)]
    public void ValidateDpi_ValidValue_DoesNotThrow(int dpi)
    {
        _service.ValidateDpi(dpi);
    }

    [Theory]
    [InlineData(71)]
    [InlineData(601)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateDpi_InvalidValue_ThrowsWithExpectedMessage(int dpi)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateDpi(dpi));
        Assert.Equal("DPI must be between 72 and 600.", ex.Message);
    }
}
