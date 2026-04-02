using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PdfAnalyticsMcp.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Geometry;

namespace PdfAnalyticsMcpConsole;

public static class Program
{
    public static async Task<int> Main(string[] args)
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
            var renderService = new RenderPagePreviewService(validationService, NullLogger<RenderPagePreviewService>.Instance);
            var pageImagesService = new PageImagesService(validationService, renderService, NullLogger<PageImagesService>.Instance);

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

                case "images":
                    return await RunGetPageImages(args, validationService, pageImagesService, jsonOptions);

                case "render":
                    return await RunRenderPagePreview(args, validationService, renderService, jsonOptions);

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

    private static async Task<int> RunGetPageImages(
        string[] args,
        IInputValidationService validationService,
        IPageImagesService pageImagesService,
        JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole images <pdfPath> <page> [outputPath]");
            return 1;
        }

        var pdfPath = args[1];

        if (!int.TryParse(args[2], out int page))
        {
            Console.Error.WriteLine("Error: page must be an integer.");
            return 1;
        }

        string? outputPath = args.Length >= 4 ? args[3] : null;

        validationService.ValidateFilePath(pdfPath);
        var result = await pageImagesService.ExtractAsync(pdfPath, page, outputPath);
        Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
        return 0;
    }

    private static async Task<int> RunRenderPagePreview(
        string[] args,
        IInputValidationService validationService,
        IRenderPagePreviewService renderService,
        JsonSerializerOptions jsonOptions)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: PdfAnalyticsMcpConsole render <pdfPath> <page> [dpi]");
            return 1;
        }

        var pdfPath = args[1];

        if (!int.TryParse(args[2], out int page))
        {
            Console.Error.WriteLine("Error: page must be an integer.");
            return 1;
        }

        int dpi = 150;
        if (args.Length >= 4 && !int.TryParse(args[3], out dpi))
        {
            Console.Error.WriteLine("Error: dpi must be an integer.");
            return 1;
        }

        validationService.ValidateFilePath(pdfPath);
        var result = await renderService.RenderAsync(pdfPath, page, dpi, "png", 80);

        // Write image to file next to the source PDF
        var outputPath = Path.Combine(
            Path.GetDirectoryName(pdfPath)!,
            $"{Path.GetFileNameWithoutExtension(pdfPath)}_page{page}_{dpi}dpi.png");
        File.WriteAllBytes(outputPath, result.ImageData);

        Console.WriteLine($"Rendered page {result.Page} at {result.Dpi} DPI: {result.Width}x{result.Height} pixels");
        Console.WriteLine($"PNG saved to: {outputPath}");
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
              images    <pdfPath> <page> [outputPath]      Get page images (outputPath: directory for PNG extraction)
              render    <pdfPath> <page> [dpi]             Render page as PNG (dpi: 72-600, default: 150)

            Examples:
              PdfAnalyticsMcpConsole info "C:\docs\report.pdf"
              PdfAnalyticsMcpConsole text "C:\docs\report.pdf" 1
              PdfAnalyticsMcpConsole text "C:\docs\report.pdf" 3 letters
              PdfAnalyticsMcpConsole graphics "C:\docs\report.pdf" 2
              PdfAnalyticsMcpConsole images "C:\docs\report.pdf" 1
              PdfAnalyticsMcpConsole images "C:\docs\report.pdf" 1 "C:\output"
              PdfAnalyticsMcpConsole render "C:\docs\report.pdf" 1
              PdfAnalyticsMcpConsole render "C:\docs\report.pdf" 1 300
            """);
    }
}
