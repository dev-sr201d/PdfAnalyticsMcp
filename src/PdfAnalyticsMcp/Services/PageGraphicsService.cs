using PdfAnalyticsMcp.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;

namespace PdfAnalyticsMcp.Services;

public class PageGraphicsService(IInputValidationService validationService) : IPageGraphicsService
{
    private const double CoordinateTolerance = 0.01;

    public PageGraphicsDto Extract(string pdfPath, int page)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfPath);
        }
        catch (Exception)
        {
            throw new ArgumentException("The file could not be opened as a PDF.");
        }

        using (document)
        {
            validationService.ValidatePageNumber(page, document.NumberOfPages);

            var pdfPage = document.GetPage(page);

            var rectangles = new List<RectangleDto>();
            var lines = new List<LineDto>();
            var paths = new List<PathDto>();

            try
            {
                foreach (var path in pdfPage.Paths)
                {
                    if (path.IsClipping)
                        continue;

                    if (!path.IsFilled && !path.IsStroked)
                        continue;

                    string? fillColor = path.IsFilled ? (ExtractColor(path.FillColor) ?? "#000000") : null;
                    string? strokeColor = path.IsStroked ? (ExtractColor(path.StrokeColor) ?? "#000000") : null;
                    double? strokeWidth = path.IsStroked ? FormatUtils.RoundCoordinate(path.LineWidth) : null;
                    string? dashPattern = path.IsStroked ? FormatDashPattern(path.LineDashPattern) : null;

                    if (TryClassifyRectangle(path, out double rx, out double ry, out double rw, out double rh))
                    {
                        rectangles.Add(new RectangleDto(
                            FormatUtils.RoundCoordinate(rx),
                            FormatUtils.RoundCoordinate(ry),
                            FormatUtils.RoundCoordinate(rw),
                            FormatUtils.RoundCoordinate(rh),
                            fillColor,
                            strokeColor,
                            strokeWidth));
                    }
                    else if (TryClassifyLine(path, out double lx1, out double ly1, out double lx2, out double ly2))
                    {
                        lines.Add(new LineDto(
                            FormatUtils.RoundCoordinate(lx1),
                            FormatUtils.RoundCoordinate(ly1),
                            FormatUtils.RoundCoordinate(lx2),
                            FormatUtils.RoundCoordinate(ly2),
                            strokeColor,
                            strokeWidth,
                            dashPattern));
                    }
                    else
                    {
                        var bounds = path.GetBoundingRectangle();
                        if (bounds.HasValue)
                        {
                            paths.Add(new PathDto(
                                FormatUtils.RoundCoordinate(bounds.Value.Left),
                                FormatUtils.RoundCoordinate(bounds.Value.Bottom),
                                FormatUtils.RoundCoordinate(bounds.Value.Width),
                                FormatUtils.RoundCoordinate(bounds.Value.Height),
                                fillColor,
                                strokeColor,
                                CountVertices(path)));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"An error occurred extracting graphics from page {page}.");
            }

            return new PageGraphicsDto(
                page,
                FormatUtils.RoundCoordinate(pdfPage.Width),
                FormatUtils.RoundCoordinate(pdfPage.Height),
                rectangles,
                lines,
                paths);
        }
    }

    internal static string? ExtractColor(IColor? color)
    {
        if (color is null)
            return null;

        try
        {
            var (r, g, b) = color.ToRGBValues();
            byte R = (byte)Math.Round(r * 255);
            byte G = (byte)Math.Round(g * 255);
            byte B = (byte)Math.Round(b * 255);
            return FormatUtils.FormatColor(R, G, B);
        }
        catch (InvalidOperationException)
        {
            // PatternColor types throw InvalidOperationException from ToRGBValues()
            return null;
        }
    }

    internal static string? FormatDashPattern(LineDashPattern? dashPattern)
    {
        if (dashPattern is null || dashPattern.Value.Array.Count == 0)
            return null;

        var arrayStr = string.Join(" ", dashPattern.Value.Array.Select(v => v.ToString("G")));
        return $"[{arrayStr}] {dashPattern.Value.Phase}";
    }

    internal static bool TryClassifyRectangle(PdfPath path, out double x, out double y, out double w, out double h)
    {
        x = y = w = h = 0;

        if (path.Count != 1)
            return false;

        var subpath = path[0];
        var commands = subpath.Commands;

        if (commands.Any(c => c is PdfSubpath.CubicBezierCurve or PdfSubpath.QuadraticBezierCurve))
            return false;

        // Pattern 1: 1 Move + 3 Line + 1 Close (from re operation)
        if (commands.Count == 5
            && commands[0] is PdfSubpath.Move
            && commands[1] is PdfSubpath.Line
            && commands[2] is PdfSubpath.Line
            && commands[3] is PdfSubpath.Line
            && commands[4] is PdfSubpath.Close)
        {
            return CheckAxisAligned4Points(commands, 3, out x, out y, out w, out h);
        }

        // Pattern 2: 1 Move + 4 Line (last point coincides with first)
        if (commands.Count == 5
            && commands[0] is PdfSubpath.Move move
            && commands[1] is PdfSubpath.Line
            && commands[2] is PdfSubpath.Line
            && commands[3] is PdfSubpath.Line
            && commands[4] is PdfSubpath.Line lastLine)
        {
            if (Math.Abs(lastLine.To.X - move.Location.X) > CoordinateTolerance ||
                Math.Abs(lastLine.To.Y - move.Location.Y) > CoordinateTolerance)
                return false;

            return CheckAxisAligned4Points(commands, 4, out x, out y, out w, out h);
        }

        return false;
    }

    private static bool CheckAxisAligned4Points(IReadOnlyList<PdfSubpath.IPathCommand> commands,
        int lineCount, out double x, out double y, out double w, out double h)
    {
        x = y = w = h = 0;

        var points = new List<PdfPoint>(4);

        if (commands[0] is PdfSubpath.Move m)
            points.Add(m.Location);
        else
            return false;

        for (int i = 1; i <= lineCount; i++)
        {
            if (commands[i] is PdfSubpath.Line l)
                points.Add(l.To);
            else
                return false;
        }

        // Check all 4 edges are axis-aligned
        for (int i = 0; i < 4; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % 4];
            bool isHorizontal = Math.Abs(current.Y - next.Y) < CoordinateTolerance;
            bool isVertical = Math.Abs(current.X - next.X) < CoordinateTolerance;
            if (!isHorizontal && !isVertical)
                return false;
        }

        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxX = points.Max(p => p.X);
        double maxY = points.Max(p => p.Y);

        x = minX;
        y = minY;
        w = maxX - minX;
        h = maxY - minY;
        return true;
    }

    internal static bool TryClassifyLine(PdfPath path, out double x1, out double y1, out double x2, out double y2)
    {
        x1 = y1 = x2 = y2 = 0;

        if (path.Count != 1)
            return false;

        var commands = path[0].Commands;

        if (commands.Count != 2)
            return false;

        if (commands[0] is not PdfSubpath.Move move || commands[1] is not PdfSubpath.Line line)
            return false;

        x1 = move.Location.X;
        y1 = move.Location.Y;
        x2 = line.To.X;
        y2 = line.To.Y;
        return true;
    }

    internal static int CountVertices(PdfPath path)
    {
        int count = 0;
        foreach (var subpath in path)
        {
            foreach (var command in subpath.Commands)
            {
                count += command switch
                {
                    PdfSubpath.Move => 1,
                    PdfSubpath.Line => 1,
                    PdfSubpath.CubicBezierCurve => 1,
                    PdfSubpath.QuadraticBezierCurve => 1,
                    PdfSubpath.Close => 1,
                    _ => 0
                };
            }
        }
        return count;
    }
}
