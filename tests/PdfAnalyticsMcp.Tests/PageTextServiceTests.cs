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
        Assert.Contains("Invalid granularity", ex.Message);
        Assert.Contains("words", ex.Message);
        Assert.Contains("letters", ex.Message);
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
}
