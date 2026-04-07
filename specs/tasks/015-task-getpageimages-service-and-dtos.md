# Task 015: GetPageImages Service and DTOs

## Description

Create the data transfer objects and extraction service for the `GetPageImages` tool (FRD-006). The service uses PDFiumCore's `fpdf_edit` API to enumerate embedded images on a single PDF page — including images nested inside Form XObjects — extracting each image's bounding box, pixel dimensions, and bits per pixel. When an output directory is provided, the service extracts each unique image to disk. For images encoded as pure JPEG (single `DCTDecode` filter), the raw JPEG bytes are extracted directly — avoiding lossy re-encoding and producing significantly smaller files. For all other encodings, the service renders to bitmap and encodes as PNG. The primary bitmap extraction method is `FPDFImageObj_GetRenderedBitmap()`, which produces clean per-image bitmaps with mask and transformation matrix applied. When `GetRenderedBitmap` returns null (which can happen for certain image objects that PDFium cannot render in context), the service falls back to `FPDFImageObj_GetBitmap()`, which returns the raw image data at native resolution without mask/matrix processing. The fallback bitmap may be in a different pixel format (BGR or grayscale instead of BGRA), so the service normalizes all formats to BGRA before PNG encoding. The service delegates all PDFiumCore document/page lifecycle and semaphore management to the shared `IPdfiumService` (Task 012b).

## Traces To

- **FRD:** FRD-006 (Page Image Extraction — GetPageImages), Functional Requirements 1–23
- **PRD:** REQ-4 (Image extraction), NFR-1 (Data volume management), REQ-6 (Page-by-page processing), REQ-7 (Robust error handling), REQ-9 (Concurrent tool safety)
- **ADRs:** ADR-0004 (PDFiumCore — Image Extraction, Thread Safety, Shared PDFiumCore Service), ADR-0005 (Serialization)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 004** — Shared serialization configuration: `SerializerConfig`, `FormatUtils` (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)
- **Task 012** — BGRA PNG encoder: `PngEncoder` (complete — needed for encoding extracted image bitmaps with `preserveAlpha: true`)
- **Task 012b** — Shared PDFiumCore service: `IPdfiumService` (complete — provides serialized document/page access)

## Technical Requirements

### DTOs

Define response DTOs in the `Models/` directory as immutable `record` types. All coordinates are in PDF points, rounded to 1 decimal place. Nullable fields are omitted from JSON when null (per `SerializerConfig`).

1. **Image element DTO** — Represents a single embedded image occurrence on the page:
   - `x` (double) — Left edge X coordinate on the page (PDF points)
   - `y` (double) — Bottom edge Y coordinate on the page (PDF points)
   - `w` (double) — Display width on the page (PDF points)
   - `h` (double) — Display height on the page (PDF points)
   - `pixelWidth` (int) — Image width in pixels (intrinsic resolution)
   - `pixelHeight` (int) — Image height in pixels (intrinsic resolution)
   - `bitsPerPixel` (int) — Bits per pixel as reported by PDFiumCore's `FPDF_IMAGEOBJ_METADATA.bits_per_pixel`
   - `file` (string?, nullable) — Absolute path to the extracted PNG file on disk (only when `outputPath` is provided and extraction succeeded)

2. **Page images response DTO** — Envelope for the full tool response:
   - `page` (int) — The 1-based page number returned
   - `width` (double) — Page width in PDF points, rounded to 1 decimal place
   - `height` (double) — Page height in PDF points, rounded to 1 decimal place
   - `images` (list of image element DTOs) — All embedded image occurrences found on the page (empty list if none; never null)

### Service Interface

Define a service interface `IPageImagesService` in `Services/` with a method that:
- Accepts a file path (string), a 1-based page number (int), an optional `outputPath` (string?), and a `CancellationToken`
- Returns the page images response DTO
- The `CancellationToken` is forwarded to `IPdfiumService` for semaphore cancellation

### Service Implementation — `PageImagesService`

The service must delegate to `IPdfiumService.ExecuteAsync` for all PDFiumCore operations. The callback receives document and page handles and performs image enumeration and extraction.

> **Note on callback synchronicity:** The `IPdfiumService.ExecuteAsync<T>` callback signature is `Func<FpdfDocumentT, FpdfPageT, T>` — a synchronous delegate. Image extraction performs synchronous file I/O (`File.WriteAllBytes`) inside this callback, which blocks the calling thread. This is acceptable because the semaphore already serializes all PDFiumCore operations to a single concurrent call, so there is no parallelism benefit from async I/O inside the callback. The semaphore-guarded section should complete as quickly as possible.

#### Output Path Validation (before calling `IPdfiumService`)

1. If `outputPath` is provided, validate it before calling `IPdfiumService` to avoid unnecessary semaphore acquisition:
   - If `outputPath` is not an absolute path: throw `ArgumentException` with `"outputPath must be an absolute path."`
   - If `outputPath` contains `..`: throw `ArgumentException` with `"Invalid output path."`
   - If the directory does not exist: throw `ArgumentException` with `"Output directory does not exist: {outputPath}"`

#### Inside the `IPdfiumService.ExecuteAsync` callback:

2. **Get page dimensions** — Use `IPdfiumService.GetPageSize(document, pageIndex)` to obtain page width and height in PDF points.

3. **Enumerate page objects recursively** — Implement a recursive traversal of the page object tree using depth-first pre-order:
   - Call `fpdf_edit.FPDFPage_CountObjects(page)` and iterate with `fpdf_edit.FPDFPage_GetObject(page, i)` for top-level objects
   - For each object, check the type via `fpdf_edit.FPDFPageObj_GetType(obj)`:
     - If `FPDF_PAGEOBJ_IMAGE` (value 3): process as an image (see step 4)
     - If `FPDF_PAGEOBJ_FORM` (value 5): recurse into the Form XObject using `fpdf_edit.FPDFFormObj_CountObjects(obj)` / `fpdf_edit.FPDFFormObj_GetObject(obj, i)`
   - Cap recursion depth at 64 levels. Objects beyond this depth are silently skipped.

4. **For each image object — extract metadata:**
   - Extract bounding box via `fpdf_edit.FPDFPageObj_GetBounds(obj, ref left, ref bottom, ref right, ref top)`. Convert to `x = left`, `y = bottom`, `w = right - left`, `h = top - bottom`. Round all coordinates to 1 decimal place using `FormatUtils.RoundCoordinate()`.
   - Extract pixel dimensions and bits per pixel via `fpdf_edit.FPDFImageObj_GetImageMetadata(obj, page, ref metadata)`. Use `metadata.width`, `metadata.height`, and `metadata.bits_per_pixel`.
   - If metadata extraction fails (API returns error), skip this image entirely and continue processing remaining images.

5. **Track unique image objects** — Use the native object pointer (the `IntPtr` / handle returned by `FPDFPage_GetObject` / `FPDFFormObj_GetObject`) to identify unique image objects. Maintain a dictionary mapping object pointers to their assigned image index (1-based) and file path (if extracted). When the same pointer is encountered again, reuse the existing index and file path — do not re-render or re-write the image.

6. **Handle empty pages** — If no images are found, the `images` list must be empty (not null).

#### When `outputPath` is provided — image extraction:

7. **Generate file names** using the pattern `{pdfStem}_p{page}_img{index}.{ext}` where:
   - `{pdfStem}` is the PDF filename without extension
   - `{page}` is the 1-based page number
   - `{index}` is the 1-based unique image index (assigned sequentially in discovery order)
   - `{ext}` is `jpg` for pure-JPEG images (single `DCTDecode` filter) or `png` for all other encodings
   - Example: `report.pdf`, page 3, second unique image (JPEG) → `report_p3_img2.jpg`
   - Example: `report.pdf`, page 3, third unique image (PNG/other) → `report_p3_img3.png`

8. **Sanitize `{pdfStem}`** — Remove or replace characters that are invalid in file names on the host OS (use `Path.GetInvalidFileNameChars()`). If the sanitized stem is empty, use `"pdf"` as a fallback.

9. **Extract each unique image** — For each unique image object (first occurrence only):
   - **Raw JPEG fast path:** Check the image's compression filter via `FPDFImageObj_GetImageFilterCount(obj)` and `FPDFImageObj_GetImageFilter(obj, index, ...)`. If the image has exactly one filter and it is `DCTDecode`, extract the raw JPEG bytes directly via `FPDFImageObj_GetImageDataRaw(obj, buffer, size)` and write to disk with a `.jpg` extension. This avoids bitmap rendering and re-encoding entirely — the output is the original JPEG data with zero quality loss.
   - **Bitmap rendering path (non-JPEG):** If the image is not a pure JPEG, attempt rendering via `fpdf_edit.FPDFImageObj_GetRenderedBitmap(document, page, obj)`, which produces a BGRA bitmap with the image's mask and transformation matrix applied.
   - If `GetRenderedBitmap` returns null, fall back to `fpdf_edit.FPDFImageObj_GetBitmap(obj)`, which returns the raw image data at native resolution without mask/matrix processing. Log a warning when falling back.
   - If both `GetRenderedBitmap` and `GetBitmap` return null, set `file` to null for all occurrences of this image object. Do not fail the tool call.
   - If a bitmap is obtained (from either API):
     - Get bitmap dimensions via `fpdfview.FPDFBitmapGetWidth(bitmap)` and `fpdfview.FPDFBitmapGetHeight(bitmap)`
     - Get the pixel format via `fpdfview.FPDFBitmapGetFormat(bitmap)`. The format determines bytes per pixel: BGRA (format 4) = 4 bpp, BGRx (format 3) = 4 bpp, BGR (format 2) = 3 bpp, Gray (format 1) = 1 bpp.
     - **Normalize to BGRA** — Convert the bitmap data to a contiguous BGRA `byte[]` array regardless of source format:
       - **BGRA (format 4):** Copy directly, handling stride padding if `stride != width * 4`.
       - **BGRx (format 3):** Copy as 4 bpp, then set all alpha bytes to 255 (opaque).
       - **BGR (format 2):** Expand 3 bpp to 4 bpp, inserting alpha = 255 for each pixel.
       - **Gray (format 1):** Expand 1 bpp to 4 bpp, replicating the gray value to B, G, R channels and setting alpha = 255.
     - Get the stride via `fpdfview.FPDFBitmapGetStride(bitmap)` and account for row alignment padding during the copy.
     - Destroy the bitmap via `fpdfview.FPDFBitmapDestroy(bitmap)` in a `try/finally` block
     - Encode to PNG using `PngEncoder.Encode(bgraData, width, height, preserveAlpha: true)` — preserving alpha transparency (FRD-006 requirement 15)
     - Write the PNG to disk at the generated file path. If write fails, set `file` to null for all occurrences.
     - If a file with the same name already exists, overwrite it without error.

10. **Set the `file` field** — For each image occurrence, set `file` to the absolute path of the written file — `.jpg` for raw JPEG extractions, `.png` for bitmap-rendered extractions (shared across all occurrences of the same image object). For images where extraction failed, `file` is null.

#### When `outputPath` is not provided:

11. The `file` field must be null for all images (omitted from serialized JSON). No rendering or file I/O occurs — only metadata extraction via `FPDFPageObj_GetBounds` and `FPDFImageObj_GetImageMetadata`.

### PDFiumCore Image Extraction API Reference

| API | Description |
|-----|-------------|
| `fpdf_edit.FPDFPage_CountObjects(page)` | Returns the number of top-level page objects. |
| `fpdf_edit.FPDFPage_GetObject(page, index)` | Returns a handle to the page object at the given 0-based index. |
| `fpdf_edit.FPDFPageObj_GetType(obj)` | Returns the object type: 1=Path, 2=Text, 3=Image, 4=Shading, 5=Form. |
| `fpdf_edit.FPDFPageObj_GetBounds(obj, ref left, ref bottom, ref right, ref top)` | Gets the bounding box in page-space coordinates (PDF points). |
| `fpdf_edit.FPDFImageObj_GetImageMetadata(obj, page, ref metadata)` | Populates `FPDF_IMAGEOBJ_METADATA` struct: `width`, `height`, `bits_per_pixel`, `colorspace`. |
| `fpdf_edit.FPDFImageObj_GetRenderedBitmap(document, page, obj)` | Renders the image object individually with mask and matrix applied. Returns a bitmap handle or null on failure. |
| `fpdf_edit.FPDFImageObj_GetBitmap(obj)` | Returns the raw image bitmap at native resolution without mask or matrix applied. Used as a fallback when `GetRenderedBitmap` returns null. The returned bitmap may be in BGR (format 2) or grayscale (format 1) rather than BGRA. |
| `fpdf_edit.FPDFImageObj_GetImageFilterCount(obj)` | Returns the number of compression filters applied to the image stream. |
| `fpdf_edit.FPDFImageObj_GetImageFilter(obj, index, buffer, buflen)` | Returns the name of the filter at the given index (e.g., `"DCTDecode"` for JPEG, `"FlateDecode"` for zlib). |
| `fpdf_edit.FPDFImageObj_GetImageDataRaw(obj, buffer, buflen)` | Returns the raw compressed image stream bytes. For `DCTDecode` images, this is a valid JPEG file. Call with `buffer=null, buflen=0` first to get the required size. |
| `fpdf_edit.FPDFFormObj_CountObjects(formObj)` | Returns the number of child objects inside a Form XObject. |
| `fpdf_edit.FPDFFormObj_GetObject(formObj, index)` | Returns a handle to a child object inside a Form XObject. |
| `fpdfview.FPDFBitmapGetWidth(bitmap)` | Returns the bitmap width in pixels. |
| `fpdfview.FPDFBitmapGetHeight(bitmap)` | Returns the bitmap height in pixels. |
| `fpdfview.FPDFBitmapGetBuffer(bitmap)` | Returns `IntPtr` to the raw BGRA pixel data. |
| `fpdfview.FPDFBitmapGetStride(bitmap)` | Returns bytes per row of the bitmap. |
| `fpdfview.FPDFBitmapGetFormat(bitmap)` | Returns the pixel format: 1=Gray, 2=BGR, 3=BGRx, 4=BGRA. |
| `fpdfview.FPDFBitmapDestroy(bitmap)` | Releases the bitmap. Must be called after pixel data is copied. |

### DI Registration

Register the service in `Program.cs` dependency injection as a singleton, following the existing pattern used by other services.

### Test Data

Create test PDF files programmatically using PdfPig's `PdfDocumentBuilder` API in the existing `TestPdfGenerator` test utility. Ensure test PDFs have deterministic, known content that tests can assert against.

The test data must include:
- A page with at least one embedded PNG image at a known position and size
- A page with at least one embedded JPEG image at a known position and size (for raw JPEG extraction testing)
- A page with multiple embedded images at known positions
- A page with no images (can reuse existing test data from prior tasks)

Place generated PDFs in `tests/TestData/`.

**Note on PdfPig image embedding:** PdfPig's `PdfDocumentBuilder` supports adding images via the `AddPng()` or `AddJpeg()` methods on the page builder. A small (e.g., 2×2 pixel) PNG should be created as a hardcoded minimal PNG byte array in `TestPdfGenerator`. The PNG is then embedded at a known position using `pageBuilder.AddPng(pngBytes, new PdfRectangle(...))` with explicit placement coordinates, providing deterministic test content.

## Acceptance Criteria

### DTOs
- [ ] Image element DTO is defined as an immutable record with all specified fields (`x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, `bitsPerPixel`, `file`), using nullable type for the `file` field.
- [ ] Page images response DTO is defined as an immutable record with `page`, `width`, `height`, and `images` list.

### Service Interface
- [ ] Service interface `IPageImagesService` is defined with a method accepting file path, page number, optional `outputPath`, and `CancellationToken`.

### Metadata Extraction
- [ ] Service enumerates image objects via PDFiumCore's `fpdf_edit` API (not PdfPig).
- [ ] Service extracts bounding boxes from `FPDFPageObj_GetBounds()`.
- [ ] Service extracts pixel dimensions and bits per pixel from `FPDFImageObj_GetImageMetadata()`.
- [ ] All coordinates are rounded to 1 decimal place using `FormatUtils.RoundCoordinate()`.

### Form XObject Recursion
- [ ] Images inside Form XObjects are discovered via recursive traversal using `FPDFFormObj_CountObjects` / `FPDFFormObj_GetObject`.
- [ ] Traversal uses depth-first pre-order for deterministic image indexing.
- [ ] Recursion depth is capped at 64 levels; deeper objects are silently skipped.
- [ ] Multiple nesting levels are handled without errors.

### Duplicate Image Deduplication
- [ ] When the same image object appears multiple times (e.g., via repeated Form XObject references), all occurrences are reported with individual bounding boxes.
- [ ] Duplicate image objects are rendered and written to disk only once.
- [ ] All occurrences of a duplicate share the same `file` path and image index.

### Without `outputPath`
- [ ] When `outputPath` is null, the `file` field is null for all images (omitted from serialized JSON).
- [ ] No rendering or file I/O occurs — only metadata extraction.

### With `outputPath` — Image Extraction
- [ ] Images with a single `DCTDecode` filter are extracted as raw JPEG files (`.jpg` extension) without bitmap rendering or re-encoding.
- [ ] Non-JPEG images are extracted via `FPDFImageObj_GetRenderedBitmap()` and written as PNG files (`.png` extension).
- [ ] When `GetRenderedBitmap` returns null for non-JPEG images, the service falls back to `FPDFImageObj_GetBitmap()` to extract the raw image at native resolution.
- [ ] Bitmap data from any PDFium format (BGRA, BGRx, BGR, Gray) is normalized to BGRA before PNG encoding.
- [ ] PNG encoding uses `PngEncoder.Encode` with `preserveAlpha: true` — alpha transparency is preserved.
- [ ] File names follow the pattern `{pdfStem}_p{page}_img{index}.{ext}` where `{ext}` is `jpg` or `png`.
- [ ] The `{pdfStem}` is sanitized to remove invalid filename characters, with `"pdf"` as a fallback for empty stems.
- [ ] The `file` field contains the absolute path to each written file.
- [ ] Existing files in the output directory are overwritten without error.
- [ ] Extracted JPEG files are valid and start with the SOI marker (`FF D8`). Extracted PNG files start with the PNG signature.

### Output Path Validation
- [ ] `outputPath` that is not absolute is rejected with `ArgumentException` and message `"outputPath must be an absolute path."`.
- [ ] `outputPath` containing `..` is rejected with `ArgumentException` and message `"Invalid output path."`.
- [ ] `outputPath` that does not exist as a directory is rejected with `ArgumentException` and message `"Output directory does not exist: {outputPath}"`.
- [ ] Output path validation occurs before calling `IPdfiumService` (before semaphore acquisition).

### Error Handling
- [ ] Per-image metadata extraction errors skip the individual image and continue processing.
- [ ] If `FPDFImageObj_GetRenderedBitmap()` returns null for an image, the service falls back to `FPDFImageObj_GetBitmap()`. If both return null, the `file` field is null but the tool call succeeds.
- [ ] File write errors set the image's `file` field to null for all occurrences without failing the tool call.
- [ ] A page with no images returns an empty `images` list (not null).
- [ ] The service delegates all PDFiumCore lifecycle and semaphore management to `IPdfiumService` — it does not create its own semaphore or call `FPDF_LoadDocument` / `FPDF_ClosePage` / `FPDF_CloseDocument` directly.
- [ ] Bitmap handles from `FPDFImageObj_GetRenderedBitmap()` or `FPDFImageObj_GetBitmap()` are destroyed via `FPDFBitmapDestroy` in `try/finally` blocks.

### Cancellation
- [ ] The `CancellationToken` is forwarded to `IPdfiumService` for semaphore cancellation.
- [ ] A cancelled request waiting for the semaphore throws `OperationCanceledException` promptly.

### Registration
- [ ] Service is registered as a singleton in `Program.cs`.
- [ ] Test data PDF(s) exist in `tests/TestData/` with known embedded images.

## Testing Requirements

Unit tests must cover:

1. **Image extraction with metadata** — Given a PDF page with a known embedded image, verify the service returns an image element with correct `x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, and `bitsPerPixel` values.
2. **Multiple images** — Given a PDF page with multiple embedded images, verify all images are returned with correct metadata.
3. **No outputPath (default)** — Given a PDF with images, verify that calling with `outputPath = null` returns image elements with `file` as null.
4. **outputPath with extraction** — Given a PDF with images and a valid output directory, verify that image files are written to disk and `file` fields contain valid absolute paths.
5. **File naming convention** — Verify files follow the `{pdfStem}_p{page}_img{index}.{ext}` pattern, with `.jpg` for JPEG images and `.png` for others.
6. **Filename sanitization** — Verify that PDF filenames with special characters (spaces, unicode, filesystem-illegal characters) produce valid output filenames. Verify that an all-special-character filename falls back to `"pdf"`.
7. **PNG alpha preservation** — Verify the extracted PNG is encoded with `preserveAlpha: true` (color type 6 / RGBA, not composited against white).
8. **Extracted PNG validity** — Verify the written PNG file starts with the PNG signature and contains a valid IHDR chunk.
9. **Empty page** — Given a PDF page with no images, verify the response contains an empty `images` list and no files are written.
10. **Coordinate rounding** — Verify all positional values are rounded to 1 decimal place.
11. **Page dimensions** — Verify the response includes correct page width and height, rounded to 1 decimal place.
12. **Page number validation** — Verify that out-of-range page numbers throw `McpException` (via `IPdfiumService`).
13. **Invalid PDF** — Verify that a non-PDF file throws `McpException` (via `IPdfiumService`).
14. **Output path validation — not absolute** — Call with a relative `outputPath`. Verify `ArgumentException` is thrown with the specified message.
15. **Output path validation — traversal** — Call with `outputPath` containing `..`. Verify `ArgumentException` is thrown.
16. **Output path validation — nonexistent** — Call with a nonexistent directory. Verify `ArgumentException` is thrown.
17. **File overwrite** — Verify that existing files are overwritten without error.
18. **Render failure resilience** — If `FPDFImageObj_GetRenderedBitmap()` returns null for one image, verify the service falls back to `FPDFImageObj_GetBitmap()`. If both return null, verify the `file` field is null but other images are still extracted.
19. **GetBitmap fallback format handling** — Verify that images extracted via the `FPDFImageObj_GetBitmap()` fallback (which may return BGR or grayscale format) are correctly normalized to BGRA and produce valid PNG files.
20. **Serialization** — Verify that DTOs serialize to expected JSON structure: camelCase properties, null fields omitted, compact format. Specifically verify that when `file` is null, the field is absent from JSON.
21. **Form XObject recursion** — Given a PDF with images inside Form XObjects, verify they are discovered and included in the response.
22. **Duplicate image deduplication** — Given a PDF with the same image referenced multiple times, verify all occurrences appear with individual bounding boxes but only one file is written.
23. **Raw JPEG extraction** — Given a PDF with a JPEG-encoded image (`DCTDecode` filter), verify the extracted file has a `.jpg` extension and contains the raw JPEG data (starts with `FF D8`, ends with `FF D9`).
24. **Raw JPEG vs PNG path selection** — Given a PDF with a PNG-encoded image (`FlateDecode` filter), verify the extracted file has a `.png` extension. Given a JPEG-encoded image, verify `.jpg`.
25. **JPEG metadata preserved** — Given a PDF with a JPEG image, verify the metadata (bounding box, pixel dimensions, bitsPerPixel) is still correct regardless of the extraction format.
