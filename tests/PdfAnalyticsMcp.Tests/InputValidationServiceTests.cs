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
}
