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

    // ValidateFormat tests

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("jpg")]
    [InlineData("PNG")]
    [InlineData("Jpeg")]
    public void ValidateFormat_ValidValue_DoesNotThrow(string format)
    {
        _service.ValidateFormat(format);
    }

    [Theory]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateFormat_InvalidValue_ThrowsWithExpectedMessage(string? format)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateFormat(format));
        Assert.Equal("Format must be 'png', 'jpeg', or 'jpg'.", ex.Message);
    }

    // ValidateQuality tests

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void ValidateQuality_ValidValue_DoesNotThrow(int quality)
    {
        _service.ValidateQuality(quality);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-1)]
    public void ValidateQuality_InvalidValue_ThrowsWithExpectedMessage(int quality)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateQuality(quality));
        Assert.Equal("Quality must be between 1 and 100.", ex.Message);
    }

    // ValidateOutputPath tests

    [Fact]
    public void ValidateOutputPath_ValidAbsoluteDirectory_DoesNotThrow()
    {
        var tempDir = Path.GetTempPath();
        _service.ValidateOutputPath(tempDir);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("folder")]
    public void ValidateOutputPath_RelativePath_ThrowsWithExpectedMessage(string outputPath)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputPath(outputPath));
        Assert.Equal("outputPath must be an absolute path.", ex.Message);
    }

    [Fact]
    public void ValidateOutputPath_Null_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputPath(null));
        Assert.Equal("outputPath must be an absolute path.", ex.Message);
    }

    [Fact]
    public void ValidateOutputPath_ContainsTraversal_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputPath("C:\\output\\..\\secret"));
        Assert.Equal("Invalid output path.", ex.Message);
    }

    [Fact]
    public void ValidateOutputPath_NonexistentDirectory_ThrowsWithExpectedMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputPath(path));
        Assert.Equal($"Output directory does not exist: {path}", ex.Message);
    }

    // ValidateOutputFile tests

    [Fact]
    public void ValidateOutputFile_ValidAbsolutePathWithExistingParent_DoesNotThrow()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), "test-output.csv");
        _service.ValidateOutputFile(outputFile);
    }

    [Theory]
    [InlineData("relative/file.csv")]
    [InlineData("file.csv")]
    public void ValidateOutputFile_RelativePath_ThrowsWithExpectedMessage(string outputFile)
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputFile(outputFile));
        Assert.Equal("Output file path must be an absolute path.", ex.Message);
    }

    [Fact]
    public void ValidateOutputFile_Null_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputFile(null));
        Assert.Equal("Output file path must be an absolute path.", ex.Message);
    }

    [Fact]
    public void ValidateOutputFile_ContainsTraversal_ThrowsWithExpectedMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputFile("C:\\output\\..\\secret.csv"));
        Assert.Equal("Output file path must not contain path traversal sequences.", ex.Message);
    }

    [Fact]
    public void ValidateOutputFile_ParentDirectoryDoesNotExist_ThrowsWithExpectedMessage()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "output.csv");
        var ex = Assert.Throws<ArgumentException>(() => _service.ValidateOutputFile(outputFile));
        Assert.Equal("The parent directory of the output file path does not exist.", ex.Message);
    }
}
