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

        // Text containing a comma (for CSV escaping tests)
        page.ResetColor();
        page.AddText("Hello, World", 12, new PdfPoint(72, 620), helvetica);

        // Text containing a double quote (for CSV escaping tests)
        page.AddText("Say \"Hi\"", 12, new PdfPoint(72, 600), helvetica);

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
    /// Creates a PDF with metadata fields and 3 pages: 2 US Letter (612×792) + 1 A4 (595×842).
    /// This ensures US Letter is the predominant size and the A4 page appears as a page-size exception.
    /// Overwrites any existing file.
    /// </summary>
    public static string CreateMetadataTestPdf()
    {
        var path = GetTestDataPath("sample-with-metadata.pdf");

        var builder = new PdfDocumentBuilder();
        builder.DocumentInformation.Title = "Test Document";
        builder.DocumentInformation.Author = "Test Author";
        builder.DocumentInformation.Subject = "Test Subject";
        builder.DocumentInformation.Keywords = "test, pdf, sample";
        builder.DocumentInformation.Creator = "TestCreator";
        builder.DocumentInformation.Producer = "TestProducer";

        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Page 1: US Letter
        var page1 = builder.AddPage(PageSize.Letter);
        page1.AddText("Page 1 - Letter", 12, new PdfPoint(72, 720), helvetica);

        // Page 2: US Letter
        var page2 = builder.AddPage(PageSize.Letter);
        page2.AddText("Page 2 - Letter", 12, new PdfPoint(72, 720), helvetica);

        // Page 3: A4
        var page3 = builder.AddPage(PageSize.A4);
        page3.AddText("Page 3 - A4", 12, new PdfPoint(72, 720), helvetica);

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

    /// <summary>
    /// Creates a minimal PDF with a Form XObject that contains an embedded image.
    /// The Form XObject is referenced once on the page, at position (100, 500) with display size 200x150.
    /// This tests that images inside Form XObjects are discovered via recursive traversal.
    /// </summary>
    public static string CreateFormXObjectImageTestPdf()
    {
        var path = GetTestDataPath("sample-formxobj-image.pdf");
        if (File.Exists(path)) return path;

        // Minimal 2x2 red pixel RGB raw data (no PNG wrapper — raw PDF image stream)
        byte[] imageData = [255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0]; // 4 pixels × 3 bytes RGB

        // Form XObject content stream: draw the image scaled to the unit box
        byte[] formStream = System.Text.Encoding.ASCII.GetBytes("1 0 0 1 0 0 cm /Im1 Do\n");

        // Page content stream: position the Form XObject at (100, 500) with size 200×150
        byte[] pageStream = System.Text.Encoding.ASCII.GetBytes("q 200 0 0 150 100 500 cm /Form1 Do Q\n");

        var pdfBytes = BuildRawPdfWithFormXObject(pageStream, formStream, imageData);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, pdfBytes);
        return path;
    }

    /// <summary>
    /// Creates a minimal PDF with a Form XObject containing an image, referenced twice on the page
    /// at different positions. This tests duplicate image deduplication — the same image object
    /// should be reported with two bounding boxes but extracted to disk only once.
    /// Form XObject at (100, 500) 200×150 and at (300, 300) 200×150.
    /// </summary>
    public static string CreateDuplicateFormXObjectImageTestPdf()
    {
        var path = GetTestDataPath("sample-dupformxobj-image.pdf");
        if (File.Exists(path)) return path;

        byte[] imageData = [255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0]; // 2x2 red RGB
        byte[] formStream = System.Text.Encoding.ASCII.GetBytes("1 0 0 1 0 0 cm /Im1 Do\n");

        // Reference Form1 twice at different positions
        byte[] pageStream = System.Text.Encoding.ASCII.GetBytes(
            "q 200 0 0 150 100 500 cm /Form1 Do Q\nq 200 0 0 150 300 300 cm /Form1 Do Q\n");

        var pdfBytes = BuildRawPdfWithFormXObject(pageStream, formStream, imageData);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, pdfBytes);
        return path;
    }

    /// <summary>
    /// Builds a raw PDF with a single page containing a Form XObject that draws an image.
    /// Page size is US Letter (612×792). The form and image XObjects are linked via resources.
    /// </summary>
    private static byte[] BuildRawPdfWithFormXObject(byte[] pageStream, byte[] formStream, byte[] imageData)
    {
        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        w.NewLine = "\n";
        var offsets = new List<long>();

        w.Write("%PDF-1.4\n");

        // Object 1: Catalog
        w.Flush(); offsets.Add(ms.Position);
        w.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // Object 2: Pages
        w.Flush(); offsets.Add(ms.Position);
        w.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Object 3: Page
        w.Flush(); offsets.Add(ms.Position);
        w.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /XObject << /Form1 5 0 R >> >> >>\nendobj\n");

        // Object 4: Page content stream
        w.Flush(); offsets.Add(ms.Position);
        w.Write($"4 0 obj\n<< /Length {pageStream.Length} >>\nstream\n");
        w.Flush(); ms.Write(pageStream);
        w.Write("\nendstream\nendobj\n");

        // Object 5: Form XObject
        w.Flush(); offsets.Add(ms.Position);
        w.Write($"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << /XObject << /Im1 6 0 R >> >> /Length {formStream.Length} >>\nstream\n");
        w.Flush(); ms.Write(formStream);
        w.Write("\nendstream\nendobj\n");

        // Object 6: Image XObject
        w.Flush(); offsets.Add(ms.Position);
        w.Write($"6 0 obj\n<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {imageData.Length} >>\nstream\n");
        w.Flush(); ms.Write(imageData);
        w.Write("\nendstream\nendobj\n");

        // xref table
        w.Flush();
        long xrefOffset = ms.Position;
        w.Write($"xref\n0 {offsets.Count + 1}\n");
        w.Write("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            w.Write($"{offset:D10} 00000 n \n");
        }

        // Trailer
        w.Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal PDF with nested Form XObjects: Page → Form1 → Form2 → Image.
    /// This tests deep Form XObject recursion (more than one level deep).
    /// </summary>
    public static string CreateNestedFormXObjectImageTestPdf()
    {
        var path = GetTestDataPath("sample-nestedformxobj-image.pdf");
        if (File.Exists(path)) return path;

        byte[] imageData = [255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0]; // 2x2 red RGB

        // Form2 draws the image
        byte[] form2Stream = System.Text.Encoding.ASCII.GetBytes("1 0 0 1 0 0 cm /Im1 Do\n");
        // Form1 draws Form2
        byte[] form1Stream = System.Text.Encoding.ASCII.GetBytes("1 0 0 1 0 0 cm /Form2 Do\n");
        // Page draws Form1 at (100, 500) 200×150
        byte[] pageStream = System.Text.Encoding.ASCII.GetBytes("q 200 0 0 150 100 500 cm /Form1 Do Q\n");

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);
        w.NewLine = "\n";
        var offsets = new List<long>();

        w.Write("%PDF-1.4\n");

        w.Flush(); offsets.Add(ms.Position);
        w.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        w.Flush(); offsets.Add(ms.Position);
        w.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Page references Form1
        w.Flush(); offsets.Add(ms.Position);
        w.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /XObject << /Form1 5 0 R >> >> >>\nendobj\n");

        w.Flush(); offsets.Add(ms.Position);
        w.Write($"4 0 obj\n<< /Length {pageStream.Length} >>\nstream\n");
        w.Flush(); ms.Write(pageStream);
        w.Write("\nendstream\nendobj\n");

        // Form1 references Form2
        w.Flush(); offsets.Add(ms.Position);
        w.Write($"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << /XObject << /Form2 6 0 R >> >> /Length {form1Stream.Length} >>\nstream\n");
        w.Flush(); ms.Write(form1Stream);
        w.Write("\nendstream\nendobj\n");

        // Form2 references Im1
        w.Flush(); offsets.Add(ms.Position);
        w.Write($"6 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Resources << /XObject << /Im1 7 0 R >> >> /Length {form2Stream.Length} >>\nstream\n");
        w.Flush(); ms.Write(form2Stream);
        w.Write("\nendstream\nendobj\n");

        // Image XObject
        w.Flush(); offsets.Add(ms.Position);
        w.Write($"7 0 obj\n<< /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length {imageData.Length} >>\nstream\n");
        w.Flush(); ms.Write(imageData);
        w.Write("\nendstream\nendobj\n");

        w.Flush();
        long xrefOffset = ms.Position;
        w.Write($"xref\n0 {offsets.Count + 1}\n");
        w.Write("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            w.Write($"{offset:D10} 00000 n \n");
        }
        w.Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");

        w.Flush();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    /// <summary>
    /// Creates a minimal valid JPEG byte array (2x2 pixels, red).
    /// This is a hand-crafted minimal JFIF file.
    /// </summary>
    public static byte[] CreateMinimalJpeg()
    {
        // Minimal valid JPEG: 2x2 red image, created via SkiaSharp
        var info = new SkiaSharp.SKImageInfo(2, 2, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque);
        using var bitmap = new SkiaSharp.SKBitmap(info);
        var pixels = new byte[2 * 2 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0;       // B
            pixels[i + 1] = 0;   // G
            pixels[i + 2] = 255; // R
            pixels[i + 3] = 255; // A
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Creates a PDF with a single embedded JPEG image at a known position.
    /// Page 1: 2x2 red JPEG placed at (100, 500) with display size 200x150.
    /// Returns the file path.
    /// </summary>
    public static string CreateJpegImageTestPdf()
    {
        var path = GetTestDataPath("sample-jpeg-image.pdf");
        if (File.Exists(path)) return path;

        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.Letter);

        byte[] jpegBytes = CreateMinimalJpeg();
        page.AddJpeg(jpegBytes, new PdfRectangle(100, 500, 300, 650));

        var bytes = builder.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
