# Task 012: GetPageImages Service and DTOs

## Description

Create the data transfer objects and extraction service for the `GetPageImages` tool (FRD-005). The service uses PdfPig's `page.GetImages()` API to enumerate embedded images on a single PDF page, extracting each image's bounding box (position and display size on the page), pixel dimensions, color depth, and optionally base64-encoded PNG data. The service returns structured DTOs ready for JSON serialization.

## Traces To

- **FRD:** FRD-005 (Page Image Extraction — GetPageImages)
- **PRD:** REQ-4 (Image extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 004** — Shared serialization configuration: `SerializerConfig`, `FormatUtils` (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)

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
   - `data` (string?, nullable) — Base64-encoded PNG data (only when `includeData = true` and PNG conversion succeeds)

2. **Page images response DTO** — Envelope for the full tool response:
   - `page` (int) — The 1-based page number returned
   - `width` (double) — Page width in PDF points, rounded to 1 decimal place
   - `height` (double) — Page height in PDF points, rounded to 1 decimal place
   - `images` (list of image element DTOs) — All embedded images found on the page (empty list if none; never null)

### Service Interface

Define a service interface `IPageImagesService` in `Services/` with a method that:
- Accepts a file path (string), a 1-based page number (int), and an `includeData` flag (bool)
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
8. **Handle base64 data based on `includeData` parameter:**
   - When `includeData` is `false` (default): the `data` field must be null (omitted from JSON).
   - When `includeData` is `true`: attempt PNG conversion via `image.TryGetPng(out byte[] pngBytes)`.
     - If `TryGetPng` returns `true`: set `data` to `Convert.ToBase64String(pngBytes)`. The base64 string must be plain (no `data:image/png;base64,` prefix).
     - If `TryGetPng` returns `false`: set `data` to null. The image metadata (position, dimensions) must still be included in the response.
9. **Round all coordinates** (x, y, w, h) using `FormatUtils.RoundCoordinate()`.
10. **Handle per-image exceptions** — If accessing an individual image's properties throws (e.g., malformed image stream), skip that image and continue processing remaining images. Log a warning if logging is available. Do not let one corrupt image fail the entire page extraction.
11. **Return the response DTO** with page dimensions and the images list.
12. **Handle empty pages** — If a page has no images, the `images` list must be empty (not null).
13. **Dispose PdfDocument** properly — open per call, not cached.

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
- A page with no images (can reuse existing test data from prior tasks, such as `sample-no-metadata.pdf`)

Place generated PDFs in `tests/TestData/`.

**Note on PdfPig image embedding:** PdfPig's `PdfDocumentBuilder` supports adding images via the `AddPng()` or `AddJpeg()` methods on the page builder. A small (e.g., 2×2 pixel) PNG should be created as a hardcoded minimal PNG byte array in `TestPdfGenerator` — the PNG format is simple enough for a minimal valid file (header + IHDR + IDAT + IEND chunks). This avoids adding an imaging library dependency to the test project. The PNG is then embedded at a known position using `pageBuilder.AddPng(pngBytes, new PdfRectangle(...))` with explicit placement coordinates, providing deterministic test content.

### Registration

Register the service in `Program.cs` dependency injection as a singleton, following the existing pattern used by `IPdfInfoService`, `IPageTextService`, and `IPageGraphicsService`.

## Acceptance Criteria

- [ ] Image element DTO is defined as an immutable record with all specified fields (`x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, `bitsPerComponent`, `data`), using nullable type for the `data` field.
- [ ] Page images response DTO is defined as an immutable record with `page`, `width`, `height`, and `images` list.
- [ ] Service interface `IPageImagesService` is defined with a method accepting file path, page number, and `includeData` flag.
- [ ] Service implementation extracts image bounding boxes from `image.Bounds`.
- [ ] Service implementation extracts pixel dimensions from `image.WidthInSamples` and `image.HeightInSamples`.
- [ ] Service implementation extracts bits per component from `image.BitsPerComponent`.
- [ ] When `includeData` is `false`, the `data` field is null (omitted from serialized JSON).
- [ ] When `includeData` is `true`, the service attempts `image.TryGetPng()` and includes base64-encoded PNG data on success.
- [ ] When `TryGetPng()` fails, the image metadata is still returned with `data` as null.
- [ ] Base64 data is a plain base64 string with no data URI prefix.
- [ ] All coordinates are rounded to 1 decimal place using `FormatUtils.RoundCoordinate()`.
- [ ] A page with no images returns an empty `images` list (not null).
- [ ] PdfDocument is opened with `using` and disposed after each call.
- [ ] Only the requested page is accessed via `document.GetPage(n)`.
- [ ] Service is registered as a singleton in `Program.cs`.
- [ ] Test data PDF(s) exist in `tests/TestData/` with known embedded images.

## Testing Requirements

Unit tests must cover:

1. **Image extraction with metadata** — Given a PDF page with a known embedded image, verify the service returns an image element with correct `x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, and `bitsPerComponent` values.
2. **Multiple images** — Given a PDF page with multiple embedded images, verify all images are returned with correct metadata.
3. **includeData false (default)** — Given a PDF with images, verify that calling with `includeData = false` returns image elements with `data` as null.
4. **includeData true with successful PNG conversion** — Given a PDF with a PNG-convertible image, verify that calling with `includeData = true` returns a non-null `data` field containing a valid base64 string.
5. **Base64 format** — Verify the `data` field is a plain base64 string (no `data:` prefix). Verify it can be decoded back to valid PNG bytes.
6. **Empty page** — Given a PDF page with no images, verify the response contains an empty `images` list.
7. **Coordinate rounding** — Verify all positional values are rounded to 1 decimal place.
8. **Page dimensions** — Verify the response includes correct page width and height, rounded to 1 decimal place.
9. **Page number validation** — Verify that out-of-range page numbers throw `ArgumentException` (via `IInputValidationService`).
10. **Invalid PDF** — Verify that a non-PDF file throws `ArgumentException`.
11. **Serialization** — Verify that the DTOs serialize to expected JSON structure: camelCase properties, null fields omitted, compact format. Specifically verify that when `data` is null, the field is absent from the JSON output.
