# Task 015: RenderPagePreview Service and DTO

## Description

Create the data transfer object and rendering service for the `RenderPagePreview` tool (FRD-006). The service uses Docnet's scaling factor API to render a single PDF page at a configurable DPI, then encodes the raw BGRA pixel output to PNG using the encoder from Task 014. The service returns a result DTO containing the rendered PNG bytes and rendering metadata. This service operates independently of PdfPig.

Because Docnet wraps the native PDFium library which is **not thread-safe**, the service must serialize all access to `DocLib.Instance` using a static `SemaphoreSlim(1, 1)`. The rendering method must accept a `CancellationToken` so that callers queued behind the semaphore can be cancelled by the MCP client.

## Traces To

- **FRD:** FRD-006 (Page Rendering Preview — RenderPagePreview)
- **PRD:** REQ-5 (Page rendering), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling), REQ-10 (Concurrent tool safety)
- **ADRs:** ADR-0004 (Docnet/PDF rendering)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)
- **Task 014** — BGRA-to-PNG encoder utility (must be complete before this task)

## Technical Requirements

### NuGet Dependency

Add `Docnet.Core` to the main project's `.csproj`:
```xml
<PackageReference Include="Docnet.Core" Version="2.*" />
```

This introduces native PDFium binaries (bundled per platform in the NuGet package). No manual configuration is required.

### DTO

Define a response DTO in the `Models/` directory as an immutable `record` type:

**Render page preview result** — Contains all data needed by the tool layer to construct MCP content blocks:
- `page` (int) — The 1-based page number rendered
- `dpi` (int) — The DPI value used for rendering
- `width` (int) — Rendered image width in pixels
- `height` (int) — Rendered image height in pixels
- `pngData` (byte[]) — The rendered page encoded as PNG

> **Note:** This DTO is not serialized to JSON directly. The tool layer uses it to construct an `ImageContentBlock` (from the PNG bytes) and a `TextContentBlock` (from the metadata fields). The `pngData` field is never included in JSON output.

### Service Interface

Define a service interface `IRenderPagePreviewService` in `Services/` with a method that:
- Accepts a file path (string), a 1-based page number (int), a DPI value (int), and a `CancellationToken`
- Returns the render page preview result DTO (can be `Task<RenderPagePreviewResult>` to support async semaphore waiting)
- Throws `ArgumentException` for validation failures (invalid page, invalid DPI, unopenable file)
- Throws `OperationCanceledException` if the cancellation token is triggered while waiting for the semaphore

### Service Implementation — `RenderPagePreviewService`

The service must:

1. **Validate DPI** — Reject values outside the range 72–600 with a clear `ArgumentException` message (e.g., "DPI must be between 72 and 600."). This validation can occur before acquiring the semaphore.
2. **Acquire the rendering semaphore** — Use a `private static readonly SemaphoreSlim _renderSemaphore = new(1, 1)` to serialize all Docnet/PDFium operations. Call `await _renderSemaphore.WaitAsync(cancellationToken)` before any Docnet API calls. Release the semaphore in a `finally` block to ensure it is always released, even on exceptions.
3. **Open the PDF using Docnet's scaling factor API:**
   ```
   DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor))
   ```
   where `scalingFactor = dpi / 72.0`. The `PageDimensions(double scalingFactor)` constructor accepts a scaling factor (1.0 = 72 DPI). This renders the page at the requested DPI without needing to know the intrinsic page dimensions upfront.
   - If Docnet cannot open the file, catch the exception and throw `ArgumentException` with a message indicating the file could not be rendered as a PDF.
   - Dispose `IDocReader` via `using` declaration.
4. **Validate the page number** using `IInputValidationService.ValidatePageNumber(page, pageCount)`. Obtain the page count from `docReader.GetPageCount()`.
5. **Get the page reader** using `docReader.GetPageReader(page - 1)` (Docnet uses 0-based indexing).
   - Dispose `IPageReader` via `using` declaration.
6. **Retrieve rendered pixel dimensions** from `pageReader.GetPageWidth()` and `pageReader.GetPageHeight()`.
7. **Get the raw BGRA pixel data** from `pageReader.GetImage()`.
8. **Validate the pixel data** — If `GetImage()` returns null or an empty array, throw `ArgumentException` indicating the page could not be rendered.
9. **Encode to PNG** using the BGRA-to-PNG encoder utility from Task 014, passing the raw pixel data, width, and height.
10. **Release the semaphore** — In the `finally` block after all Docnet operations complete.
11. **Return the result DTO** with page number, DPI, pixel dimensions, and encoded PNG bytes.

> **Concurrency note:** The static semaphore ensures that only one thread at a time can use PDFium's native library, preventing `AccessViolationException` and memory corruption. The semaphore is `static` because the constraint is process-wide (there is only one `DocLib.Instance`). Fast validations (DPI range, file path) should execute before the semaphore to avoid unnecessary queuing.

### Docnet API Reference

| API | Description |
|-----|-------------|
| `DocLib.Instance.GetDocReader(string filePath, PageDimensions dimensions)` | Opens a PDF for reading. Use `new PageDimensions(scalingFactor)` where `scalingFactor` of 1.0 = 72 DPI. Scaling factor `dpi / 72.0` renders at the desired DPI. Returns `IDocReader`. |
| `IDocReader.GetPageCount()` | Returns total number of pages in the document. |
| `IDocReader.GetPageReader(int pageIndex)` | Opens a specific page for reading. **0-based index.** Returns `IPageReader`. |
| `IPageReader.GetPageWidth()` | Returns the scaled page width in pixels. |
| `IPageReader.GetPageHeight()` | Returns the scaled page height in pixels. |
| `IPageReader.GetImage()` | Returns raw BGRA pixel data (4 bytes per pixel: Blue, Green, Red, Alpha). |

### DI Registration

Register the service in `Program.cs`:
- `IRenderPagePreviewService` → `RenderPagePreviewService` as `AddSingleton` (consistent with all other services; the service holds no state and opens/disposes native resources within each method call).

### Test Data

Reuse existing test PDFs from `tests/TestData/` for rendering tests. The following existing PDFs are suitable:
- `sample-with-metadata.pdf` — 2-page PDF with known dimensions (612×792 points = US Letter)
- `sample-text.pdf` — 1-page PDF with text content
- `sample-blank.pdf` — Empty page for edge case testing
- `not-a-pdf.txt` — Invalid file for error handling tests

No new test PDF generation is required.

## Acceptance Criteria

- [ ] `Docnet.Core` NuGet package is added to the main project.
- [ ] The service renders a known test PDF page at 150 DPI and returns PNG data that starts with the PNG signature.
- [ ] The service correctly reports pixel dimensions matching the expected values for the test PDF at the requested DPI (e.g., a 612×792 point page at 150 DPI should produce approximately 1275×1650 pixels).
- [ ] The service renders correctly at DPI 72 (minimum) and DPI 300, producing proportionally different pixel dimensions.
- [ ] The service throws `ArgumentException` for DPI below 72 or above 600.
- [ ] The service throws `ArgumentException` for page number 0, negative values, or values beyond the document's page count.
- [ ] The service throws `ArgumentException` when the file cannot be opened by Docnet.
- [ ] The service correctly translates 1-based page numbers to Docnet's 0-based indexing (page 1 → index 0, page 2 → index 1).
- [ ] Native resources (`IDocReader`, `IPageReader`) are properly disposed on both success and failure paths.
- [ ] All Docnet/PDFium operations are serialized through a static `SemaphoreSlim(1, 1)`, acquired before any `DocLib.Instance` call and released in a `finally` block.
- [ ] The rendering method accepts a `CancellationToken` that is passed to `SemaphoreSlim.WaitAsync()`, allowing queued callers to be cancelled.
- [ ] Fast validations (DPI range, file path existence) execute before acquiring the semaphore.
- [ ] The service is registered in `Program.cs` DI container.

## Testing Requirements

Unit tests must validate the service's rendering, validation, and error handling behavior.

### Required Unit Test Scenarios

1. **Render at default DPI** — Call the service on a known test PDF (e.g., `sample-with-metadata.pdf`, page 1) at 150 DPI. Verify the returned DTO contains: page=1, dpi=150, non-zero width and height, and PNG data starting with the PNG signature.
2. **Pixel dimensions at 150 DPI** — Render a US Letter page (612×792 pts) at 150 DPI. Verify the width and height in the result are approximately 1275 and 1650 (within ±5 pixels to account for Docnet's aspect-ratio-aware scaling).
3. **Pixel dimensions at 72 DPI** — Render the same page at 72 DPI. Verify the dimensions are approximately 612×792 pixels. Confirm they are smaller than the 150 DPI result.
4. **Pixel dimensions at 300 DPI** — Render the same page at 300 DPI. Verify the dimensions are approximately 2550×3300 pixels. Confirm they are larger than the 150 DPI result.
5. **Page 2 access** — Render page 2 of a multi-page test PDF. Verify the result contains page=2 and valid PNG data.
6. **DPI too low** — Call with DPI=50. Verify `ArgumentException` is thrown with a message mentioning the valid range.
7. **DPI too high** — Call with DPI=700. Verify `ArgumentException` is thrown.
8. **DPI at boundary (72)** — Call with DPI=72. Verify it succeeds (boundary is inclusive).
9. **DPI at boundary (600)** — Call with DPI=600. Verify it succeeds.
10. **Page number zero** — Call with page=0. Verify `ArgumentException` is thrown.
11. **Page beyond count** — Call with a page number beyond the document's page count. Verify `ArgumentException` is thrown.
12. **Invalid PDF file** — Call with `not-a-pdf.txt`. Verify `ArgumentException` is thrown with a message about the file not being renderable.
13. **PNG data validity** — Verify the returned PNG data contains a valid IHDR chunk whose width and height match the DTO's width and height fields.
