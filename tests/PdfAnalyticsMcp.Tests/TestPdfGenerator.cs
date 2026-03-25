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

    /// <summary>
    /// Creates a PDF with enough words to produce > 30 KB of JSON at word granularity.
    /// Used to test outputFile behavior on dense pages.
    /// </summary>
    public static string CreateDenseTextTestPdf()
    {
        var path = GetTestDataPath("sample-text-dense.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.Letter);

        // Generate ~600 words — each word element serializes to ~100 bytes of JSON,
        // so 600 words ≈ 60 KB which comfortably exceeds 30 KB.
        var words = new List<string>();
        for (int i = 1; i <= 600; i++)
            words.Add($"denseword{i}");

        double y = 750;
        int wordsPerLine = 8;
        for (int i = 0; i < words.Count; i += wordsPerLine)
        {
            var lineWords = words.Skip(i).Take(wordsPerLine);
            var line = string.Join(" ", lineWords);
            page.AddText(line, 8, new PdfPoint(36, y), helvetica);
            y -= 10;
        }

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Creates a PDF with known graphic elements for graphics extraction tests.
    /// Page 1: Rectangles (filled red, stroked blue) and straight lines (black, green).
    /// Page 2: Complex paths (circle, ellipse) that generate Bézier curves.
    /// Page 3: Graphics with save/restore state (different colors inside q/Q blocks).
    /// Returns the file path.
    /// </summary>
    public static string CreateGraphicsTestPdf()
    {
        var path = GetTestDataPath("sample-graphics.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();

        // Page 1: Rectangles and lines
        var page1 = builder.AddPage(PageSize.Letter);
        // Stroked rectangle at (100, 600) 200x50 (DrawRectangle only strokes; SetTextAndFillColor sets fill state but re S does not use it)
        page1.SetTextAndFillColor(255, 0, 0);
        page1.DrawRectangle(new PdfPoint(100, 600), 200, 50);

        // Stroked blue rectangle at (100, 500) 150x80
        page1.SetStrokeColor(0, 0, 255);
        page1.DrawRectangle(new PdfPoint(100, 500), 150, 80, 2);

        // Black line from (50, 400) to (300, 400)
        page1.ResetColor();
        page1.DrawLine(new PdfPoint(50, 400), new PdfPoint(300, 400));

        // Green line from (50, 350) to (250, 350)
        page1.SetStrokeColor(0, 128, 0);
        page1.DrawLine(new PdfPoint(50, 350), new PdfPoint(250, 350), 2);

        // Page 2: Complex paths (circle and ellipse generate Bézier curves)
        var page2 = builder.AddPage(PageSize.Letter);
        page2.SetStrokeColor(255, 0, 0);
        // DrawRectangle for reference
        page2.DrawRectangle(new PdfPoint(50, 700), 100, 50);

        // Circle at center ~(300, 500) with radius ~30 — generates Bézier curves
        page2.SetStrokeColor(0, 0, 255);
        page2.DrawCircle(new PdfPoint(300, 500), 30, 1);

        // Page 3: Save/restore state testing
        var page3 = builder.AddPage(PageSize.Letter);
        // Set red stroke, draw a line
        page3.SetStrokeColor(255, 0, 0);
        page3.DrawLine(new PdfPoint(50, 700), new PdfPoint(200, 700));

        // Draw a rectangle with different color after state change
        page3.SetStrokeColor(0, 255, 0);
        page3.DrawRectangle(new PdfPoint(50, 600), 100, 40, 1);

        // Another line with blue stroke
        page3.SetStrokeColor(0, 0, 255);
        page3.DrawLine(new PdfPoint(50, 500), new PdfPoint(200, 500), 3);

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Creates a PDF page with no drawn graphics (blank page) for empty-graphics testing.
    /// Returns the file path.
    /// </summary>
    public static string CreateBlankTestPdf()
    {
        var path = GetTestDataPath("sample-blank.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.Letter);

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Creates a PDF with fill-only, fill+stroke, triangle, and dashed-line graphics.
    /// Page 1: Filled-only rectangle (red fill, no stroke via fill painting).
    ///         Fill+stroke rectangle (green fill, blue stroke).
    ///         Triangle (complex path, not a rectangle).
    ///         Dashed line.
    /// Returns the file path.
    /// </summary>
    public static string CreateGraphicsExtendedTestPdf()
    {
        var path = GetTestDataPath("sample-graphics-extended.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.Letter);

        // Triangle at known coordinates — classified as complex path (3 line segments, not axis-aligned rect)
        page.SetStrokeColor(255, 0, 0);
        page.DrawTriangle(new PdfPoint(100, 600), new PdfPoint(200, 700), new PdfPoint(300, 600), 1);

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Returns a minimal valid 2x2 red PNG image as a byte array.
    /// PNG format: signature + IHDR + IDAT (uncompressed via stored block) + IEND.
    /// </summary>
    public static byte[] CreateMinimalPng()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // PNG signature
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk: 2x2 pixels, 8-bit RGB
        WriteChunk(bw, "IHDR", writer =>
        {
            writer.Write(ToBigEndian(2));  // width
            writer.Write(ToBigEndian(2));  // height
            writer.Write((byte)8);         // bit depth
            writer.Write((byte)2);         // color type: RGB
            writer.Write((byte)0);         // compression
            writer.Write((byte)0);         // filter
            writer.Write((byte)0);         // interlace
        });

        // IDAT chunk: image data compressed with zlib
        // Each row: filter byte (0=None) + 3 bytes per pixel (RGB) for 2 pixels = 7 bytes per row
        // 2 rows = 14 bytes raw data
        byte[] rawImageData =
        [
            0x00, 0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00, // Row 1: filter=None, red pixel, red pixel
            0x00, 0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00  // Row 2: filter=None, red pixel, red pixel
        ];

        // Wrap in zlib: 2-byte header + deflate stored block + 4-byte Adler-32
        byte[] zlibData = ZlibCompress(rawImageData);
        WriteChunk(bw, "IDAT", writer => writer.Write(zlibData));

        // IEND chunk
        WriteChunk(bw, "IEND", _ => { });

        return ms.ToArray();
    }

    private static void WriteChunk(BinaryWriter bw, string type, Action<BinaryWriter> writeData)
    {
        using var dataMs = new MemoryStream();
        using var dataBw = new BinaryWriter(dataMs);
        writeData(dataBw);
        dataBw.Flush();
        byte[] data = dataMs.ToArray();
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

        bw.Write(ToBigEndian(data.Length));              // length
        bw.Write(typeBytes);                              // type
        bw.Write(data);                                   // data
        uint crc = Crc32(typeBytes, data);
        bw.Write(ToBigEndian((int)crc));                  // CRC
    }

    private static byte[] ToBigEndian(int value) =>
        BitConverter.IsLittleEndian
            ? [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]
            : BitConverter.GetBytes(value);

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        // zlib header: CMF=0x78 (deflate, window 32K), FLG=0x01 (no dict, check bits)
        ms.WriteByte(0x78);
        ms.WriteByte(0x01);

        // Deflate stored block (BFINAL=1, BTYPE=00)
        ms.WriteByte(0x01); // BFINAL=1, BTYPE=00 (no compression)
        int len = data.Length;
        ms.WriteByte((byte)(len & 0xFF));
        ms.WriteByte((byte)((len >> 8) & 0xFF));
        ms.WriteByte((byte)(~len & 0xFF));
        ms.WriteByte((byte)((~len >> 8) & 0xFF));
        ms.Write(data, 0, data.Length);

        // Adler-32 checksum
        uint adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);

        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type) crc = CrcUpdate(crc, b);
        foreach (byte b in data) crc = CrcUpdate(crc, b);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint CrcUpdate(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }

    /// <summary>
    /// Creates a PDF with a single embedded PNG image at a known position.
    /// Page 1: 2x2 red PNG placed at (100, 500) with display size 200x150.
    /// Returns the file path.
    /// </summary>
    public static string CreateImageTestPdf()
    {
        var path = GetTestDataPath("sample-image.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.Letter);

        byte[] pngBytes = CreateMinimalPng();
        page.AddPng(pngBytes, new PdfRectangle(100, 500, 300, 650));

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Creates a PDF with multiple embedded images on page 1.
    /// Image 1: 2x2 red PNG at (50, 600) display size 100x80.
    /// Image 2: 2x2 red PNG at (200, 400) display size 150x120.
    /// Returns the file path.
    /// </summary>
    public static string CreateMultiImageTestPdf()
    {
        var path = GetTestDataPath("sample-multi-image.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.Letter);

        byte[] pngBytes = CreateMinimalPng();
        page.AddPng(pngBytes, new PdfRectangle(50, 600, 150, 680));
        page.AddPng(pngBytes, new PdfRectangle(200, 400, 350, 520));

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
