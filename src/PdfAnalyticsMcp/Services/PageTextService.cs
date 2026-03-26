using PdfAnalyticsMcp.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PdfAnalyticsMcp.Services;

public class PageTextService(IInputValidationService validationService) : IPageTextService
{
    private static readonly string[] ValidGranularities = ["words", "letters"];

    public PageTextDto Extract(string pdfPath, int page, string granularity)
    {
        if (!ValidGranularities.Contains(granularity, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Granularity must be 'words' or 'letters'.");

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"The file could not be accessed: {pdfPath}. It may be in use by another process.");
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            throw new ArgumentException("The file could not be opened as a PDF.");
        }

        using (document)
        {
            validationService.ValidatePageNumber(page, document.NumberOfPages);

            var pdfPage = document.GetPage(page);

            List<TextElementDto> elements;
            try
            {
                elements = string.Equals(granularity, "words", StringComparison.OrdinalIgnoreCase)
                    ? ExtractWords(pdfPage)
                    : ExtractLetters(pdfPage);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"An error occurred extracting text from page {page}.");
            }

            return new PageTextDto(
                page,
                FormatUtils.RoundCoordinate(pdfPage.Width),
                FormatUtils.RoundCoordinate(pdfPage.Height),
                elements);
        }
    }

    public PageTextSummaryDto ExtractToFile(string pdfPath, int page, string granularity, string outputFile)
    {
        if (!Path.IsPathRooted(outputFile))
            throw new ArgumentException("Output file path must be an absolute path.");

        if (outputFile.Contains(".."))
            throw new ArgumentException("Output file path must not contain path traversal sequences.");

        var parentDir = Path.GetDirectoryName(outputFile);
        if (parentDir is null || !Directory.Exists(parentDir))
            throw new ArgumentException("The parent directory of the output file path does not exist.");

        var fullResult = Extract(pdfPath, page, granularity);

        WriteCsv(outputFile, fullResult.Elements);

        var fileInfo = new FileInfo(outputFile);

        return new PageTextSummaryDto(
            fullResult.Page,
            fullResult.Width,
            fullResult.Height,
            fullResult.Elements.Count,
            fileInfo.FullName,
            fileInfo.Length);
    }

    private static void WriteCsv(string filePath, IReadOnlyList<TextElementDto> elements)
    {
        using var writer = new StreamWriter(filePath, append: false, encoding: System.Text.Encoding.UTF8);
        writer.WriteLine("text,x,y,w,h,font,size,color,bold,italic");

        foreach (var e in elements)
        {
            writer.Write(CsvEscape(e.Text));
            writer.Write(',');
            writer.Write(e.X);
            writer.Write(',');
            writer.Write(e.Y);
            writer.Write(',');
            writer.Write(e.W);
            writer.Write(',');
            writer.Write(e.H);
            writer.Write(',');
            writer.Write(CsvEscape(e.Font));
            writer.Write(',');
            writer.Write(e.Size);
            writer.Write(',');
            writer.Write(e.Color ?? "");
            writer.Write(',');
            writer.Write(e.Bold == true ? "true" : "");
            writer.Write(',');
            writer.Write(e.Italic == true ? "true" : "");
            writer.WriteLine();
        }
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
        return value;
    }

    private static List<TextElementDto> ExtractWords(Page pdfPage)
    {
        var words = pdfPage.GetWords().ToList();
        List<TextElementDto> elements = new(words.Count);

        foreach (var word in words)
        {
            var firstLetter = word.Letters.FirstOrDefault();
            var fontName = firstLetter?.FontName ?? "Unknown";
            var fontSize = firstLetter?.PointSize ?? 0;
            var color = GetColorHex(firstLetter);
            var (bold, italic) = InferBoldItalic(fontName);

            elements.Add(new TextElementDto(
                word.Text,
                FormatUtils.RoundCoordinate(word.BoundingBox.Left),
                FormatUtils.RoundCoordinate(word.BoundingBox.Bottom),
                FormatUtils.RoundCoordinate(word.BoundingBox.Width),
                FormatUtils.RoundCoordinate(word.BoundingBox.Height),
                fontName,
                FormatUtils.RoundCoordinate(fontSize),
                color,
                bold,
                italic));
        }

        return elements;
    }

    private static List<TextElementDto> ExtractLetters(Page pdfPage)
    {
        var letters = pdfPage.Letters;
        List<TextElementDto> elements = new(letters.Count);

        foreach (var letter in letters)
        {
            var fontName = letter.FontName ?? "Unknown";
            var color = GetColorHex(letter);
            var (bold, italic) = InferBoldItalic(fontName);

            elements.Add(new TextElementDto(
                letter.Value,
                FormatUtils.RoundCoordinate(letter.BoundingBox.Left),
                FormatUtils.RoundCoordinate(letter.BoundingBox.Bottom),
                FormatUtils.RoundCoordinate(letter.BoundingBox.Width),
                FormatUtils.RoundCoordinate(letter.BoundingBox.Height),
                fontName,
                FormatUtils.RoundCoordinate(letter.PointSize),
                color,
                bold,
                italic));
        }

        return elements;
    }

    internal static string? GetColorHex(Letter? letter)
    {
        if (letter?.Color is null)
            return null;

        try
        {
            var (r, g, b) = letter.Color.ToRGBValues();
            var hex = FormatUtils.FormatColor(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255));

            return hex == "#000000" ? null : hex;
        }
        catch
        {
            return null;
        }
    }

    internal static (bool? Bold, bool? Italic) InferBoldItalic(string fontName)
    {
        var name = fontName ?? "";
        bool? bold = name.Contains("Bold", StringComparison.OrdinalIgnoreCase) ? true : null;
        bool? italic = name.Contains("Italic", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase)
            ? true
            : null;

        return (bold, italic);
    }
}
