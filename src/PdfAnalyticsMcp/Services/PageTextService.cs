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
            throw new ArgumentException($"Invalid granularity '{granularity}'. Valid values are: 'words', 'letters'.");

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

            var elements = string.Equals(granularity, "words", StringComparison.OrdinalIgnoreCase)
                ? ExtractWords(pdfPage)
                : ExtractLetters(pdfPage);

            return new PageTextDto(
                page,
                FormatUtils.RoundCoordinate(pdfPage.Width),
                FormatUtils.RoundCoordinate(pdfPage.Height),
                elements);
        }
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
