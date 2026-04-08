# Task 015: RenderPagePreview Service and DTO

## Description

Create the data transfer objects and rendering service for the `RenderPagePreview` tool (FRD-005). The service delegates to the shared `IPdfiumService` (Task 014) for serialized document/page access, then uses PDFiumCore's `FPDF_RenderPageBitmapWithMatrix()` to render a single PDF page at a configurable DPI. The raw BGRA pixel output is encoded to either PNG or JPEG depending on the requested format. PNG encoding uses the encoder from Task 012; JPEG encoding uses the SkiaSharp-based encoder from Task 013. The service returns a result DTO containing the encoded image bytes, MIME type, and rendering metadata.

This service operates independently of PdfPig. All PDFiumCore operations (document loading, page access, rendering) are serialized through the shared `IPdfiumService` semaphore, ensuring thread safety.

## Traces To

- **FRD:** FRD-005 (Page Rendering — RenderPagePreview), Functional Requirements 1–19
- **PRD:** REQ-5 (Page rendering), REQ-6 (Page-by-page processing), REQ-7 (Robust error handling), REQ-9 (Concurrent tool safety)
- **ADRs:** ADR-0004 (PDFiumCore — Rendering, Thread Safety, Shared PDFiumCore Service)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)
- **Task 012** — BGRA-to-PNG encoder utility (must be complete before this task)
- **Task 013** — BGRA-to-JPEG encoder utility using SkiaSharp (must be complete before this task)
- **Task 014** — Shared PDFiumCore service: `IPdfiumService` (must be complete before this task)

## Technical Requirements

### NuGet Dependencies

The `PDFiumCore` and `SkiaSharp` packages are already added by Tasks 014 and 013 respectively. No additional NuGet packages are needed for this task.

### DTO

Define a response DTO in the `Models/` directory as an immutable `record` type:

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

### Service Interface

Define a service interface `IRenderPagePreviewService` in `Services/` with a single method:

**`RenderAsync`**:
- Accepts a file path (string), a 1-based page number (int), a DPI value (int), a format (string), a quality (int), and a `CancellationToken`
- Returns the render page preview result DTO (`Task<RenderPagePreviewResult>`)
- Throws `ArgumentException` for validation failures — both parameter validation (invalid DPI, invalid format, invalid quality) and failures propagated from `IPdfiumService` (invalid page, unopenable file)
- Throws `OperationCanceledException` if the cancellation token is triggered while waiting for the semaphore

### Service Implementation — `RenderPagePreviewService`

The service must:

1. **Validate DPI** — Reject values outside the range 72–600 with a clear `ArgumentException` message (e.g., `"DPI must be between 72 and 600."`). This validation occurs before calling `IPdfiumService`, avoiding unnecessary semaphore acquisition.
2. **Validate format** — Normalize the format string to lowercase. Accept `"png"`, `"jpeg"`, and `"jpg"` (case-insensitive). Treat `"jpg"` as `"jpeg"`. Reject any other value with a clear `ArgumentException` message listing the valid options (e.g., `"Format must be 'png', 'jpeg', or 'jpg'."`). This validation occurs before calling `IPdfiumService`.
3. **Validate quality** — Reject values outside the range 1–100 with a clear `ArgumentException` message (e.g., `"Quality must be between 1 and 100."`). This validation occurs before calling `IPdfiumService`.
4. **Delegate to `IPdfiumService.ExecuteAsync`** — Pass the file path, page number, cancellation token, and a callback that performs the rendering. The shared service handles semaphore acquisition, document loading, page loading, page number validation, error handling, and native resource cleanup.
5. **Inside the callback — render the page:**
   - Compute the scaling factor: `float scale = (float)(dpi / 72.0)`
   - Get page dimensions: use `IPdfiumService.GetPageSize(document, pageIndex)` to get `pageWidth` and `pageHeight` in PDF points
   - Compute pixel dimensions: `int width = (int)(pageWidth * scale)`, `int height = (int)(pageHeight * scale)`
   - Create a bitmap: `fpdfview.FPDFBitmapCreateEx(width, height, (int)FPDFBitmapFormat.BGRA, IntPtr.Zero, 0)`
   - Fill with white background: `fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF)`
   - Set up the scaling matrix and clipping rectangle:
     ```
     var matrix = new FS_MATRIX_() { A = scale, D = scale };
     var clipping = new FS_RECTF_() { Right = width, Top = height };
     ```
   - Render: `fpdfview.FPDF_RenderPageBitmapWithMatrix(bitmap, page, matrix, clipping, 0)`
   - Extract raw BGRA pixel data: get the buffer pointer via `fpdfview.FPDFBitmapGetBuffer(bitmap)` and the stride via `fpdfview.FPDFBitmapGetStride(bitmap)`. If `stride == width * 4`, copy the entire buffer in one `Marshal.Copy` call (`width * height * 4` bytes). If `stride > width * 4` (due to platform-specific row alignment padding), copy row-by-row into a contiguous `byte[width * height * 4]` array — for each row, copy `width * 4` bytes from offset `row * stride` in the source buffer. The PngEncoder and JpegEncoder expect contiguous pixel data with no padding between rows.
   - Destroy the bitmap: `fpdfview.FPDFBitmapDestroy(bitmap)` in a `try/finally` block
   - Validate the pixel data — if the bitmap buffer is `IntPtr.Zero` or the byte array is empty, throw `InvalidOperationException` indicating the page could not be rendered
6. **Encode to the requested format** (outside the callback, after native resources are released):
   - If format is `"png"`: encode using `PngEncoder.Encode(bgraData, width, height, preserveAlpha: false)` from Task 012. The `preserveAlpha: false` flag composites against white, producing opaque RGB output (FRD-005 requirement 6). Quality is ignored for PNG (FRD-005 requirement 14).
   - If format is `"jpeg"`: encode using `JpegEncoder.Encode(bgraData, width, height, quality)` from Task 013. Quality directly controls JPEG compression.
7. **Return the result DTO** with page number, DPI, format (normalized), quality, pixel dimensions, encoded image bytes, and MIME type (`"image/png"` or `"image/jpeg"`).

> **Note on bitmap handle cleanup:** The `FpdfBitmapT` handle created for rendering is owned by this service's callback, not by `IPdfiumService`. It must be destroyed via `FPDFBitmapDestroy` in a `try/finally` within the callback, before the callback returns. The document and page handles are managed by `IPdfiumService`.

> **Note on encoding location:** Encoding (step 6) can be performed either inside or outside the `IPdfiumService` callback. Performing it outside the callback is preferred — this releases the PDFium semaphore sooner, allowing other callers to proceed while encoding runs. However, this requires the callback to return the raw BGRA data plus dimensions, which the service then encodes. Either approach is acceptable.

### PDFiumCore Rendering API Reference

| API | Description |
|-----|-------------|
| `fpdfview.FPDFBitmapCreateEx(width, height, format, buffer, stride)` | Creates a bitmap. Pass `IntPtr.Zero` for buffer and `0` for stride to allocate internally. Format `(int)FPDFBitmapFormat.BGRA` = 4. |
| `fpdfview.FPDFBitmapFillRect(bitmap, left, top, width, height, color)` | Fills a rectangle. Color `0xFFFFFFFF` = opaque white (ARGB). |
| `fpdfview.FPDF_RenderPageBitmapWithMatrix(bitmap, page, matrix, clipping, flags)` | Renders a page using a transformation matrix and clipping rectangle. Flags `0` for default rendering. |
| `fpdfview.FPDFBitmapGetBuffer(bitmap)` | Returns `IntPtr` to the raw pixel data. |
| `fpdfview.FPDFBitmapGetStride(bitmap)` | Returns the stride (bytes per row) of the bitmap. |
| `fpdfview.FPDFBitmapDestroy(bitmap)` | Releases the bitmap. Must be called after pixel data is copied. |

### DI Registration

Register the service in `Program.cs`:
- `IRenderPagePreviewService` → `RenderPagePreviewService` as `AddSingleton` (consistent with all other services; the service holds no mutable state and delegates native resource management to `IPdfiumService`).

### Test Data

Reuse existing test PDFs from `tests/TestData/` for rendering tests. The following existing PDFs are suitable:
- `sample-with-metadata.pdf` — 2-page PDF with known dimensions (612×792 points = US Letter)
- `sample-text.pdf` — 1-page PDF with text content
- `sample-blank.pdf` — Empty page for edge case testing
- `not-a-pdf.txt` — Invalid file for error handling tests

No new test PDF generation is required.

## Acceptance Criteria

- [ ] The service renders a known test PDF page at 150 DPI with default format (JPEG) and returns image data that starts with the JPEG SOI marker `[0xFF, 0xD8]`.
- [ ] The service renders a known test PDF page at 150 DPI with format `"png"` and returns image data that starts with the PNG signature.
- [ ] The service correctly reports pixel dimensions matching the expected values for the test PDF at the requested DPI (e.g., a 612×792 point page at 150 DPI should produce 1275×1650 pixels).
- [ ] The service renders correctly at DPI 72 (minimum) and DPI 300, producing proportionally different pixel dimensions.
- [ ] The service throws `ArgumentException` for DPI below 72 or above 600.
- [ ] The service throws `ArgumentException` for invalid format values (e.g., `"bmp"`, `"gif"`).
- [ ] The service accepts format values case-insensitively (`"PNG"`, `"Jpeg"`, `"JPG"` all succeed).
- [ ] The service normalizes `"jpg"` to `"jpeg"` in the result DTO.
- [ ] The service throws `ArgumentException` for quality below 1 or above 100.
- [ ] The service throws `ArgumentException` for page number 0, negative values, or values beyond the document's page count (via `IPdfiumService`).
- [ ] The service throws `ArgumentException` when the file cannot be opened as a PDF (via `IPdfiumService`).
- [ ] Fast validations (DPI range, format, quality) execute before calling `IPdfiumService` (before semaphore acquisition).
- [ ] The result DTO includes the correct MIME type: `"image/png"` for PNG, `"image/jpeg"` for JPEG.
- [ ] The result DTO includes the encoded image size (`imageData.Length`).
- [ ] JPEG quality parameter affects output: quality=100 produces a larger file than quality=10 for the same page.
- [ ] PNG output ignores the quality parameter (quality=10 and quality=100 produce identical PNG output).
- [ ] PNG encoding uses `preserveAlpha: false` — the output is composited against white (opaque RGB).
- [ ] The rendering bitmap handle (`FpdfBitmapT`) is destroyed via `FPDFBitmapDestroy` in a `try/finally` block within the callback.
- [ ] The service delegates all PDFiumCore document/page lifecycle and semaphore management to `IPdfiumService` — it does not create its own semaphore or call `FPDF_LoadDocument` / `FPDF_ClosePage` / `FPDF_CloseDocument` directly.
- [ ] The rendering method accepts a `CancellationToken` that is forwarded to `IPdfiumService` for semaphore cancellation.
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
11. **Pixel dimensions at 150 DPI** — Render a US Letter page (612×792 pts) at 150 DPI. Verify the width and height in the result are 1275 and 1650.
12. **Pixel dimensions at 72 DPI** — Render the same page at 72 DPI. Verify the dimensions are 612×792 pixels. Confirm they are smaller than the 150 DPI result.
13. **Pixel dimensions at 300 DPI** — Render the same page at 300 DPI. Verify the dimensions are 2550×3300 pixels. Confirm they are larger than the 150 DPI result.
14. **Page 2 access** — Render page 2 of a multi-page test PDF. Verify the result contains page=2 and valid image data.
15. **DPI too low** — Call with DPI=50. Verify `ArgumentException` is thrown with a message mentioning the valid range.
16. **DPI too high** — Call with DPI=700. Verify `ArgumentException` is thrown.
17. **DPI at boundary (72)** — Call with DPI=72. Verify it succeeds (boundary is inclusive).
18. **DPI at boundary (600)** — Call with DPI=600. Verify it succeeds.
19. **Page number zero** — Call with page=0. Verify `ArgumentException` is thrown.
20. **Page beyond count** — Call with a page number beyond the document's page count. Verify `ArgumentException` is thrown.
21. **Invalid PDF file** — Call with `not-a-pdf.txt`. Verify `ArgumentException` is thrown with a message about the file not being openable as a PDF.
22. **Image data validity (PNG)** — Verify the returned PNG data contains a valid IHDR chunk whose width and height match the DTO's width and height fields.
23. **Result DTO sizeBytes** — Verify the `imageData.Length` in the result matches reasonable expectations (positive integer, JPEG smaller than PNG for the same page).
