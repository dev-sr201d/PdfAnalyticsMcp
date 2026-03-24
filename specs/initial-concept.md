# PDF Analytics MCP Concept

## Proposed MCP Tool Set

### 1. `GetPdfInfo` — Document overview
Returns page count, page dimensions, title/author metadata, and optionally a bookmarks/outline tree. Lets the agent plan its page-by-page traversal.

### 2. `GetPageText` — Letter-level text with full metadata
Per page, returns every letter (or word) with:
- **Position**: x, y, width, height (in PDF points)
- **Font**: name, size, bold/italic flags, font family
- **Color**: RGB fill color of the text
- **Reading order metadata**: original content stream order

PdfPig already extracts all of this via `page.Letters` — you're using position and font but not color. Color is important because many complex layouts use colored text for headings, sidebar titles, or hyperlink-style callouts.

**Format consideration**: Letter-by-letter JSON is enormous. Pre-grouping into words (with font/color attributes) or even lines is much more practical for the AI. Offer a `granularity` parameter (`letters` | `words` | `lines`).

### 3. `GetPageGraphics` — Drawn paths, lines, rectangles
This is the **critical missing piece**. PdfPig exposes `page.Paths` (list of `PdfPath` objects) and raw `page.Operations`, but your current code doesn't use them. These contain:

| Graphic element | What it reveals |
|---|---|
| **Filled rectangles** with color | Sidebar backgrounds, callout box fills, table cell shading |
| **Stroked rectangles** | Bordered boxes, callout frames, table cell borders |
| **Horizontal/vertical lines** | Table gridlines, section dividers, underlines |
| **Line properties** (width, dash pattern, color) | Distinguishing decorative rules from table borders |

The tool should return each path as a classified shape:
- **Rectangles**: bounds (x, y, w, h), fill color, stroke color, stroke width
- **Lines**: start point, end point, stroke color, width, dash pattern
- **Complex paths**: simplified bounding box + vertex list

This requires walking PdfPig's graphics state operations to track the current transformation matrix, color state, and line style — then correlating those with path construction operations. It's medium complexity but PdfPig provides all the raw data.

### 4. `GetPageImages` — Embedded image positions and data
Returns each image's:
- **Bounding box** (position and size on page)
- **Pixel dimensions** and color space
- **Base64-encoded PNG** (via PdfPig's `image.TryGetPng()`)

Images matter because the AI needs to know *where* they are to understand text flow around them. The actual image bytes let the agent include `![](image.png)` references in the output.

### 5. `RenderPagePreview` — Full page as an image (optional but powerful)

Renders the page to a PNG so a multimodal model can literally *see* the layout. This is the most valuable tool for complex layouts — the AI can cross-reference visual appearance with the structured data from tools 2–4.

**Caveat**: PdfPig cannot render pages. You'd need an additional library:
- **Docnet** (PDFium wrapper) — best rendering quality, but has native dependencies
- **External CLI** like `mutool draw` (MuPDF) or `pdftoppm` (Poppler) — spawned as a process
- **SkiaSharp** — only if you build a custom renderer (not practical)

This is optional but gives the AI a huge advantage on complex layouts.

---

## Recommended Tool Signatures

```csharp
[McpServerToolType]
public static class PdfInspectTools
{
    [McpServerTool, Description("Returns PDF document metadata: page count, dimensions, title, bookmarks.")]
    public static string GetPdfInfo(
        [Description("Absolute path to the PDF file")] string pdfPath);

    [McpServerTool, Description("Returns text content from a PDF page with position, font, size, and color for each element.")]
    public static string GetPageText(
        [Description("Absolute path to the PDF file")] string pdfPath,
        [Description("1-based page number")] int page,
        [Description("Level of detail: 'words' (default) or 'letters'")] string granularity = "words");

    [McpServerTool, Description("Returns all drawn graphics on a page: rectangles, lines, and paths with colors, fills, and stroke properties. Use to identify table borders, sidebars, callout boxes, and dividers.")]
    public static string GetPageGraphics(
        [Description("Absolute path to the PDF file")] string pdfPath,
        [Description("1-based page number")] int page);

    [McpServerTool, Description("Returns embedded images on a page with bounding boxes and optional Base64 PNG data.")]
    public static string GetPageImages(
        [Description("Absolute path to the PDF file")] string pdfPath,
        [Description("1-based page number")] int page,
        [Description("If true, includes base64-encoded image data")] bool includeData = false);

    [McpServerTool, Description("Renders a PDF page as a PNG image for visual layout analysis.")]
    public static string RenderPagePreview(
        [Description("Absolute path to the PDF file")] string pdfPath,
        [Description("1-based page number")] int page,
        [Description("Render DPI (default 150)")] int dpi = 150);
}
```

---

## Implementation Complexity

| Tool | PdfPig support | Effort |
|---|---|---|
| `GetPdfInfo` | Full — `document.NumberOfPages`, metadata, bookmarks | Low |
| `GetPageText` | Full — `page.Letters` / `page.GetWords()` with all properties | Low (mostly serialization) |
| `GetPageGraphics` | Partial — raw `page.Operations` + `page.Paths` need state machine processing | **Medium-High** |
| `GetPageImages` | Full — `page.GetImages()`, `TryGetPng()` | Low-Medium |
| `RenderPagePreview` | **Not supported** — needs Docnet or external tool | Medium (new dependency) |

The graphics extraction is the hardest part. You need to replay the graphics state stack (tracking current color, transform matrix, line style) while intercepting path construction and painting operations. PdfPig gives you the raw operations list but doesn't pre-classify shapes for you.

---

## Expected Agent Workflow

1. `GetPdfInfo("book.pdf")` → 320 pages, 612×792pt (US Letter)
2. For each page (or range):
   - `GetPageGraphics(page: N)` → "there are 14 horizontal lines and 12 vertical lines forming a grid at y=200–500, plus a filled blue rectangle at x=400–580, y=100–700"
   - `GetPageText(page: N)` → "words at positions... the text inside the blue rectangle is a sidebar, the text within the grid is a table"
   - `GetPageImages(page: N)` → "illustration at top-right, 300×400px"
   - (optional) `RenderPagePreview(page: N)` → visual confirmation
3. Agent synthesizes: "This page has a two-column layout with a sidebar on the right, a table in the left column, and an illustration above the table"
4. Agent generates appropriate Markdown

---

## Key Design Decision: Data Volume

A dense page might have 3,000+ letters and hundreds of graphics operations. Returning all of this as JSON in a single tool call could be 50–100KB per page. Consider:

- **Default to `words` granularity** (not letters) — reduces volume ~5x
- **Classify graphics server-side** into rectangles/lines/curves rather than returning raw operations — this is necessary anyway since raw ops are unusable without state tracking
- **Page-by-page** (already planned) — never load the whole document at once
- **Summary mode** for graphics — e.g., "12 horizontal lines, 8 vertical lines, 3 filled rectangles" with full details on request

