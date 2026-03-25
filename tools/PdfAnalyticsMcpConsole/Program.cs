using System.Text.Json;
using PdfAnalyticsMcp.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Geometry;

namespace PdfAnalyticsMcpConsole;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            var validationService = new InputValidationService();
            var pdfInfoService = new PdfInfoService();
            var pageTextService = new PageTextService(validationService);
            var pageGraphicsService = new PageGraphicsService(validationService);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };

            switch (command)
            {
                case "info":
                    return RunGetPdfInfo(args, validationService, pdfInfoService, jsonOptions);

                case "text":
                    return RunGetPageText(args, validationService, pageTextService, jsonOptions);

                case "graphics":
                    return RunGetPageGraphics(args, validationService, pageGraphicsService, jsonOptions);

                case "debug-ops":
                    return RunDebugOps(args, validationService);

                case "debug-paths":
                    return RunDebugPaths(args, validationService);

                default:
                    Console.Error.WriteLine($"Unknown command: {args[0]}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return 2;
        }
    }

    private static int RunGetPdfInfo(
        string[] args,
        IInputValidationService validationService,
        IPdfInfoService pdfInfoService,
        JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole info <pdfPath>");
            return 1;
        }

        var pdfPath = args[1];
        validationService.ValidateFilePath(pdfPath);
        var result = pdfInfoService.Extract(pdfPath);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private static int RunGetPageText(
        string[] args,
        IInputValidationService validationService,
        IPageTextService pageTextService,
        JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole text <pdfPath> <page> [granularity]");
            return 1;
        }

        var pdfPath = args[1];

        if (!int.TryParse(args[2], out int page))
        {
            Console.Error.WriteLine("Error: page must be an integer.");
            return 1;
        }

        var granularity = args.Length >= 4 ? args[3] : "words";

        validationService.ValidateFilePath(pdfPath);
        var result = pageTextService.Extract(pdfPath, page, granularity);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private static int RunGetPageGraphics(
        string[] args,
        IInputValidationService validationService,
        IPageGraphicsService pageGraphicsService,
        JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole graphics <pdfPath> <page>");
            return 1;
        }

        var pdfPath = args[1];

        if (!int.TryParse(args[2], out int page))
        {
            Console.Error.WriteLine("Error: page must be an integer.");
            return 1;
        }

        validationService.ValidateFilePath(pdfPath);
        var result = pageGraphicsService.Extract(pdfPath, page);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private static int RunDebugOps(
        string[] args,
        IInputValidationService validationService)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole debug-ops <pdfPath> <page>");
            return 1;
        }

        var pdfPath = args[1];
        if (!int.TryParse(args[2], out int page))
        {
            Console.Error.WriteLine("Error: page must be an integer.");
            return 1;
        }

        validationService.ValidateFilePath(pdfPath);
        using var doc = PdfDocument.Open(pdfPath);
        validationService.ValidatePageNumber(page, doc.NumberOfPages);
        var pdfPage = doc.GetPage(page);
        var ops = pdfPage.Operations;

        Console.WriteLine($"Total operations on page {page}: {ops.Count}");
        Console.WriteLine();
        Console.WriteLine("Operation types:");
        var types = ops.GroupBy(o => o.GetType().Name).OrderByDescending(g => g.Count());
        foreach (var g in types)
            Console.WriteLine($"  {g.Key}: {g.Count()}");

        Console.WriteLine();
        Console.WriteLine("First 100 operations:");
        foreach (var op in ops.Take(100))
            Console.WriteLine($"  [{op.GetType().Name}] {op}");

        return 0;
    }

    private static int RunDebugPaths(
        string[] args,
        IInputValidationService validationService)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole debug-paths <pdfPath> <page>");
            return 1;
        }

        var pdfPath = args[1];
        if (!int.TryParse(args[2], out int page))
        {
            Console.Error.WriteLine("Error: page must be an integer.");
            return 1;
        }

        validationService.ValidateFilePath(pdfPath);
        using var doc = PdfDocument.Open(pdfPath);
        validationService.ValidatePageNumber(page, doc.NumberOfPages);
        var pdfPage = doc.GetPage(page);

        var paths = pdfPage.Paths;
        Console.WriteLine($"Total paths on page {page}: {paths.Count}");
        Console.WriteLine();

        int i = 0;
        foreach (var path in paths.Take(30))
        {
            i++;
            var bbox = path.GetBoundingRectangle();
            Console.WriteLine($"Path {i}:");
            Console.WriteLine($"  IsFilled={path.IsFilled}, IsStroked={path.IsStroked}, IsClipping={path.IsClipping}");
            Console.WriteLine($"  FillColor={path.FillColor} (type: {path.FillColor?.GetType().Name})");
            Console.WriteLine($"  StrokeColor={path.StrokeColor} (type: {path.StrokeColor?.GetType().Name})");
            Console.WriteLine($"  LineWidth={path.LineWidth}, LineDashPattern={path.LineDashPattern}");
            if (bbox.HasValue)
                Console.WriteLine($"  BBox: x={bbox.Value.Left:F1}, y={bbox.Value.Bottom:F1}, w={bbox.Value.Width:F1}, h={bbox.Value.Height:F1}");
            Console.WriteLine($"  Subpaths: {path.Count}");
            foreach (var subpath in path.Take(5))
            {
                Console.WriteLine($"    Subpath: {subpath.Commands.Count} commands");
                foreach (var cmd in subpath.Commands.Take(10))
                    Console.WriteLine($"      [{cmd.GetType().Name}] {cmd}");
            }
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            PdfAnalyticsMcpConsole - CLI wrapper for PdfAnalyticsMcp tools

            Usage:
              PdfAnalyticsMcpConsole <command> [arguments]

            Commands:
              info      <pdfPath>                          Get PDF document metadata
              text      <pdfPath> <page> [granularity]     Get page text (granularity: words|letters, default: words)
              graphics  <pdfPath> <page>                   Get classified page graphics (rectangles, lines, paths)

            Examples:
              PdfAnalyticsMcpConsole info "C:\docs\report.pdf"
              PdfAnalyticsMcpConsole text "C:\docs\report.pdf" 1
              PdfAnalyticsMcpConsole text "C:\docs\report.pdf" 3 letters
              PdfAnalyticsMcpConsole graphics "C:\docs\report.pdf" 2
            """);
    }
}
