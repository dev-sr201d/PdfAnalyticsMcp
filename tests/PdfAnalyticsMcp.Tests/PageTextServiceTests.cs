using System.Text.Json;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class PageTextServiceTests
{
    private readonly PageTextService _service = new(new InputValidationService());

    [Fact]
    public void Extract_WordGranularity_ReturnsWordElements()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        Assert.Equal(1, result.Page);
        Assert.True(result.Elements.Count > 0);
        // "Hello World" + "Bold Text" + "Red Text" + "Italic Text" + "BoldItalic Text" = multiple words
        var texts = result.Elements.Select(e => e.Text).ToList();
        Assert.Contains("Hello", texts);
        Assert.Contains("World", texts);
    }

    [Fact]
    public void Extract_LetterGranularity_ReturnsSingleCharacterElements()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "letters");

        Assert.True(result.Elements.Count > 0);
        // Letters should be individual characters
        Assert.All(result.Elements, e => Assert.True(e.Text.Length <= 2));

        // Letter count should be greater than word count
        var wordResult = _service.Extract(path, 1, "words");
        Assert.True(result.Elements.Count > wordResult.Elements.Count);
    }

    [Fact]
    public void Extract_WordGranularity_DerivesFontMetadataFromLetters()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var helloWord = result.Elements.First(e => e.Text == "Hello");
        Assert.Contains("Helvetica", helloWord.Font);
        Assert.Equal(12.0, helloWord.Size);
    }

    [Fact]
    public void Extract_BoldFont_InfersBoldFlag()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var boldWord = result.Elements.First(e => e.Text == "Bold");
        Assert.True(boldWord.Bold);
        Assert.Null(boldWord.Italic);
    }

    [Fact]
    public void Extract_ItalicFont_InfersItalicFlag()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        // HelveticaOblique should set italic = true
        var italicWord = result.Elements.First(e => e.Text == "Italic");
        Assert.Null(italicWord.Bold);
        Assert.True(italicWord.Italic);
    }

    [Fact]
    public void Extract_BoldItalicFont_InfersBothFlags()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var boldItalicWord = result.Elements.First(e => e.Text == "BoldItalic");
        Assert.True(boldItalicWord.Bold);
        Assert.True(boldItalicWord.Italic);
    }

    [Fact]
    public void Extract_RegularFont_OmitsBoldAndItalicFlags()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var helloWord = result.Elements.First(e => e.Text == "Hello");
        Assert.Null(helloWord.Bold);
        Assert.Null(helloWord.Italic);
    }

    [Fact]
    public void Extract_RedText_ReturnsColorHex()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var redWord = result.Elements.First(e => e.Text == "Red");
        Assert.Equal("#FF0000", redWord.Color);
    }

    [Fact]
    public void Extract_BlackText_OmitsColor()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var helloWord = result.Elements.First(e => e.Text == "Hello");
        Assert.Null(helloWord.Color);
    }

    [Fact]
    public void Extract_CoordinatesAreRoundedToOneDecimal()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        foreach (var element in result.Elements)
        {
            Assert.Equal(Math.Round(element.X, 1, MidpointRounding.AwayFromZero), element.X);
            Assert.Equal(Math.Round(element.Y, 1, MidpointRounding.AwayFromZero), element.Y);
            Assert.Equal(Math.Round(element.W, 1, MidpointRounding.AwayFromZero), element.W);
            Assert.Equal(Math.Round(element.H, 1, MidpointRounding.AwayFromZero), element.H);
            Assert.Equal(Math.Round(element.Size, 1, MidpointRounding.AwayFromZero), element.Size);
        }
    }

    [Fact]
    public void Extract_ReturnsCorrectPageDimensions()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        // Letter size: 612 x 792
        Assert.Equal(612.0, result.Width);
        Assert.Equal(792.0, result.Height);
    }

    [Fact]
    public void Extract_InvalidGranularity_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 1, "paragraphs"));
        Assert.Equal("Granularity must be 'words' or 'letters'.", ex.Message);
    }

    [Fact]
    public void Extract_PageOutOfRange_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 99, "words"));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Extract_PageZero_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 0, "words"));
        Assert.Contains("Page number must be 1 or greater", ex.Message);
    }

    [Fact]
    public void Extract_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 1, "words"));
        Assert.Equal("The file could not be opened as a PDF.", ex.Message);
    }

    [Fact]
    public void Extract_LockedFile_ThrowsArgumentExceptionWithAccessMessage()
    {
        var source = TestPdfGenerator.GetTestDataPath("sample-with-metadata.pdf");
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
        File.Copy(source, tempPath);
        try
        {
            using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = Assert.Throws<ArgumentException>(() => _service.Extract(tempPath, 1, "words"));
            Assert.Equal($"The file could not be accessed: {tempPath}. It may be in use by another process.", ex.Message);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Extract_Serialization_ProducesExpectedJsonStructure()
    {
        var path = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // camelCase property names
        Assert.True(root.TryGetProperty("page", out _));
        Assert.True(root.TryGetProperty("width", out _));
        Assert.True(root.TryGetProperty("height", out _));
        Assert.True(root.TryGetProperty("elements", out var elements));

        var first = elements[0];
        Assert.True(first.TryGetProperty("text", out _));
        Assert.True(first.TryGetProperty("x", out _));
        Assert.True(first.TryGetProperty("y", out _));
        Assert.True(first.TryGetProperty("w", out _));
        Assert.True(first.TryGetProperty("h", out _));
        Assert.True(first.TryGetProperty("font", out _));
        Assert.True(first.TryGetProperty("size", out _));

        // Null fields should be omitted
        var helloElement = elements.EnumerateArray().First(
            e => e.GetProperty("text").GetString() == "Hello");
        Assert.False(helloElement.TryGetProperty("color", out _));
        Assert.False(helloElement.TryGetProperty("bold", out _));
        Assert.False(helloElement.TryGetProperty("italic", out _));
    }

    [Fact]
    public void Extract_LargePageWordGranularity_ResponseUnder30KB()
    {
        var path = TestPdfGenerator.CreateLargeTextTestPdf();
        var result = _service.Extract(path, 1, "words");

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(json);

        Assert.True(sizeInBytes <= 30 * 1024, $"Response size {sizeInBytes} bytes exceeds 30 KB limit.");
    }

    [Theory]
    [InlineData("Bold", true, null)]
    [InlineData("Italic", null, true)]
    [InlineData("BoldItalic", true, true)]
    [InlineData("Oblique", null, true)]
    [InlineData("Helvetica", null, null)]
    [InlineData("Arial-Bold", true, null)]
    [InlineData("TimesNewRoman-BoldItalic", true, true)]
    [InlineData("CourierOblique", null, true)]
    public void InferBoldItalic_FontNamePatterns(string fontName, bool? expectedBold, bool? expectedItalic)
    {
        var (bold, italic) = PageTextService.InferBoldItalic(fontName);
        Assert.Equal(expectedBold, bold);
        Assert.Equal(expectedItalic, italic);
    }

    #region ExtractToFile Tests

    [Fact]
    public void ExtractToFile_WritesToDiskAndReturnsSummary()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var summary = _service.ExtractToFile(pdfPath, 1, "words", outputFile);

            Assert.Equal(1, summary.Page);
            Assert.True(summary.Width > 0);
            Assert.True(summary.Height > 0);
            Assert.True(summary.ElementCount > 0);
            Assert.Equal(Path.GetFullPath(outputFile), summary.OutputFile);
            Assert.True(summary.SizeBytes > 0);
            Assert.True(File.Exists(outputFile));
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public void ExtractToFile_FileContainsValidCsvWithHeaderAndCorrectRowCount()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            var summary = _service.ExtractToFile(pdfPath, 1, "words", outputFile);
            var lines = File.ReadAllLines(outputFile);

            // First line is the header
            Assert.Equal("text,x,y,w,h,font,size,color,bold,italic", lines[0]);
            // Data rows = elementCount
            Assert.Equal(summary.ElementCount, lines.Length - 1);
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public void ExtractToFile_CsvEscaping_CommasAndQuotesEscapedPerRfc4180()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            _service.ExtractToFile(pdfPath, 1, "words", outputFile);
            var content = File.ReadAllText(outputFile);

            // Text with a comma should be quoted: "Hello, World" becomes """Hello, World""" or similar
            // The word "Hello," should be CSV-escaped since it contains a comma
            Assert.Contains("\"Hello,", content);

            // Text with quotes: Say "Hi" should be escaped as "Say ""Hi"""
            Assert.Contains("\"\"", content);
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public void ExtractToFile_BooleanFields_TrueWhenSetEmptyOtherwise()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            _service.ExtractToFile(pdfPath, 1, "words", outputFile);
            var lines = File.ReadAllLines(outputFile);

            // Find a bold word line (Bold Text) - should have "true" in bold column (index 8)
            var boldLine = lines.Skip(1).First(l => l.StartsWith("Bold,"));
            var boldFields = ParseCsvLine(boldLine);
            Assert.Equal("true", boldFields[8]); // bold
            Assert.Equal("", boldFields[9]); // italic

            // Find a regular word (Hello) - bold and italic should be empty
            var helloLine = lines.Skip(1).First(l => l.StartsWith("Hello,"));
            var helloFields = ParseCsvLine(helloLine);
            Assert.Equal("", helloFields[8]); // bold
            Assert.Equal("", helloFields[9]); // italic
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public void ExtractToFile_OverwritesExistingFile()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        try
        {
            File.WriteAllText(outputFile, "old content");
            var summary = _service.ExtractToFile(pdfPath, 1, "words", outputFile);

            var fileContent = File.ReadAllText(outputFile);
            Assert.DoesNotContain("old content", fileContent);
            Assert.StartsWith("text,x,y,w,h,font,size,color,bold,italic", fileContent);
            Assert.True(summary.SizeBytes > 0);
        }
        finally
        {
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public void ExtractToFile_RelativePath_ThrowsArgumentException()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(
            () => _service.ExtractToFile(pdfPath, 1, "words", "relative/output.json"));
        Assert.Contains("absolute path", ex.Message);
    }

    [Fact]
    public void ExtractToFile_PathTraversal_ThrowsArgumentException()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();

        var ex = Assert.Throws<ArgumentException>(
            () => _service.ExtractToFile(pdfPath, 1, "words", "C:\\temp\\..\\output.json"));
        Assert.Contains("path traversal", ex.Message);
    }

    [Fact]
    public void ExtractToFile_DirectoryMissing_ThrowsArgumentException()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var outputFile = Path.Combine("C:\\", $"nonexistent_{Guid.NewGuid()}", "output.json");

        var ex = Assert.Throws<ArgumentException>(
            () => _service.ExtractToFile(pdfPath, 1, "words", outputFile));
        Assert.Contains("parent directory", ex.Message);
    }

    [Fact]
    public void Extract_ContinuesToReturnFullDto_NoFileWritten()
    {
        var pdfPath = TestPdfGenerator.CreateTextTestPdf();
        var result = _service.Extract(pdfPath, 1, "words");

        Assert.True(result.Elements.Count > 0);
        Assert.Equal(1, result.Page);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Parses a single CSV line respecting RFC 4180 quoting rules.
    /// </summary>
    private static string[] ParseCsvLine(string line)
    {
        List<string> fields = [];
        int i = 0;
        while (i <= line.Length)
        {
            if (i == line.Length)
            {
                fields.Add("");
                break;
            }
            if (line[i] == '"')
            {
                // Quoted field
                var sb = new System.Text.StringBuilder();
                i++; // skip opening quote
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++; // skip closing quote
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++; // skip comma
            }
            else
            {
                // Unquoted field
                int start = i;
                while (i < line.Length && line[i] != ',') i++;
                fields.Add(line[start..i]);
                if (i < line.Length) i++; // skip comma
            }
        }
        return [.. fields];
    }

    #endregion
}
