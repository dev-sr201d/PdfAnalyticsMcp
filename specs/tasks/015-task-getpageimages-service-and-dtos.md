# Task 015: GetPageImages Service and DTOs

## Description

Create the data transfer objects and extraction service for the `GetPageImages` tool (FRD-006). The service uses PdfPig's `page.GetImages()` API to enumerate embedded images on a single PDF page, extracting each image's bounding box (position and display size on the page), pixel dimensions, and color depth. When an output directory is provided, the service extracts each image as a PNG file to disk — first attempting direct extraction via PdfPig, then falling back to rendering the page and cropping the image region for any images where direct extraction fails. The service returns structured DTOs ready for JSON serialization.

## Traces To

- **FRD:** FRD-006 (Page Image Extraction — GetPageImages)
- **PRD:** REQ-4 (Image extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling), REQ-10 (Concurrent tool safety)
- **ADRs:** ADR-0002 (PdfPig), ADR-0004 (Docnet/PDF rendering), ADR-0005 (Serialization)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 004** — Shared serialization configuration: `SerializerConfig`, `FormatUtils` (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)
- **Task 012** — BGRA PNG encoder: `PngEncoder` (complete — needed for encoding cropped images from the render fallback)
- **Task 013** — RenderPagePreview service: `IRenderPagePreviewService` (complete — needed for `RenderRawAsync` in the render fallback)

## Technical Requirements

### DTOs

Define response DTOs in the `Models/` directory as immutable `record` types. All coordinates are in PDF points, rounded to 1 decimal place. Nullable fields are omitted from JSON when null (per `SerializerConfig`).

1. **Image element DTO** — Represents a single embedded image on the page:
   - `x` (double) — Left edge X coordinate on the page (PDF points)
   - `y` (double) — Bottom edge Y coordinate on the page (PDF points)
   - `w` (double) — Display width on the page (PDF points)
   - `h` (double) — Display height on the page (PDF points)
   - `pixelWidth` (int) — Image width in pixels (intrinsic resolution)
   - `pixelHeight` (int) — Image height in pixels (intrinsic resolution)
   - `bitsPerComponent` (int) — Bits per color component (e.g., 8 for typical images)
   - `file` (string?, nullable) — Absolute path to the extracted PNG file on disk (only when `outputPath` is provided and extraction succeeded)

2. **Page images response DTO** — Envelope for the full tool response:
   - `page` (int) — The 1-based page number returned
   - `width` (double) — Page width in PDF points, rounded to 1 decimal place
   - `height` (double) — Page height in PDF points, rounded to 1 decimal place
   - `images` (list of image element DTOs) — All embedded images found on the page (empty list if none; never null)

### Service Interface

Define a service interface `IPageImagesService` in `Services/` with a method that:
- Accepts a file path (string), a 1-based page number (int), and an optional `outputPath` (string?)
- Returns the page images response DTO
- Uses `IInputValidationService` for page number validation (file path validation is the tool layer's responsibility, matching the established pattern in existing tools)

### Service Implementation — `PageImagesService`

The service must:

1. **Open the PDF** using PdfPig's `PdfDocument.Open()` with a `using` declaration. If the file cannot be opened as a PDF, catch the exception and throw `ArgumentException` with a message indicating the file could not be opened as a PDF (matching the pattern in `PdfInfoService`, `PageTextService`, and `PageGraphicsService`).
2. **Validate the page number** against the document's page count via `IInputValidationService.ValidatePageNumber()`.
3. **Access only the requested page** using `document.GetPage(n)` — never iterate all pages.
4. **Enumerate images** using `page.GetImages()` which returns `IEnumerable<IPdfImage>`.
5. **Extract bounding box** for each image from `image.Bounds` (a `PdfRectangle`). Convert to `x`, `y`, `w`, `h` coordinates:
   - `x` = `Bounds.Left`
   - `y` = `Bounds.Bottom`
   - `w` = `Bounds.Width`
   - `h` = `Bounds.Height`
6. **Extract pixel dimensions** from `image.WidthInSamples` and `image.HeightInSamples`.
7. **Extract bits per component** from `image.BitsPerComponent`.
8. **Round all coordinates** (x, y, w, h) using `FormatUtils.RoundCoordinate()`.
9. **Handle per-image exceptions** — If accessing an individual image's properties throws (e.g., malformed image stream), skip that image and continue processing remaining images. Log a warning if logging is available. Do not let one corrupt image fail the entire page extraction.
10. **Handle empty pages** — If a page has no images, the `images` list must be empty (not null).
11. **Dispose PdfDocument** properly — open per call, not cached.

#### When `outputPath` is provided:

12. **Validate `outputPath`** — Must be an absolute path, must not contain path traversal sequences (`..`), and the directory must exist. Throw `ArgumentException` with a clear message on failure.
13. **Generate file names** using the pattern `{pdfStem}_p{page}_img{index}.png` where:
    - `{pdfStem}` is the PDF filename without extension (e.g., `report` from `report.pdf`)
    - `{page}` is the 1-based page number
    - `{index}` is the 1-based image index in the order returned by PdfPig
    - Example: `report.pdf`, page 3, second image → `report_p3_img2.png`
14. **Sanitize `{pdfStem}`** — Remove or replace characters that are invalid in file names on the host OS (use `Path.GetInvalidFileNameChars()`). If the sanitized stem is empty (e.g., the PDF filename consists entirely of special characters), use `"pdf"` as a fallback.
15. **Attempt direct extraction first** — For each image, call `image.TryGetPng(out byte[] pngBytes)`. If it succeeds, write `pngBytes` to the output file path using `File.WriteAllBytes()`.
16. **Collect fallback candidates** — Track which images failed `TryGetPng()` along with their bounding boxes and target file paths.
17. **Execute render-based fallback** — If any images require fallback:
    - Render the page **once** using Docnet at a DPI chosen to approximate the native resolution of the highest-resolution fallback image (see DPI selection below). Use the same rendering semaphore as `RenderPagePreviewService` to serialize native library access.
    - For each fallback image, crop the rendered output using the image's bounding box mapped from PDF points to pixel coordinates (see coordinate mapping below).
    - Encode each cropped region as PNG using `PngEncoder.Encode()` and write to disk.
18. **Set the `file` field** — For each image where extraction succeeded (direct or fallback), set `file` to the absolute path of the written PNG file. For images where extraction failed entirely, set `file` to null.
19. **Handle file write errors gracefully** — If writing a PNG file to disk fails (permissions, disk full), set the image's `file` field to null. Do not fail the entire tool call.
20. **Overwrite existing files** — If a file with the same name already exists in the output directory, overwrite it without error.

#### When `outputPath` is not provided:

21. The `file` field must be null for all images (omitted from serialized JSON). No file I/O or rendering occurs — only metadata extraction.

### DPI Selection for Fallback Rendering

The render DPI should approximate the native resolution of the images being cropped to avoid unnecessary upscaling or downscaling:

- For each fallback image, compute its effective DPI: `max(pixelWidth / (w / 72.0), pixelHeight / (h / 72.0))` where `w` and `h` are the image's display size on the page in PDF points.
- Choose the DPI as the maximum across all fallback images on the page.
- Clamp the result to the range 72–600. If the computation cannot be performed (e.g., zero-width image), default to 150 DPI.

### Coordinate Mapping for Image Cropping

When cropping a rendered page to extract an image region:

- Convert PDF-point bounding box to pixel coordinates: `pixelX = x * (dpi / 72.0)`, `pixelY = y * (dpi / 72.0)`, etc.
- **Invert Y-axis**: PDF origin is bottom-left (Y increases upward); pixel origin is top-left (Y increases downward). Compute: `pixelTop = renderHeight - (y + h) * (dpi / 72.0)`.
- **Clamp** the crop region to the render boundaries: `pixelLeft = max(0, pixelLeft)`, `pixelTop = max(0, pixelTop)`, `cropWidth = min(cropWidth, renderWidth - pixelLeft)`, `cropHeight = min(cropHeight, renderHeight - pixelTop)`. Check that the clamped region has positive width and height; skip the image if not.
- Extract the BGRA pixel data for the crop region from the full-page render buffer (row-by-row copy from the appropriate byte offsets in the flat BGRA buffer).
- Encode the cropped BGRA data to PNG using `PngEncoder.Encode()`.

### Rendering via RenderRawAsync

The render-based fallback delegates to `IRenderPagePreviewService.RenderRawAsync()` (FRD-005, requirement 14). This method returns the raw BGRA pixel buffer and dimensions without PNG encoding, allowing this service to crop individual image regions and encode them separately. The rendering semaphore, Docnet lifecycle, and error handling are all encapsulated inside the rendering service — `PageImagesService` does not need to manage any of that. Inject `IRenderPagePreviewService` via constructor injection.

### PdfPig Image API Reference

The service consumes PdfPig's public `IPdfImage` interface. Key properties and methods:

| Property / Method | Type | Description |
|---|---|---|
| `Bounds` | `PdfRectangle` | Bounding box on the page (Left, Bottom, Width, Height) |
| `WidthInSamples` | `int` | Image width in pixels |
| `HeightInSamples` | `int` | Image height in pixels |
| `BitsPerComponent` | `int` | Bits per color component (e.g., 8 for typical RGB/grayscale images) |
| `TryGetPng(out byte[])` | `bool` | Attempts to convert the image to PNG; returns false if conversion fails |

`PdfRectangle` properties (all `double`, in page-space coordinates):

| Property | Description |
|---|---|
| `Left` | Left edge X coordinate |
| `Bottom` | Bottom edge Y coordinate |
| `Width` | Width of the rectangle |
| `Height` | Height of the rectangle |

### Test Data

Create test PDF files programmatically using PdfPig's `PdfDocumentBuilder` API in the existing `TestPdfGenerator` test utility. Ensure test PDFs have deterministic, known content that tests can assert against.

The test data must include:
- A page with at least one embedded image at a known position and size
- A page with multiple embedded images at known positions
- A page with no images (can reuse existing test data from prior tasks, such as `sample-no-metadata.pdf`)

Place generated PDFs in `tests/TestData/`.

**Note on PdfPig image embedding:** PdfPig's `PdfDocumentBuilder` supports adding images via the `AddPng()` or `AddJpeg()` methods on the page builder. A small (e.g., 2×2 pixel) PNG should be created as a hardcoded minimal PNG byte array in `TestPdfGenerator` — the PNG format is simple enough for a minimal valid file (header + IHDR + IDAT + IEND chunks). This avoids adding an imaging library dependency to the test project. The PNG is then embedded at a known position using `pageBuilder.AddPng(pngBytes, new PdfRectangle(...))` with explicit placement coordinates, providing deterministic test content.

### Registration

Register the service in `Program.cs` dependency injection as a singleton, following the existing pattern used by `IPdfInfoService`, `IPageTextService`, and `IPageGraphicsService`.

## Acceptance Criteria

### DTOs
- [ ] Image element DTO is defined as an immutable record with all specified fields (`x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, `bitsPerComponent`, `file`), using nullable type for the `file` field.
- [ ] Page images response DTO is defined as an immutable record with `page`, `width`, `height`, and `images` list.

### Service Interface
- [ ] Service interface `IPageImagesService` is defined with a method accepting file path, page number, and optional `outputPath`.

### Metadata Extraction
- [ ] Service extracts image bounding boxes from `image.Bounds`.
- [ ] Service extracts pixel dimensions from `image.WidthInSamples` and `image.HeightInSamples`.
- [ ] Service extracts bits per component from `image.BitsPerComponent`.
- [ ] All coordinates are rounded to 1 decimal place using `FormatUtils.RoundCoordinate()`.

### Without `outputPath`
- [ ] When `outputPath` is null, the `file` field is null for all images (omitted from serialized JSON).
- [ ] No file I/O or rendering occurs — only metadata extraction.

### With `outputPath` — Direct Extraction
- [ ] When `outputPath` is provided, the service attempts `image.TryGetPng()` for each image and writes the PNG to disk on success.
- [ ] File names follow the pattern `{pdfStem}_p{page}_img{index}.png`.
- [ ] The `{pdfStem}` is sanitized to remove invalid filename characters, with `"pdf"` as a fallback for empty stems.
- [ ] The `file` field contains the absolute path to each written PNG file.

### With `outputPath` — Render-Based Fallback
- [ ] When `TryGetPng()` fails for one or more images and `outputPath` is provided, the service renders the page once via Docnet and crops each failed image.
- [ ] The fallback render DPI approximates the native resolution of the highest-resolution fallback image, clamped to 72–600.
- [ ] Crop coordinate mapping correctly inverts the Y-axis from PDF to pixel space.
- [ ] Crop regions are clamped to the render boundaries.
- [ ] Cropped BGRA data is encoded as PNG using `PngEncoder.Encode()`.
- [ ] Fallback rendering delegates to `IRenderPagePreviewService.RenderRawAsync()` (no direct Docnet/semaphore usage in this service).

### Output Path Validation
- [ ] `outputPath` must be absolute — relative paths are rejected with `ArgumentException`.
- [ ] `outputPath` containing `..` is rejected with `ArgumentException`.
- [ ] `outputPath` that does not exist as a directory is rejected with `ArgumentException`.
- [ ] Existing files in the output directory are overwritten without error.

### Error Handling
- [ ] Per-image metadata extraction errors skip the individual image and continue processing.
- [ ] File write errors set the image's `file` field to null without failing the tool call.
- [ ] If fallback rendering itself fails, affected images have `file` as null but the call still succeeds.
- [ ] A page with no images returns an empty `images` list (not null).
- [ ] PdfDocument is opened with `using` and disposed after each call.
- [ ] Only the requested page is accessed via `document.GetPage(n)`.

### Registration
- [ ] Service is registered as a singleton in `Program.cs`.
- [ ] Test data PDF(s) exist in `tests/TestData/` with known embedded images.

## Testing Requirements

Unit tests must cover:

1. **Image extraction with metadata** — Given a PDF page with a known embedded image, verify the service returns an image element with correct `x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, and `bitsPerComponent` values.
2. **Multiple images** — Given a PDF page with multiple embedded images, verify all images are returned with correct metadata.
3. **No outputPath (default)** — Given a PDF with images, verify that calling with `outputPath = null` returns image elements with `file` as null.
4. **outputPath with direct extraction** — Given a PDF with a PNG-convertible image and a valid output directory, verify that PNG files are written to disk and `file` fields contain valid absolute paths.
5. **File naming convention** — Verify files follow the `{pdfStem}_p{page}_img{index}.png` pattern.
6. **Filename sanitization** — Verify that PDF filenames with special characters (spaces, unicode, filesystem-illegal characters) produce valid output filenames. Verify that an all-special-character filename falls back to `"pdf"`.
7. **Render-based fallback** — Given an image that fails `TryGetPng()`, verify the service falls back to render-and-crop and writes a valid PNG file. (This may require a test PDF with an image format PdfPig can't convert, or mocking `TryGetPng()` to return false.)
8. **Single render for multiple fallbacks** — Verify the page is rendered only once when multiple images need fallback extraction.
9. **Crop coordinate mapping** — Verify the Y-axis inversion and clamping produce correct crop regions.
10. **Empty page** — Given a PDF page with no images, verify the response contains an empty `images` list and no files are written.
11. **Coordinate rounding** — Verify all positional values are rounded to 1 decimal place.
12. **Page dimensions** — Verify the response includes correct page width and height, rounded to 1 decimal place.
13. **Page number validation** — Verify that out-of-range page numbers throw `ArgumentException`.
14. **Invalid PDF** — Verify that a non-PDF file throws `ArgumentException`.
15. **Output path validation** — Verify relative paths, paths with `..`, and non-existent directories throw `ArgumentException`.
16. **File overwrite** — Verify that existing files are overwritten without error.
17. **Serialization** — Verify that DTOs serialize to expected JSON structure: camelCase properties, null fields omitted, compact format. Specifically verify that when `file` is null, the field is absent from JSON; and when `file` is present, it contains an absolute path string.
