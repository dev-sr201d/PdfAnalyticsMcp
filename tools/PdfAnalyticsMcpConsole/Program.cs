using System.Text.Json;
using PdfAnalyticsMcp.Services;

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

    private static void PrintUsage()
    {
        Console.WriteLine("""
            PdfAnalyticsMcpConsole - CLI wrapper for PdfAnalyticsMcp tools

            Usage:
              PdfAnalyticsMcpConsole <command> [arguments]

            Commands:
              info  <pdfPath>                          Get PDF document metadata
              text  <pdfPath> <page> [granularity]     Get page text (granularity: words|letters, default: words)

            Examples:
              PdfAnalyticsMcpConsole info "C:\docs\report.pdf"
              PdfAnalyticsMcpConsole text "C:\docs\report.pdf" 1
              PdfAnalyticsMcpConsole text "C:\docs\report.pdf" 3 letters
            """);
    }
}
