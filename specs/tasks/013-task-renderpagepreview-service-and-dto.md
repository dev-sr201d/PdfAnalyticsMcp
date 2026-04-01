# Task 013: RenderPagePreview Service and DTO

## Description

Create the data transfer objects and rendering service for the `RenderPagePreview` tool (FRD-005). The service uses Docnet's scaling factor API to render a single PDF page at a configurable DPI, then encodes the raw BGRA pixel output to either PNG or JPEG depending on the requested format. PNG encoding uses the encoder from Task 012; JPEG encoding uses the SkiaSharp-based encoder from Task 012a. The service returns a result DTO containing the encoded image bytes, MIME type, and rendering metadata. This service operates independently of PdfPig.

The service also exposes a raw BGRA buffer API (`RenderRawAsync`) that returns the unencoded pixel buffer for internal use by other features (e.g., the render-based image extraction fallback in FRD-006 / Task 015). Both methods share the same semaphore, validation, and error handling.

Because Docnet wraps the native PDFium library which is **not thread-safe**, the service must serialize all access to `DocLib.Instance` using a static `SemaphoreSlim(1, 1)`. The rendering method must accept a `CancellationToken` so that callers queued behind the semaphore can be cancelled by the MCP client.

## Traces To

- **FRD:** FRD-005 (Page Rendering — RenderPagePreview), Functional Requirements 1–27
- **PRD:** REQ-5 (Page rendering), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling), REQ-10 (Concurrent tool safety)
- **ADRs:** ADR-0004 (PDF Page Rendering and Image Encoding)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)
- **Task 012** — BGRA-to-PNG encoder utility (must be complete before this task)
- **Task 012a** — BGRA-to-JPEG encoder utility using SkiaSharp (must be complete before this task)

## Technical Requirements

### NuGet Dependency

Add `Docnet.Core` to the main project's `.csproj` (if not already present):
```xml
<PackageReference Include="Docnet.Core" Version="2.*" />
```

The `SkiaSharp` package is already added by Task 012a.

This introduces native PDFium binaries (bundled per platform in the NuGet package). No manual configuration is required.

### DTO

Define response DTOs in the `Models/` directory as immutable `record` types:

**Render page preview result** — Contains all data needed by the tool layer to construct MCP content blocks:
- `page` (int) — The 1-based page number rendered
- `dpi` (int) — The DPI value used for rendering
- `format` (string) — The normalized format name: `"png"` or `"jpeg"`
- `quality` (int) — The quality value used
- `width` (int) — Rendered image width in pixels
- `height` (int) — Rendered image height in pixels
- `imageData` (byte[]) — The rendered page encoded in the requested format (PNG or JPEG)
- `mimeType` (string) — The MIME type of the encoded image (`"image/png"` or `"image/jpeg"`)

> **Note:** This DTO is not serialized to JSON directly. The tool layer uses it to construct an `ImageContentBlock` (from `imageData` with `mimeType`) and a `TextContentBlock` (from the metadata fields). The `imageData` field is never included in JSON output.

**Raw render result** — Contains the unencoded pixel buffer for internal use by other features (FRD-005 requirements 14–16):
- `width` (int) — Rendered image width in pixels
- `height` (int) — Rendered image height in pixels
- `bgraData` (byte[]) — Raw BGRA pixel data as returned by Docnet (transparent alpha intact; caller is responsible for compositing and encoding)

> **Note:** This DTO is internal to the service layer. The raw BGRA buffer is consumed by the image extraction fallback (Task 015), not by the MCP tool directly.

### Service Interface

Define a service interface `IRenderPagePreviewService` in `Services/` with two methods:

**`RenderAsync`** (primary — used by the MCP tool):
- Accepts a file path (string), a 1-based page number (int), a DPI value (int), a format (string), a quality (int), and a `CancellationToken`
- Returns the render page preview result DTO (`Task<RenderPagePreviewResult>`)
- Throws `ArgumentException` for validation failures (invalid page, invalid DPI, invalid format, invalid quality, unopenable file)
- Throws `OperationCanceledException` if the cancellation token is triggered while waiting for the semaphore

**`RenderRawAsync`** (internal — used by image extraction fallback, FRD-005 requirement 24):
- Accepts the same core parameters as before: file path (string), 1-based page number (int), DPI value (int), and `CancellationToken`
- Does **not** accept `format` or `quality` — it returns raw pixel data, not encoded images (FRD-005 requirement 20)
- Returns the raw render result DTO (`Task<RenderRawResult>`) containing the BGRA pixel buffer, width, and height — **not** encoded
- Uses the same semaphore, validation, Docnet lifecycle, and error handling as `RenderAsync` (FRD-005 requirement 25)
- Returns the raw BGRA buffer as-is from Docnet with transparent alpha intact (FRD-005 requirement 26)

### Service Implementation — `RenderPagePreviewService`

The service must:

1. **Validate DPI** — Reject values outside the range 72–600 with a clear `ArgumentException` message (e.g., "DPI must be between 72 and 600."). This validation can occur before acquiring the semaphore.
2. **Validate format** — Normalize the format string to lowercase. Accept `"png"`, `"jpeg"`, and `"jpg"` (case-insensitive). Treat `"jpg"` as `"jpeg"`. Reject any other value with a clear `ArgumentException` message listing the valid options (e.g., "Format must be 'png', 'jpeg', or 'jpg'."). This validation can occur before acquiring the semaphore.
3. **Validate quality** — Reject values outside the range 1–100 with a clear `ArgumentException` message (e.g., "Quality must be between 1 and 100."). This validation can occur before acquiring the semaphore.
4. **Acquire the rendering semaphore** — Use a `private static readonly SemaphoreSlim _renderSemaphore = new(1, 1)` to serialize all Docnet/PDFium operations. Call `await _renderSemaphore.WaitAsync(cancellationToken)` before any Docnet API calls. Release the semaphore in a `finally` block to ensure it is always released, even on exceptions.
5. **Open the PDF using Docnet's scaling factor API:**
   ```
   DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor))
   ```
   where `scalingFactor = dpi / 72.0`. The `PageDimensions(double scalingFactor)` constructor accepts a scaling factor (1.0 = 72 DPI). This renders the page at the requested DPI without needing to know the intrinsic page dimensions upfront.
   - If Docnet cannot open the file, catch the exception and throw `ArgumentException` with a message indicating the file could not be rendered as a PDF.
   - Dispose `IDocReader` via `using` declaration.
6. **Validate the page number** using `IInputValidationService.ValidatePageNumber(page, pageCount)`. Obtain the page count from `docReader.GetPageCount()`.
7. **Get the page reader** using `docReader.GetPageReader(page - 1)` (Docnet uses 0-based indexing).
   - Dispose `IPageReader` via `using` declaration.
8. **Retrieve rendered pixel dimensions** from `pageReader.GetPageWidth()` and `pageReader.GetPageHeight()`.
9. **Get the raw BGRA pixel data** from `pageReader.GetImage()`.
10. **Validate the pixel data** — If `GetImage()` returns null or an empty array, throw `ArgumentException` indicating the page could not be rendered.
11. **Encode to the requested format:**
    - If format is `"png"`: encode using `PngEncoder.Encode(bgraData, width, height)` from Task 012. Quality is ignored for PNG (FRD-005 requirement 14).
    - If format is `"jpeg"`: encode using `JpegEncoder.Encode(bgraData, width, height, quality)` from Task 012a. Quality directly controls JPEG compression.
12. **Release the semaphore** — In the `finally` block after all Docnet operations complete.
13. **Return the result DTO** with page number, DPI, format (normalized), quality, pixel dimensions, encoded image bytes, and MIME type (`"image/png"` or `"image/jpeg"`).

### `RenderRawAsync` Implementation

This method shares the same semaphore, validation, and Docnet lifecycle as `RenderAsync` (FRD-005 requirement 25). The implementation may either:
- Duplicate the rendering pipeline (validate → acquire semaphore → open doc → get page → get image → release semaphore) and return the raw BGRA buffer plus dimensions without calling `PngEncoder.Encode`, or
- Extract the shared rendering pipeline into a private helper method that both `RenderAsync` and `RenderRawAsync` delegate to, with `RenderAsync` additionally encoding to PNG.

Either approach is acceptable as long as both methods share the same semaphore and error handling (FRD-005 requirement 27).

The raw BGRA buffer is returned as-is from Docnet with the transparent alpha channel intact (FRD-005 requirement 26). The caller (image extraction service) is responsible for any alpha compositing and PNG encoding of cropped regions.

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

- [ ] `Docnet.Core` NuGet package is added to the main project (if not already present).
- [ ] The service renders a known test PDF page at 150 DPI with default format (PNG) and returns image data that starts with the PNG signature.
- [ ] The service renders a known test PDF page at 150 DPI with format `"jpeg"` and returns image data that starts with the JPEG SOI marker `[0xFF, 0xD8]`.
- [ ] The service correctly reports pixel dimensions matching the expected values for the test PDF at the requested DPI (e.g., a 612×792 point page at 150 DPI should produce approximately 1275×1650 pixels).
- [ ] The service renders correctly at DPI 72 (minimum) and DPI 300, producing proportionally different pixel dimensions.
- [ ] The service throws `ArgumentException` for DPI below 72 or above 600.
- [ ] The service throws `ArgumentException` for invalid format values (e.g., `"bmp"`, `"gif"`).
- [ ] The service accepts format values case-insensitively (`"PNG"`, `"Jpeg"`, `"JPG"` all succeed).
- [ ] The service normalizes `"jpg"` to `"jpeg"` in the result DTO.
- [ ] The service throws `ArgumentException` for quality below 1 or above 100.
- [ ] The service throws `ArgumentException` for page number 0, negative values, or values beyond the document's page count.
- [ ] The service throws `ArgumentException` when the file cannot be opened by Docnet.
- [ ] The service correctly translates 1-based page numbers to Docnet's 0-based indexing (page 1 → index 0, page 2 → index 1).
- [ ] Native resources (`IDocReader`, `IPageReader`) are properly disposed on both success and failure paths.
- [ ] All Docnet/PDFium operations are serialized through a static `SemaphoreSlim(1, 1)`, acquired before any `DocLib.Instance` call and released in a `finally` block.
- [ ] The rendering method accepts a `CancellationToken` that is passed to `SemaphoreSlim.WaitAsync()`, allowing queued callers to be cancelled.
- [ ] Fast validations (DPI range, format, quality, file path existence) execute before acquiring the semaphore.
- [ ] The result DTO includes the correct MIME type: `"image/png"` for PNG, `"image/jpeg"` for JPEG.
- [ ] The result DTO includes the encoded image size (`imageData.Length`).
- [ ] JPEG quality parameter affects output: quality=100 produces a larger file than quality=10 for the same page.
- [ ] PNG output ignores the quality parameter (quality=10 and quality=100 produce identical PNG output).
- [ ] `RenderRawAsync` returns a raw BGRA pixel buffer with the correct width, height, and buffer length (`width × height × 4`).
- [ ] `RenderRawAsync` uses the same static semaphore as `RenderAsync`.
- [ ] `RenderRawAsync` applies the same validation (DPI range, file path, page number) as `RenderAsync`.
- [ ] `RenderRawAsync` does not accept format or quality parameters.
- [ ] `RenderRawAsync` does not call `PngEncoder.Encode` or `JpegEncoder.Encode` — it returns the raw Docnet output.
- [ ] `RenderRawAsync` returns the BGRA buffer with the transparent alpha channel intact (not composited against white).
- [ ] The service is registered in `Program.cs` DI container.

## Testing Requirements

Unit tests must validate the service's rendering, validation, and error handling behavior.

### Required Unit Test Scenarios

1. **Render at default DPI as PNG** — Call the service on a known test PDF (e.g., `sample-with-metadata.pdf`, page 1) at 150 DPI with format `"png"`. Verify the returned DTO contains: page=1, dpi=150, format="png", non-zero width and height, mimeType="image/png", and image data starting with the PNG signature.
2. **Render as JPEG** — Call the service at 150 DPI with format `"jpeg"`. Verify the returned DTO contains: format="jpeg", mimeType="image/jpeg", and image data starting with the JPEG SOI marker `[0xFF, 0xD8]`.
3. **Format "jpg" alias** — Call with format `"jpg"`. Verify the returned DTO has format="jpeg" (normalized) and mimeType="image/jpeg".
4. **Format case-insensitivity** — Call with format `"PNG"`, `"Jpeg"`, `"JPG"`. Verify all succeed.
5. **Invalid format** — Call with format `"bmp"`. Verify `ArgumentException` is thrown with a message listing valid options.
6. **JPEG quality affects size** — Render the same page as JPEG at quality=10 and quality=100. Verify the quality=100 output is larger.
7. **PNG ignores quality** — Render the same page as PNG at quality=10 and quality=100. Verify both produce identical output (same byte array).
8. **Quality at boundaries** — Call with quality=1 and quality=100. Verify both succeed.
9. **Quality below range** — Call with quality=0. Verify `ArgumentException` is thrown.
10. **Quality above range** — Call with quality=101. Verify `ArgumentException` is thrown.
11. **Pixel dimensions at 150 DPI** — Render a US Letter page (612×792 pts) at 150 DPI. Verify the width and height in the result are approximately 1275 and 1650 (within ±5 pixels to account for Docnet's aspect-ratio-aware scaling).
12. **Pixel dimensions at 72 DPI** — Render the same page at 72 DPI. Verify the dimensions are approximately 612×792 pixels. Confirm they are smaller than the 150 DPI result.
13. **Pixel dimensions at 300 DPI** — Render the same page at 300 DPI. Verify the dimensions are approximately 2550×3300 pixels. Confirm they are larger than the 150 DPI result.
14. **Page 2 access** — Render page 2 of a multi-page test PDF. Verify the result contains page=2 and valid image data.
15. **DPI too low** — Call with DPI=50. Verify `ArgumentException` is thrown with a message mentioning the valid range.
16. **DPI too high** — Call with DPI=700. Verify `ArgumentException` is thrown.
17. **DPI at boundary (72)** — Call with DPI=72. Verify it succeeds (boundary is inclusive).
18. **DPI at boundary (600)** — Call with DPI=600. Verify it succeeds.
19. **Page number zero** — Call with page=0. Verify `ArgumentException` is thrown.
20. **Page beyond count** — Call with a page number beyond the document's page count. Verify `ArgumentException` is thrown.
21. **Invalid PDF file** — Call with `not-a-pdf.txt`. Verify `ArgumentException` is thrown with a message about the file not being renderable.
22. **Image data validity (PNG)** — Verify the returned PNG data contains a valid IHDR chunk whose width and height match the DTO's width and height fields.
23. **Result DTO sizeBytes** — Verify the `imageData.Length` in the result matches reasonable expectations (positive integer, JPEG smaller than PNG for the same page).
24. **RenderRawAsync returns raw BGRA** — Call `RenderRawAsync` on a known test PDF at 150 DPI. Verify the returned buffer length equals `width × height × 4` and that the width and height are plausible for the test PDF at that DPI.
25. **RenderRawAsync validation** — Call `RenderRawAsync` with DPI=50 (below range). Verify `ArgumentException` is thrown. Call with page=0. Verify `ArgumentException` is thrown. This confirms the raw method shares the same validation.
26. **RenderRawAsync does not encode** — Call `RenderRawAsync` and verify the returned `bgraData` does **not** start with the PNG signature bytes or JPEG SOI marker. This confirms raw buffer output.
