using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace PdfAnalyticsMcp.Tests;

public static class TestPdfGenerator
{
    private static string GetTestDataDir()
    {
        var testAssemblyDir = AppContext.BaseDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "tests", "TestData");
    }

    public static string GetTestDataPath(string fileName) =>
        Path.Combine(GetTestDataDir(), fileName);

    /// <summary>
    /// Creates a small PDF with known text content, multiple fonts, and a colored text element.
    /// Page 1: "Hello World" in Helvetica 12pt (black), "Bold Text" in Helvetica-Bold 14pt (black),
    ///         "Red Text" in Helvetica 12pt (red #FF0000).
    /// Returns the file path.
    /// </summary>
    public static string CreateTextTestPdf()
    {
        var path = GetTestDataPath("sample-text.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
        var helveticaBold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        var page = builder.AddPage(PageSize.Letter);

        // Regular black text
        page.AddText("Hello World", 12, new PdfPoint(72, 720), helvetica);

        // Bold text
        page.AddText("Bold Text", 14, new PdfPoint(72, 700), helveticaBold);

        // Red colored text
        page.SetTextAndFillColor(255, 0, 0);
        page.AddText("Red Text", 12, new PdfPoint(72, 680), helvetica);
        page.ResetColor();

        // Italic font text
        var helveticaOblique = builder.AddStandard14Font(Standard14Font.HelveticaOblique);
        page.AddText("Italic Text", 12, new PdfPoint(72, 660), helveticaOblique);

        // Bold-Italic text
        var helveticaBoldOblique = builder.AddStandard14Font(Standard14Font.HelveticaBoldOblique);
        page.AddText("BoldItalic Text", 12, new PdfPoint(72, 640), helveticaBoldOblique);

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Creates a PDF with ~300 words on a single page for response size testing.
    /// Returns the file path.
    /// </summary>
    public static string CreateLargeTextTestPdf()
    {
        var path = GetTestDataPath("sample-text-large.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.Letter);

        // Generate ~300 words of text spread across lines
        var words = new List<string>();
        for (int i = 1; i <= 300; i++)
            words.Add($"word{i}");

        double y = 750;
        int wordsPerLine = 10;
        for (int i = 0; i < words.Count; i += wordsPerLine)
        {
            var lineWords = words.Skip(i).Take(wordsPerLine);
            var line = string.Join(" ", lineWords);
            page.AddText(line, 10, new PdfPoint(36, y), helvetica);
            y -= 14;
        }

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
