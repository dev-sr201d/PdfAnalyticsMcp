using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class PageImagesServiceTests
{
    private readonly PageImagesService _service = new(
        new InputValidationService(),
        NullLogger<PageImagesService>.Instance);

    [Fact]
    public void Extract_PageWithImage_ReturnsCorrectMetadata()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        Assert.Equal(1, result.Page);
        Assert.Single(result.Images);

        var image = result.Images[0];
        // PdfRectangle(100, 500, 300, 650) => x=100, y=500, w=200, h=150
        Assert.Equal(100.0, image.X);
        Assert.Equal(500.0, image.Y);
        Assert.Equal(200.0, image.W);
        Assert.Equal(150.0, image.H);
        Assert.Equal(2, image.PixelWidth);
        Assert.Equal(2, image.PixelHeight);
        Assert.Equal(8, image.BitsPerComponent);
    }

    [Fact]
    public void Extract_MultipleImages_ReturnsAllImages()
    {
        var path = TestPdfGenerator.CreateMultiImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        Assert.Equal(2, result.Images.Count);

        // Image 1: PdfRectangle(50, 600, 150, 680) => x=50, y=600, w=100, h=80
        var img1 = result.Images[0];
        Assert.Equal(50.0, img1.X);
        Assert.Equal(600.0, img1.Y);
        Assert.Equal(100.0, img1.W);
        Assert.Equal(80.0, img1.H);

        // Image 2: PdfRectangle(200, 400, 350, 520) => x=200, y=400, w=150, h=120
        var img2 = result.Images[1];
        Assert.Equal(200.0, img2.X);
        Assert.Equal(400.0, img2.Y);
        Assert.Equal(150.0, img2.W);
        Assert.Equal(120.0, img2.H);
    }

    [Fact]
    public void Extract_IncludeDataFalse_DataIsNull()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        Assert.Single(result.Images);
        Assert.Null(result.Images[0].Data);
    }

    [Fact]
    public void Extract_IncludeDataTrue_ReturnsBase64PngData()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: true);

        Assert.Single(result.Images);
        var image = result.Images[0];
        Assert.NotNull(image.Data);

        // Verify it's plain base64 (no data URI prefix)
        Assert.DoesNotContain("data:", image.Data);

        // Verify it can be decoded back to valid bytes
        byte[] decoded = Convert.FromBase64String(image.Data);
        Assert.True(decoded.Length > 0);

        // Verify it starts with PNG signature
        Assert.Equal(0x89, decoded[0]);
        Assert.Equal(0x50, decoded[1]); // 'P'
        Assert.Equal(0x4E, decoded[2]); // 'N'
        Assert.Equal(0x47, decoded[3]); // 'G'
    }

    [Fact]
    public void Extract_EmptyPage_ReturnsEmptyImagesList()
    {
        var path = TestPdfGenerator.CreateBlankTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        Assert.NotNull(result.Images);
        Assert.Empty(result.Images);
    }

    [Fact]
    public void Extract_CoordinatesRoundedToOneDecimal()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        var image = result.Images[0];
        // All coordinates should have at most 1 decimal place
        Assert.Equal(Math.Round(image.X, 1), image.X);
        Assert.Equal(Math.Round(image.Y, 1), image.Y);
        Assert.Equal(Math.Round(image.W, 1), image.W);
        Assert.Equal(Math.Round(image.H, 1), image.H);
    }

    [Fact]
    public void Extract_ReturnsCorrectPageDimensions()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        // US Letter: 612 x 792 points
        Assert.Equal(612.0, result.Width);
        Assert.Equal(792.0, result.Height);
    }

    [Fact]
    public void Extract_InvalidPageNumber_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();

        Assert.Throws<ArgumentException>(() => _service.Extract(path, 0, includeData: false));
        Assert.Throws<ArgumentException>(() => _service.Extract(path, -1, includeData: false));
        Assert.Throws<ArgumentException>(() => _service.Extract(path, 99, includeData: false));
    }

    [Fact]
    public void Extract_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");
        Assert.Throws<ArgumentException>(() => _service.Extract(path, 1, includeData: false));
    }

    [Fact]
    public void Serialization_DataNullOmittedFromJson()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);

        // Verify camelCase
        Assert.True(doc.RootElement.TryGetProperty("page", out _));
        Assert.True(doc.RootElement.TryGetProperty("width", out _));
        Assert.True(doc.RootElement.TryGetProperty("height", out _));
        Assert.True(doc.RootElement.TryGetProperty("images", out _));

        // Verify data field is absent (null omitted)
        var imageElement = doc.RootElement.GetProperty("images")[0];
        Assert.False(imageElement.TryGetProperty("data", out _), "data field should be omitted when null");

        // Verify other image fields are present
        Assert.True(imageElement.TryGetProperty("x", out _));
        Assert.True(imageElement.TryGetProperty("y", out _));
        Assert.True(imageElement.TryGetProperty("w", out _));
        Assert.True(imageElement.TryGetProperty("h", out _));
        Assert.True(imageElement.TryGetProperty("pixelWidth", out _));
        Assert.True(imageElement.TryGetProperty("pixelHeight", out _));
        Assert.True(imageElement.TryGetProperty("bitsPerComponent", out _));
    }

    [Fact]
    public void Serialization_DataPresentWhenIncluded()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: true);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);

        var imageElement = doc.RootElement.GetProperty("images")[0];
        Assert.True(imageElement.TryGetProperty("data", out var dataValue));
        Assert.Equal(JsonValueKind.String, dataValue.ValueKind);
        Assert.False(string.IsNullOrEmpty(dataValue.GetString()));
    }

    [Fact]
    public void Serialization_CompactJsonNoPrettyPrint()
    {
        var path = TestPdfGenerator.CreateImageTestPdf();
        var result = _service.Extract(path, 1, includeData: false);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);

        // Compact JSON should not contain newlines
        Assert.DoesNotContain("\n", json);
    }
}
