# AGENTS.md — PdfAnalyticsMcp Development Guidelines

This document synthesizes the project requirements (PRD), architecture decisions (ADRs), and technology best practices into a single reference for all agents and developers working on this codebase.

---

## 1. Project Overview

**PdfAnalyticsMcp** is a local MCP (Model Context Protocol) server that exposes PDF inspection tools over stdio. AI agents use these tools to understand complex PDF documents — including text with font/color metadata, drawn graphics (table borders, sidebars, callout boxes), embedded images, and visual page previews — and produce faithful representations in other formats such as Markdown.

### Key Constraints

- The server runs as a **local child process** communicating over **stdin/stdout** (stdio transport). No HTTP, no network.
- All page-content tools operate on a **single page per call**. Never load an entire document at once.
- Response payloads must target **≤ 30 KB per call** at default granularity. Tools that may exceed this limit support an optional `outputFile` parameter to write the full JSON to disk and return a compact summary inline.
- The server is **read-only** — it does not modify PDFs (writing `outputFile` output is the sole exception; no PDF content is altered).
- OCR, form fields, digital signatures, and PDF/A compliance are **out of scope**.

---

## 2. Technology Stack

| Component | Technology | Version | ADR |
|-----------|-----------|---------|-----|
| Language / Runtime | C# / .NET | .NET 9, C# 13 | ADR-0001 |
| PDF Parsing | PdfPig (`UglyToad.PdfPig`) | Latest stable | ADR-0002 |
| MCP Server SDK | Official C# SDK (`ModelContextProtocol`) | Latest stable | ADR-0003 |
| PDF Rendering & Image Extraction | PDFiumCore (`PDFiumCore`) | Latest stable | ADR-0004 |
| JPEG Encoding | SkiaSharp (`SkiaSharp`) | Latest stable | ADR-0004 |
| Serialization | `System.Text.Json` (built-in) | — | ADR-0005 |
| Hosting | `Microsoft.Extensions.Hosting` | — | ADR-0003 |

---

## 3. Project Structure

```
PdfAnalyticsMcp/
├── specs/
│   ├── initial-concept.md       # Original concept / design notes
│   ├── prd.md                   # Product Requirements Document
│   ├── adr/                     # Architecture Decision Records
│   │   ├── 0001-language-and-runtime.md
│   │   ├── 0002-pdf-parsing-library.md
│   │   ├── 0003-mcp-server-sdk.md
│   │   ├── 0004-pdf-rendering-library.md
│   │   └── 0005-serialization-and-response-format.md
│   ├── features/                # Feature Requirement Documents (FRDs)
│   └── tasks/                   # Task breakdowns for implementation
├── src/
│   └── PdfAnalyticsMcp/         # Main server project
│       ├── Program.cs            # Host setup, MCP server registration
│       ├── Tools/                # MCP tool classes
│       ├── Models/               # DTOs for tool responses
│       └── Services/             # PDF extraction logic
├── tests/
│   └── PdfAnalyticsMcp.Tests/   # Unit and integration tests
├── AGENTS.md                    # This file
└── PdfAnalyticsMcp.sln
```

### Conventions

- One tool class per file in `Tools/`.
- Business logic (PDF extraction, graphics classification) goes in `Services/`, not in tool methods directly.
- DTOs in `Models/` are plain record types used only for serialization.
- Tool methods are thin wrappers: validate input, call a service, serialize the result.

---

## 4. MCP Server Tools

The server exposes five tools, each operating on a single PDF page (except `GetPdfInfo` which is document-level):

| Tool | Purpose | REQ |
|------|---------|-----|
| `GetPdfInfo` | Page count, dimensions, title, author, subject, keywords, creator, producer, bookmarks | REQ-1 |
| `GetPageText` | Text with position, font, size, color; `words` or `letters` granularity; optional `outputFile` for large pages | REQ-2 |
| `GetPageGraphics` | Classified shapes: rectangles, lines, paths with fill/stroke/color | REQ-3 |
| `RenderPagePreview` | Full page rendered as PNG or JPEG at configurable DPI and quality | REQ-5 |
| `GetPageImages` | Image bounding boxes, dimensions; optional file-based PNG extraction to disk via per-image rendered bitmaps | REQ-4 |

---

## 5. C# / .NET 9 Best Practices

### Project Setup

```xml
<PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
</PropertyGroup>
```

### General Rules

- **Nullable reference types** are enabled project-wide. Never suppress nullable warnings with `!` unless provably safe. Validate nullability at system boundaries (tool parameters).
- **File-scoped namespaces** — use `namespace Foo;` (single-line), not block syntax.
- **Primary constructors** — prefer for simple DI injection in service classes.
- **`async`/`await`** — use for any I/O-bound operations. Never block with `.Result` or `.Wait()`.
- **`using` declarations** — prefer `using var stream = ...;` over `using (var stream = ...) { }`.
- **Pattern matching** — prefer `is`, `switch` expressions, and property patterns for type checks and complex conditionals.
- **Records** — use `record` or `record struct` for DTOs and immutable data types.
- **Target-typed `new`** — use `List<string> items = [];` and `MyType x = new()` where the type is clear.

### Dependency Injection

- Register services in `Program.cs` via the `IServiceCollection` on `Host.CreateApplicationBuilder`.
- Prefer constructor injection. Avoid service locator patterns.
- Use `AddSingleton` for stateless services, `AddTransient` for per-call services that hold disposable resources.

### Error Handling

- Validate tool parameters at the boundary (null/empty path, page number out of range) and throw `ArgumentException` with clear messages.
- **The MCP SDK only preserves error messages from `McpException` subclasses.** For all other exception types (including `ArgumentException`), the SDK returns a generic "An error occurred invoking '...'" message to prevent leaking internal details. To surface validation errors with meaningful messages, catch `ArgumentException` in the tool method and rethrow as `McpException`:
  ```csharp
  catch (ArgumentException ex)
  {
      throw new McpException(ex.Message);
  }
  ```
- Throw `McpProtocolException` only for protocol-level issues (e.g., malformed requests), not for application errors.
- Never expose internal details (stack traces, file paths beyond what the user provided) in error messages.

---

## 6. MCP Server SDK Best Practices

### Server Bootstrap

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
// Register application services here
await builder.Build().RunAsync();
```

### Key Rules

- **All logging goes to stderr.** Stdout is reserved for MCP protocol messages. Configure `LogToStandardErrorThreshold = LogLevel.Trace` to ensure no log output leaks to stdout.
- **Attribute-based tool registration.** Mark tool container classes with `[McpServerToolType]` and individual tool methods with `[McpServerTool]`.
- **Descriptive annotations are critical.** Every tool and every parameter must have a `[Description("...")]` attribute. These descriptions are what the AI agent sees to understand how to use the tool. Write them as clear, actionable instructions.
- **Tool methods can be static or instance.** Use instance methods when the tool needs injected services. The MCP SDK resolves DI for instance tool classes.
- **Cancellation tokens.** Accept `CancellationToken` as a parameter in tool methods for long-running operations. The SDK passes the client's cancellation signal.
- **Return types.** Return `string` for simple text results. For structured data, return a serialized JSON string. The SDK wraps the return value in a `TextContentBlock`.

### Tool Design Principles

- Tools should be **self-contained** — a single tool call must return a complete, usable result without requiring the agent to combine multiple calls.
- Keep tool names **verb-noun** and concise: `GetPdfInfo`, `GetPageText`, not `RetrievePdfDocumentInformation`.
- Default parameter values should produce the most commonly useful result (e.g., `granularity = "words"`, `includeData = false`, `dpi = 150`).

---

## 7. PdfPig Best Practices

### Document Lifecycle

```csharp
using PdfDocument document = PdfDocument.Open(pdfPath);
Page page = document.GetPage(pageNumber); // 1-based
```

- **Always dispose `PdfDocument`** via `using` declaration. PdfPig holds file handles and memory-mapped resources.
- Open the document **per tool call**, not cached across calls. Each MCP tool call is independent and short-lived. Caching introduces lifecycle complexity and file-locking issues.
- Use **`document.GetPage(n)`** for single-page access (1-based index), not `document.GetPages()` which iterates all pages.

### Text Extraction

- **Default to `page.GetWords()`** — returns `Word` objects with `Text`, `BoundingBox`, `FontName`, `FontSize`, and constituent letters. This is 5× smaller than letter-level data and sufficient for most layout analysis.
- Access letter-level data via `page.Letters` only when the agent explicitly requests `granularity = "letters"`.
- Each `Letter` exposes: `Value`, `Location` (point), `BoundingBox` (bounding box), `FontName`, `FontSize`, `PointSize`, `Color` (RGB).
- **Use `Letter.PointSize`, not `Letter.FontSize`, for font size.** `FontSize` returns the raw value from the PDF content stream, which is often `1.0` for embedded/subset fonts (the font matrix handles scaling). `PointSize` returns the actual rendered size in points (e.g., 9pt, 10pt). For Standard14 fonts (Helvetica, Times, Courier, etc.) both properties return the same value, but for real-world PDFs with embedded fonts — which are the common case — `FontSize` is unreliable.
- For complex layouts, consider `NearestNeighbourWordExtractor.Instance` passed to `page.GetWords()` — it handles irregular spacing better than the default extractor.

### Graphics Extraction

- Use **`page.Paths`** (`IReadOnlyList<PdfPath>`) to access pre-processed path data. PdfPig's internal `ContentStreamProcessor` handles CTM transforms, graphics state tracking (colors, line widths, dash patterns), and Form XObject recursion automatically.
- Each `PdfPath` exposes `IsFilled`, `IsStroked`, `IsClipping`, `FillColor`/`StrokeColor` (`IColor`), `LineWidth`, `LineDashPattern`, and a list of `PdfSubpath` objects containing `Move`, `Line`, `CubicBezierCurve`, `QuadraticBezierCurve`, and `Close` commands with `PdfPoint` coordinates already in page space.
- Convert colors via `IColor.ToRGBValues()` → `(double r, double g, double b)` in 0.0–1.0 range. Handle `PatternColor` gracefully (it throws `InvalidOperationException` from `ToRGBValues()`).
- Filter out clipping paths (`IsClipping == true`) and invisible paths (neither filled nor stroked).
- Classify each remaining `PdfPath` into **rectangles**, **lines**, and **complex paths** server-side. Return classified shapes, never raw operations.

### Image Extraction

Image extraction is handled by **PDFiumCore** (see Section 8), not PdfPig. PDFiumCore's `fpdf_edit` API provides per-object access to page content, enabling direct extraction of individual image objects as rendered bitmaps — regardless of source encoding — without the limitations of PdfPig's `TryGetPng()`.

---

## 8. PDFiumCore (PDF Rendering & Image Extraction) Best Practices

PDFiumCore (`PDFiumCore` NuGet package) is a .NET Standard 2.1 wrapper around Google's PDFium library. It provides the full PDFium C API surface, including page rendering (`fpdf_view`), page object manipulation (`fpdf_edit`), and text extraction (`fpdf_text`). This project uses it for **page rendering** (REQ-5) and **embedded image extraction** (REQ-4).

### Library Lifecycle

```csharp
// Call once at application startup (e.g., in a hosted service or static initializer)
fpdfview.FPDF_InitLibrary();

// Call at shutdown
fpdfview.FPDF_DestroyLibrary();
```

### Page Rendering

```csharp
using PDFiumCore;

var document = fpdfview.FPDF_LoadDocument(pdfPath, null);
var page = fpdfview.FPDF_LoadPage(document, pageNumber - 1); // 0-based

float scale = (float)(dpi / 72.0);
double pageWidth = 0, pageHeight = 0;
fpdfview.FPDF_GetPageSizeByIndex(document, pageNumber - 1, ref pageWidth, ref pageHeight);
int width = (int)(pageWidth * scale);
int height = (int)(pageHeight * scale);

var bitmap = fpdfview.FPDFBitmapCreateEx(width, height, (int)FPDFBitmapFormat.BGRA, IntPtr.Zero, 0);
fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF); // white background

using var matrix = new FS_MATRIX_();
using var clipping = new FS_RECTF_();
matrix.A = scale; matrix.D = scale;
clipping.Right = width; clipping.Top = height;

fpdfview.FPDF_RenderPageBitmapWithMatrix(bitmap, page, matrix, clipping, 0);

// Access raw BGRA pixel data
IntPtr buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
int stride = fpdfview.FPDFBitmapGetStride(bitmap);
byte[] rawBytes = new byte[stride * height];
Marshal.Copy(buffer, rawBytes, 0, rawBytes.Length);

// Cleanup
fpdfview.FPDFBitmapDestroy(bitmap);
fpdfview.FPDF_ClosePage(page);
fpdfview.FPDF_CloseDocument(document);
```

### Image Extraction

```csharp
using PDFiumCore;

var document = fpdfview.FPDF_LoadDocument(pdfPath, null);
var page = fpdfview.FPDF_LoadPage(document, pageNumber - 1);

int objCount = fpdf_edit.FPDFPage_CountObjects(page);
for (int i = 0; i < objCount; i++)
{
    var obj = fpdf_edit.FPDFPage_GetObject(page, i);
    if (fpdf_edit.FPDFPageObj_GetType(obj) != 3) // FPDF_PAGEOBJ_IMAGE = 3
        continue;

    // Get bounding box
    float left = 0, bottom = 0, right = 0, top = 0;
    fpdf_edit.FPDFPageObj_GetBounds(obj, ref left, ref bottom, ref right, ref top);

    // Get image metadata
    var metadata = new FPDF_IMAGEOBJ_METADATA();
    fpdf_edit.FPDFImageObj_GetImageMetadata(obj, page, ref metadata);
    // metadata.width, metadata.height, metadata.bits_per_pixel, metadata.colorspace

    // Render image object to bitmap (with mask + matrix applied)
    var imgBitmap = fpdf_edit.FPDFImageObj_GetRenderedBitmap(document, page, obj);
    // Extract pixel data via FPDFBitmapGetBuffer, encode to PNG, write to disk
    fpdfview.FPDFBitmapDestroy(imgBitmap);
}

fpdfview.FPDF_ClosePage(page);
fpdfview.FPDF_CloseDocument(document);
```

### Key Rules

- PDFium page numbers are **0-based** — subtract 1 from the user-facing 1-based page number.
- **`FPDF_InitLibrary()`** must be called once before any PDFium operations. Call `FPDF_DestroyLibrary()` at application shutdown.
- Rendering output is raw **BGRA pixel data** (4 bytes per pixel), not an encoded image. You must encode to PNG or JPEG before returning to the agent.
- For **PNG encoding**, use a lightweight manual PNG writer using `System.IO.Compression.ZLibStream` (built into .NET 6+). No external imaging library needed.
- For **JPEG encoding**, use **SkiaSharp** (`SKImage.Encode(SKEncodedImageFormat.Jpeg, quality)`). SkiaSharp wraps libjpeg-turbo for high-quality compression with a direct 1–100 quality mapping. See ADR-0004.
- For page rendering, composite the BGRA alpha channel against a white background before encoding, producing opaque output. This matches standard PDF viewer behavior.
- For image extraction, preserve the alpha channel as-is — extracted images may contain transparency. Do not composite against white.
- For page rendering, use `FPDF_RenderPageBitmapWithMatrix()` with a scaling matrix where `scalingFactor = dpi / 72.0`. Default to 150 DPI — a good balance between visual clarity and data size. A typical US Letter page at 150 DPI produces a ~1275×1650 pixel image.
- For image extraction, use `FPDFImageObj_GetRenderedBitmap(document, page, imageObject)` to render each image object individually. This produces a clean bitmap with the image's mask and transformation matrix applied, at the image's native resolution. No full-page render-and-crop fallback is needed.
- PDFiumCore bundles **platform-specific PDFium native binaries** via NuGet for Windows (x64, x86), Linux (x64), and macOS (x64). These are auto-selected at runtime.
- All PDFium handles (`FpdfDocumentT`, `FpdfPageT`, `FpdfBitmapT`) must be explicitly released via their corresponding close/destroy functions. Use `try/finally` to ensure cleanup.
- PDFiumCore operates independently from PdfPig. Each library opens the PDF file separately, which is fine for short-lived tool calls.
- **PDFium is not thread-safe.** The underlying PDFium native library does not support concurrent calls from multiple threads. Use a `static SemaphoreSlim(1, 1)` in a shared PDFiumCore service to serialize all calls. Accept `CancellationToken` in service methods so that callers queued behind the semaphore can be cancelled by the MCP client.
- **Shared PDFiumCore service.** Both page rendering and image extraction depend on PDFiumCore. Encapsulate the library lifecycle (`FPDF_InitLibrary` / `FPDF_DestroyLibrary`), the serialization semaphore, and document/page loading with consistent error handling in a single shared service. The rendering service and image extraction service consume this shared service rather than interacting with PDFiumCore directly. This avoids duplicating semaphore management, lifecycle code, and error handling across features.

---

## 9. Serialization Best Practices (System.Text.Json)

### Shared Serializer Options

Define a single `JsonSerializerOptions` instance and reuse it:

```csharp
public static class SerializerConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
```

### Rules

- **camelCase** property names — matches JSON conventions and is LLM-friendly.
- **Omit nulls** — reduces payload size; the agent can distinguish "absent" from "present."
- **No indentation** — saves bytes on large responses. Agents don't need pretty-printed JSON.
- **Round coordinates** to 1 decimal place (0.1 PDF points). Full double precision produces 15+ digits per coordinate, inflating payloads substantially on dense pages.
- **Hex color strings** — use `"#RRGGBB"` format (e.g., `"#FF0000"`), not separate R/G/B properties.
- **Base64 image data** — only include when explicitly requested via a parameter.
- **Source generators** — consider `[JsonSerializable(typeof(MyDto))]` for AOT compatibility and reduced allocations on hot paths.
- **File-based output** — when a tool supports an `outputFile` parameter, write the element data as CSV to disk (header row + data rows, ~50% fewer tokens than JSON) and return a compact summary DTO inline (< 1 KB). The summary contains the envelope data (page, width, height) so it is not repeated in the CSV. Validate the output path: must be absolute, must not contain `..`, and the parent directory must exist. See ADR-0005.

### DTO Design

```csharp
public record WordDto(
    string Text,
    double X,
    double Y,
    double W,
    double H,
    string Font,
    double Size,
    string? Color,
    bool? Bold,
    bool? Italic);
```

- Use `record` types for immutable, concise DTOs.
- Use nullable properties for optional fields so they are omitted from JSON when null.
- Keep DTOs flat — avoid deep nesting that inflates payload size and complicates agent parsing.

---

## 10. Testing Guidelines

### Strategy

- **Unit tests** for services (PDF extraction logic, graphics classification, serialization).
- **Integration tests** for tool methods using small sample PDF files.
- Keep sample PDFs in `tests/TestData/` — include PDFs with tables, multi-column layouts, images, and colored text.
- Use `xUnit` as the test framework (standard for .NET).

### What to Test

- Correct extraction of text with font/color metadata from a known PDF.
- Path classification correctly identifies rectangles, lines, and complex paths from `page.Paths`.
- Page number validation (0, negative, beyond page count).
- Invalid/missing file path handling.
- Response size stays within expected bounds for representative pages.
- PDFiumCore rendering produces valid PNG bytes.
- PDFiumCore image extraction produces correct per-image bitmaps.
- Serialization output matches expected JSON structure.

---

## 11. Performance Considerations

- **Per-page processing** — never iterate all pages when only one is requested. Use `document.GetPage(n)` not `document.GetPages().ElementAt(n)`.
- **Word-level default** — `page.GetWords()` is ~5× less data than `page.Letters` and sufficient for most structural analysis.
- **Avoid large allocations** — for image data, use `Span<byte>` / `Memory<byte>` where possible. Base64-encode directly to the output rather than building intermediate strings.
- **Reuse `JsonSerializerOptions`** — creating new options per call forces recomputation of internal caches.
- **Dispose native resources** — both PdfPig and PDFiumCore hold unmanaged resources. Always use `using` or `try/finally` to release handles.

---

## 12. Security Considerations

- **Path validation** — verify that `pdfPath` parameters point to existing files. Do not construct paths from user input without validation. Reject paths containing traversal sequences.
- **No arbitrary code execution** — the server only reads PDF files. No shell commands, no file writes.
- **Error message sanitization** — do not leak internal file paths, stack traces, or system information in tool error responses.
- **Resource limits** — very large PDFs or pages with thousands of graphics elements could produce oversized responses. Consider response size caps as a safeguard.
- **Native dependency trust** — PDFiumCore's PDFium binaries come from the official NuGet package. Do not substitute with untrusted binaries.
