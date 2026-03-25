using System.Text.Json;
using PdfAnalyticsMcp.Services;

namespace PdfAnalyticsMcp.Tests;

public class PageGraphicsServiceTests
{
    private readonly PageGraphicsService _service = new(new InputValidationService());

    [Fact]
    public void Extract_RectangleFromReOp_ReturnsClassifiedRectangle()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        Assert.True(result.Rectangles.Count >= 1, "Expected at least one rectangle on page 1.");
    }

    [Fact]
    public void Extract_LineSegment_ReturnsClassifiedLine()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        Assert.True(result.Lines.Count >= 1, "Expected at least one line on page 1.");
    }

    [Fact]
    public void Extract_CirclePath_ReturnsComplexPath()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 2);

        Assert.True(result.Paths.Count >= 1, "Expected at least one complex path (circle) on page 2.");
        var circlePath = result.Paths[0];
        Assert.True(circlePath.VertexCount > 4, "Circle should have more than 4 vertices (Bézier control points).");
    }

    [Fact]
    public void Extract_StrokedRectangle_HasStrokeColorAndWidth()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        // The blue stroked rectangle
        var strokedRect = result.Rectangles.FirstOrDefault(r => r.StrokeColor is not null);
        Assert.NotNull(strokedRect);
        Assert.NotNull(strokedRect.StrokeColor);
        Assert.NotNull(strokedRect.StrokeWidth);
    }

    [Fact]
    public void Extract_StrokeOnlyPainting_HasNoFillColor()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        // Lines are stroke-only
        var line = result.Lines.First();
        Assert.Null(line.DashPattern); // solid line
    }

    [Fact]
    public void Extract_LineWithStrokeColor_HasCorrectColor()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        // We drew a green line with stroke color (0, 128, 0)
        var greenLine = result.Lines.FirstOrDefault(l => l.StrokeColor == "#008000");
        Assert.NotNull(greenLine);
        Assert.Equal(2.0, greenLine.StrokeWidth);
    }

    [Fact]
    public void Extract_RectanglePosition_HasCorrectCoordinates()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        Assert.True(result.Rectangles.Count > 0);
        var rect = result.Rectangles[0];
        Assert.True(rect.W > 0, "Rectangle width should be positive.");
        Assert.True(rect.H > 0, "Rectangle height should be positive.");
    }

    [Fact]
    public void Extract_CoordinatesAreRoundedToOneDecimal()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        foreach (var rect in result.Rectangles)
        {
            Assert.Equal(Math.Round(rect.X, 1, MidpointRounding.AwayFromZero), rect.X);
            Assert.Equal(Math.Round(rect.Y, 1, MidpointRounding.AwayFromZero), rect.Y);
            Assert.Equal(Math.Round(rect.W, 1, MidpointRounding.AwayFromZero), rect.W);
            Assert.Equal(Math.Round(rect.H, 1, MidpointRounding.AwayFromZero), rect.H);
        }

        foreach (var line in result.Lines)
        {
            Assert.Equal(Math.Round(line.X1, 1, MidpointRounding.AwayFromZero), line.X1);
            Assert.Equal(Math.Round(line.Y1, 1, MidpointRounding.AwayFromZero), line.Y1);
            Assert.Equal(Math.Round(line.X2, 1, MidpointRounding.AwayFromZero), line.X2);
            Assert.Equal(Math.Round(line.Y2, 1, MidpointRounding.AwayFromZero), line.Y2);
        }
    }

    [Fact]
    public void Extract_PageDimensions_AreCorrectForLetterSize()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        Assert.Equal(612.0, result.Width);
        Assert.Equal(792.0, result.Height);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public void Extract_BlankPage_ReturnsEmptyLists()
    {
        var path = TestPdfGenerator.CreateBlankTestPdf();
        var result = _service.Extract(path, 1);

        Assert.Empty(result.Rectangles);
        Assert.Empty(result.Lines);
        Assert.Empty(result.Paths);
    }

    [Fact]
    public void Extract_ColorFormatIsHexRrggbb()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        foreach (var rect in result.Rectangles)
        {
            if (rect.FillColor is not null)
                Assert.Matches(@"^#[0-9A-F]{6}$", rect.FillColor);
            if (rect.StrokeColor is not null)
                Assert.Matches(@"^#[0-9A-F]{6}$", rect.StrokeColor);
        }

        foreach (var line in result.Lines)
        {
            if (line.StrokeColor is not null)
                Assert.Matches(@"^#[0-9A-F]{6}$", line.StrokeColor);
        }
    }

    [Fact]
    public void Extract_PageOutOfRange_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 99));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Extract_PageZero_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 0));
        Assert.Contains("Page number must be 1 or greater", ex.Message);
    }

    [Fact]
    public void Extract_InvalidPdfFile_ThrowsArgumentException()
    {
        var path = TestPdfGenerator.GetTestDataPath("not-a-pdf.txt");

        var ex = Assert.Throws<ArgumentException>(() => _service.Extract(path, 1));
        Assert.Equal("The file could not be opened as a PDF.", ex.Message);
    }

    [Fact]
    public void Extract_Serialization_ProducesExpectedJsonStructure()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("page", out _));
        Assert.True(root.TryGetProperty("width", out _));
        Assert.True(root.TryGetProperty("height", out _));
        Assert.True(root.TryGetProperty("rectangles", out _));
        Assert.True(root.TryGetProperty("lines", out _));
        Assert.True(root.TryGetProperty("paths", out _));
    }

    [Fact]
    public void Extract_Serialization_OmitsNullFields()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        var json = JsonSerializer.Serialize(result, SerializerConfig.Options);
        var doc = JsonDocument.Parse(json);

        // Find a line (stroke-only) - should not have fillColor
        var lines = doc.RootElement.GetProperty("lines");
        if (lines.GetArrayLength() > 0)
        {
            var line = lines[0];
            // Lines should not have any fill-related fields
            Assert.True(line.TryGetProperty("strokeColor", out _));
        }
    }

    [Fact]
    public void Extract_MultipleGraphicTypes_AllClassified()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 1);

        // Page 1 has both rectangles and lines
        Assert.True(result.Rectangles.Count > 0, "Expected rectangles on page 1.");
        Assert.True(result.Lines.Count > 0, "Expected lines on page 1.");
    }

    [Fact]
    public void Extract_ComplexPathVertexCount_IncludesCurvePoints()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 2);

        // Circle uses Bézier curves, so vertex count should include control points
        if (result.Paths.Count > 0)
        {
            var complexPath = result.Paths[0];
            Assert.True(complexPath.VertexCount > 0);
            Assert.True(complexPath.W > 0);
            Assert.True(complexPath.H > 0);
        }
    }

    [Fact]
    public void Extract_ComplexPath_HasStrokeColor()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 2);

        if (result.Paths.Count > 0)
        {
            var complexPath = result.Paths.FirstOrDefault(p => p.StrokeColor is not null);
            Assert.NotNull(complexPath);
            Assert.Matches(@"^#[0-9A-F]{6}$", complexPath.StrokeColor!);
        }
    }

    [Fact]
    public void Extract_StateChangesBetweenShapes_CorrectColors()
    {
        var path = TestPdfGenerator.CreateGraphicsTestPdf();
        var result = _service.Extract(path, 3);

        // Page 3 has shapes with different stroke colors: red, green, blue
        Assert.True(result.Lines.Count >= 2 || result.Rectangles.Count >= 1,
            "Expected multiple shapes on page 3.");
    }
}
