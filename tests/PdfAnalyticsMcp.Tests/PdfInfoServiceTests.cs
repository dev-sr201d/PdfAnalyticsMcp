using System.Text.Json;
using PdfAnalyticsMcp.Models;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class PdfInfoServiceTests
{
    private readonly PdfInfoService _service = new();

    private static string GetTestDataPath(string fileName)
    {
        var testAssemblyDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "tests", "TestData", fileName);
    }

    [Fact]
    public void Extract_WithMetadata_ReturnsCorrectMetadataFields()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = _service.Extract(path);

        Assert.Equal(3, result.PageCount);
        Assert.Equal("Test Document", result.Title);
        Assert.Equal("Test Author", result.Author);
        Assert.Equal("Test Subject", result.Subject);
        Assert.Equal("test, pdf, sample", result.Keywords);
        Assert.Equal("TestCreator", result.Creator);
        Assert.Equal("TestProducer", result.Producer);
    }

    [Fact]
    public void Extract_WithMetadata_ReturnsPredominantDimensionsAndExceptions()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = _service.Extract(path);

        // Predominant is US Letter (612 x 792) — 2 of 3 pages
        Assert.Equal(612.0, result.PredominantPageWidth);
        Assert.Equal(792.0, result.PredominantPageHeight);

        // Page 3 is A4 — should appear as the sole exception
        Assert.NotNull(result.PageSizeExceptions);
        Assert.Single(result.PageSizeExceptions);
        Assert.Equal(3, result.PageSizeExceptions[0].Number);
        Assert.Equal(595.0, result.PageSizeExceptions[0].Width);
        Assert.Equal(842.0, result.PageSizeExceptions[0].Height);
    }

    [Fact]
    public void Extract_UniformPageSizes_ReturnsNullExceptions()
    {
        var path = GetTestDataPath("sample-with-bookmarks.pdf");
        var result = _service.Extract(path);

        // Both pages are same size — no exceptions
        Assert.Null(result.PageSizeExceptions);
    }

    [Fact]
    public void Extract_WithoutMetadata_ReturnsNullMetadataFields()
    {
        var path = GetTestDataPath("sample-no-metadata.pdf");
        var result = _service.Extract(path);

        Assert.Equal(1, result.PageCount);
        Assert.Null(result.Title);
        Assert.Null(result.Author);
        Assert.Null(result.Subject);
        Assert.Null(result.Keywords);
        Assert.Null(result.Creator);
    }

    [Fact]
    public void Extract_WithoutBookmarks_ReturnsNullBookmarks()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");
        var result = _service.Extract(path);

        Assert.Null(result.Bookmarks);
    }

    [Fact]
    public void Extract_WithBookmarks_ReturnsHierarchicalTree()
    {
        var path = GetTestDataPath("sample-with-bookmarks.pdf");
        var result = _service.Extract(path);

        Assert.NotNull(result.Bookmarks);
        Assert.Equal(2, result.Bookmarks.Count);

        // Chapter 1 with child
        var chapter1 = result.Bookmarks[0];
        Assert.Equal("Chapter 1", chapter1.Title);
        Assert.Equal(1, chapter1.Page);
        Assert.NotNull(chapter1.Children);
        Assert.Single(chapter1.Children);
        Assert.Equal("Section 1.1", chapter1.Children[0].Title);
        Assert.Equal(1, chapter1.Children[0].Page);
        Assert.Null(chapter1.Children[0].Children);

        // Chapter 2 without children
        var chapter2 = result.Bookmarks[1];
        Assert.Equal("Chapter 2", chapter2.Title);
        Assert.Equal(2, chapter2.Page);
        Assert.Null(chapter2.Children);
    }

    [Fact]
    public void Extract_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = GetTestDataPath("not-a-pdf.txt");
        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path));
        Assert.Equal("The file could not be opened as a PDF.", ex.Message);
    }

    [Fact]
    public void Extract_LockedFile_ThrowsArgumentExceptionWithAccessMessage()
    {
        var source = GetTestDataPath("sample-with-metadata.pdf");
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        File.Copy(source, tempPath);
        try
        {
            using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = Assert.Throws<ArgumentException>(() => _service.Extract(tempPath));
            Assert.Equal($"The file could not be accessed: {tempPath}. It may be in use by another process.", ex.Message);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Extract_NullMetadata_SerializesAsOmitted()
    {
        var path = GetTestDataPath("sample-no-metadata.pdf");
        var result = _service.Extract(path);
        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);

        Assert.DoesNotContain("\"title\"", json);
        Assert.DoesNotContain("\"author\"", json);
        Assert.DoesNotContain("\"bookmarks\"", json);
        Assert.DoesNotContain("\"pageSizeExceptions\"", json);
    }
}
