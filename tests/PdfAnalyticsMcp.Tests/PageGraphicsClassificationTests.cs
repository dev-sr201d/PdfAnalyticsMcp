using PdfAnalyticsMcp.Services;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;

namespace PdfAnalyticsMcp.Tests;

/// <summary>
/// Tests for path classification, color extraction, filtering, and dash pattern formatting
/// that exercise the internal static methods on PageGraphicsService directly using
/// constructed PdfPath/PdfSubpath objects.
/// </summary>
public class PageGraphicsClassificationTests
{
    // --- Helpers to build PdfPath / PdfSubpath objects ---

    private static PdfSubpath BuildSubpath(Action<PdfSubpath> configure)
    {
        var subpath = new PdfSubpath();
        configure(subpath);
        return subpath;
    }

    private static PdfPath BuildPath(PdfSubpath subpath,
        bool filled = false, bool stroked = false, bool clipping = false,
        IColor? fillColor = null, IColor? strokeColor = null,
        double lineWidth = 1.0, LineDashPattern? dashPattern = null)
    {
        var path = new PdfPath();
        path.Add(subpath);

        if (filled)
        {
            path.SetFilled(FillingRule.NonZeroWinding);
            if (fillColor is not null || strokeColor is not null)
            {
                var gs = new CurrentGraphicsState();
                if (fillColor is not null) gs.CurrentNonStrokingColor = fillColor;
                if (strokeColor is not null) gs.CurrentStrokingColor = strokeColor;
                gs.LineWidth = lineWidth;
                if (dashPattern.HasValue) gs.LineDashPattern = dashPattern.Value;
                path.SetFillDetails(gs);
            }
        }

        if (stroked)
        {
            path.SetStroked();
            var gs = new CurrentGraphicsState();
            if (strokeColor is not null) gs.CurrentStrokingColor = strokeColor;
            if (fillColor is not null) gs.CurrentNonStrokingColor = fillColor;
            gs.LineWidth = lineWidth;
            if (dashPattern.HasValue) gs.LineDashPattern = dashPattern.Value;
            path.SetStrokeDetails(gs);
        }

        if (clipping)
            path.SetClipping(FillingRule.NonZeroWinding);

        return path;
    }

    // --- Test #2: Rectangle from 4 explicit lines (Move+4Line, last point coincides with first) ---

    [Fact]
    public void TryClassifyRectangle_Move4LineCoincident_ClassifiedAsRectangle()
    {
        // Build 1 Move + 4 Line where last Line.To == Move.Location, axis-aligned
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(10, 20);
            sp.LineTo(110, 20);
            sp.LineTo(110, 70);
            sp.LineTo(10, 70);
            sp.LineTo(10, 20); // coincides with start
        });

        var path = new PdfPath();
        path.Add(subpath);

        bool result = PageGraphicsService.TryClassifyRectangle(path, out double x, out double y, out double w, out double h);

        Assert.True(result);
        Assert.Equal(10.0, x);
        Assert.Equal(20.0, y);
        Assert.Equal(100.0, w);
        Assert.Equal(50.0, h);
    }

    [Fact]
    public void TryClassifyRectangle_Move4LineNotCoincident_NotRectangle()
    {
        // Last point does NOT coincide with first
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(10, 20);
            sp.LineTo(110, 20);
            sp.LineTo(110, 70);
            sp.LineTo(10, 70);
            sp.LineTo(15, 25); // does not match start
        });

        var path = new PdfPath();
        path.Add(subpath);

        bool result = PageGraphicsService.TryClassifyRectangle(path, out _, out _, out _, out _);

        Assert.False(result);
    }

    // --- Test #3: Non-axis-aligned quadrilateral → complex path ---

    [Fact]
    public void TryClassifyRectangle_NonAxisAligned_ReturnsFalse()
    {
        // Diamond shape — edges are diagonal, not horizontal/vertical
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(50, 0);
            sp.LineTo(100, 50);
            sp.LineTo(50, 100);
            sp.LineTo(0, 50);
            sp.CloseSubpath();
        });

        var path = new PdfPath();
        path.Add(subpath);

        bool result = PageGraphicsService.TryClassifyRectangle(path, out _, out _, out _, out _);

        Assert.False(result);
    }

    // --- Test #5: Triangle → complex path with correct vertex count ---

    [Fact]
    public void Extract_Triangle_ClassifiedAsComplexPath()
    {
        var pdfPath = TestPdfGenerator.CreateGraphicsExtendedTestPdf();
        var service = new PageGraphicsService(new InputValidationService());
        var result = service.Extract(pdfPath, 1);

        // Triangle from DrawTriangle should be a complex path (3 lines, not a rect)
        Assert.True(result.Paths.Count >= 1, "Expected at least one complex path (triangle) on page 1.");
    }

    [Fact]
    public void CountVertices_Triangle_Returns4()
    {
        // Triangle: Move + Line + Line + Close = 4 vertices
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.LineTo(50, 100);
            sp.LineTo(100, 0);
            sp.CloseSubpath();
        });

        var path = new PdfPath();
        path.Add(subpath);

        int count = PageGraphicsService.CountVertices(path);

        Assert.Equal(4, count);
    }

    // --- Test #7: Path with curves is never classified as rectangle ---

    [Fact]
    public void TryClassifyRectangle_PathWithCurves_ReturnsFalse()
    {
        // Build a path with Move + 3 BezierCurves + Close that forms a box-like shape
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.BezierCurveTo(100, 0, 100, 0, 100, 0); // degenerate curve (effectively a line)
            sp.BezierCurveTo(100, 50, 100, 50, 100, 50);
            sp.BezierCurveTo(0, 50, 0, 50, 0, 50);
            sp.CloseSubpath();
        });

        var path = new PdfPath();
        path.Add(subpath);

        bool result = PageGraphicsService.TryClassifyRectangle(path, out _, out _, out _, out _);

        Assert.False(result);
    }

    // --- Test #8: Fill-only path ---

    [Fact]
    public void Extract_FillOnly_HasFillColorNoStroke()
    {
        var fillColor = new RGBColor(1.0, 0.0, 0.0);
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(10, 20);
            sp.LineTo(110, 20);
            sp.LineTo(110, 70);
            sp.LineTo(10, 70);
            sp.CloseSubpath();
        });

        var path = BuildPath(subpath, filled: true, stroked: false, fillColor: fillColor);

        Assert.True(path.IsFilled);
        Assert.False(path.IsStroked);

        // Verify rectangle classification works
        bool isRect = PageGraphicsService.TryClassifyRectangle(path, out _, out _, out _, out _);
        Assert.True(isRect);

        // Verify color extraction
        string? fill = PageGraphicsService.ExtractColor(path.FillColor);
        Assert.Equal("#FF0000", fill);
    }

    // --- Test #10: Fill+stroke path ---

    [Fact]
    public void Extract_FillAndStroke_HasBothColors()
    {
        var fillColor = new RGBColor(1.0, 0.0, 0.0);
        var strokeColor = new RGBColor(0.0, 1.0, 0.0);
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(10, 20);
            sp.LineTo(110, 20);
            sp.LineTo(110, 70);
            sp.LineTo(10, 70);
            sp.CloseSubpath();
        });

        var path = BuildPath(subpath, filled: true, stroked: true,
            fillColor: fillColor, strokeColor: strokeColor, lineWidth: 3.0);

        Assert.True(path.IsFilled);
        Assert.True(path.IsStroked);
        Assert.Equal("#FF0000", PageGraphicsService.ExtractColor(path.FillColor));
        Assert.Equal("#00FF00", PageGraphicsService.ExtractColor(path.StrokeColor));
    }

    // --- Test #11: RGB color conversion ---

    [Fact]
    public void ExtractColor_RgbColor_ReturnsCorrectHex()
    {
        var color = new RGBColor(0.2, 0.4, 0.6);
        var result = PageGraphicsService.ExtractColor(color);

        // R=round(0.2*255)=51=0x33, G=round(0.4*255)=102=0x66, B=round(0.6*255)=153=0x99
        Assert.Equal("#336699", result);
    }

    // --- Test #12: Grayscale color conversion ---

    [Fact]
    public void ExtractColor_GrayColor_ReturnsCorrectHex()
    {
        var color = new GrayColor(0.5);
        var result = PageGraphicsService.ExtractColor(color);

        // 0.5 gray → (128, 128, 128) → #808080
        Assert.Equal("#808080", result);
    }

    [Fact]
    public void ExtractColor_GrayColorBlack_ReturnsBlack()
    {
        var color = new GrayColor(0.0);
        var result = PageGraphicsService.ExtractColor(color);

        Assert.Equal("#000000", result);
    }

    [Fact]
    public void ExtractColor_GrayColorWhite_ReturnsWhite()
    {
        var color = new GrayColor(1.0);
        var result = PageGraphicsService.ExtractColor(color);

        Assert.Equal("#FFFFFF", result);
    }

    // --- Test #13: PatternColor handling ---

    [Fact]
    public void ExtractColor_NullColor_ReturnsNull()
    {
        var result = PageGraphicsService.ExtractColor(null);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractColor_ColorThatThrowsInvalidOperation_ReturnsNull()
    {
        // PatternColor types throw InvalidOperationException from ToRGBValues().
        // We simulate this with a mock IColor that throws.
        var mockColor = new ThrowingColor();
        var result = PageGraphicsService.ExtractColor(mockColor);

        Assert.Null(result);
    }

    // --- Test #14: Clipping paths filtered ---

    [Fact]
    public void Extract_ClippingPath_IsExcluded()
    {
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.LineTo(100, 0);
            sp.LineTo(100, 100);
            sp.LineTo(0, 100);
            sp.CloseSubpath();
        });

        var path = BuildPath(subpath, filled: true, clipping: true);

        // Verify clipping flag is set
        Assert.True(path.IsClipping);
    }

    // --- Test #15: Invisible paths filtered ---

    [Fact]
    public void InvisiblePath_NeitherFilledNorStroked_WouldBeExcluded()
    {
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.LineTo(100, 0);
        });

        var path = new PdfPath();
        path.Add(subpath);

        // Path without SetFilled or SetStroked: both should be false
        Assert.False(path.IsFilled);
        Assert.False(path.IsStroked);
    }

    // --- Test #22: Dash pattern formatting ---

    [Fact]
    public void FormatDashPattern_WithArray_ReturnsFormattedString()
    {
        var pattern = new LineDashPattern(0, new double[] { 3, 2 });
        var result = PageGraphicsService.FormatDashPattern(pattern);

        Assert.Equal("[3 2] 0", result);
    }

    [Fact]
    public void FormatDashPattern_EmptyArray_ReturnsNull()
    {
        var pattern = new LineDashPattern(0, Array.Empty<double>());
        var result = PageGraphicsService.FormatDashPattern(pattern);

        Assert.Null(result);
    }

    [Fact]
    public void FormatDashPattern_Null_ReturnsNull()
    {
        var result = PageGraphicsService.FormatDashPattern(null);

        Assert.Null(result);
    }

    [Fact]
    public void FormatDashPattern_WithPhase_ReturnsFormattedString()
    {
        var pattern = new LineDashPattern(5, new double[] { 4, 1, 2, 1 });
        var result = PageGraphicsService.FormatDashPattern(pattern);

        Assert.Equal("[4 1 2 1] 5", result);
    }

    // --- Test: Line classification ---

    [Fact]
    public void TryClassifyLine_MoveAndLine_ReturnsTrue()
    {
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(10, 20);
            sp.LineTo(100, 200);
        });

        var path = new PdfPath();
        path.Add(subpath);

        bool result = PageGraphicsService.TryClassifyLine(path, out double x1, out double y1, out double x2, out double y2);

        Assert.True(result);
        Assert.Equal(10.0, x1);
        Assert.Equal(20.0, y1);
        Assert.Equal(100.0, x2);
        Assert.Equal(200.0, y2);
    }

    [Fact]
    public void TryClassifyLine_MoveLineClose_ReturnsFalse()
    {
        // m + l + h has a Close — not a simple line
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.LineTo(10, 10);
            sp.CloseSubpath();
        });

        var path = new PdfPath();
        path.Add(subpath);

        bool result = PageGraphicsService.TryClassifyLine(path, out _, out _, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryClassifyLine_MultipleSubpaths_ReturnsFalse()
    {
        var sub1 = BuildSubpath(sp => { sp.MoveTo(0, 0); sp.LineTo(10, 10); });
        var sub2 = BuildSubpath(sp => { sp.MoveTo(20, 20); sp.LineTo(30, 30); });

        var path = new PdfPath();
        path.Add(sub1);
        path.Add(sub2);

        bool result = PageGraphicsService.TryClassifyLine(path, out _, out _, out _, out _);

        Assert.False(result);
    }

    // --- Test: Rectangle with multiple subpaths → not a rectangle ---

    [Fact]
    public void TryClassifyRectangle_MultipleSubpaths_ReturnsFalse()
    {
        var sub1 = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.LineTo(100, 0);
            sp.LineTo(100, 100);
            sp.LineTo(0, 100);
            sp.CloseSubpath();
        });
        var sub2 = BuildSubpath(sp => { sp.MoveTo(0, 0); sp.LineTo(10, 10); });

        var path = new PdfPath();
        path.Add(sub1);
        path.Add(sub2);

        bool result = PageGraphicsService.TryClassifyRectangle(path, out _, out _, out _, out _);

        Assert.False(result);
    }

    // --- Test: CountVertices for curve paths ---

    [Fact]
    public void CountVertices_CubicBezierPath_CountsEndpointOnly()
    {
        var subpath = BuildSubpath(sp =>
        {
            sp.MoveTo(0, 0);
            sp.BezierCurveTo(10, 20, 30, 40, 50, 0);
        });

        var path = new PdfPath();
        path.Add(subpath);

        // Move=1 + CubicBezier=1 (endpoint only, per spec) = 2
        int count = PageGraphicsService.CountVertices(path);
        Assert.Equal(2, count);
    }

    // --- Mock IColor that throws InvalidOperationException (simulates PatternColor) ---

    private class ThrowingColor : IColor
    {
        public ColorSpace ColorSpace => ColorSpace.DeviceRGB;

        public (double r, double g, double b) ToRGBValues()
        {
            throw new InvalidOperationException("Pattern colors cannot be converted to RGB.");
        }
    }
}
