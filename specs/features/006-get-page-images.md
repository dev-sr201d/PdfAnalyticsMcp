# FRD-006: Page Image Extraction (GetPageImages)

## Traces To

- **PRD:** REQ-4 (Image extraction), NFR-1 (Data volume management), REQ-6 (Page-by-page processing), REQ-7 (Robust error handling), REQ-9 (Concurrent tool safety)
- **ADRs:** ADR-0004 (PDFiumCore/PDF rendering and image extraction), ADR-0005 (Serialization)

## Summary

Provide a tool that returns embedded images on a single PDF page with their bounding boxes and metadata. The agent uses image positions to understand text flow around images. When an output directory is provided, the tool extracts each image to disk, enabling the agent to reference or embed them when converting to other formats.

Image discovery and extraction uses PDFiumCore's `fpdf_edit` API, which provides per-object access to page content. The extraction strategy is optimized per encoding:

- **JPEG images** (single `DCTDecode` filter): The raw JPEG bytes are extracted directly via `FPDFImageObj_GetImageDataRaw()`, preserving the original encoding with zero quality loss. The output file uses a `.jpg` extension.
- **All other encodings** (PNG, JPEG2000, JBIG2, CMYK, etc.): Each image object is rendered individually via `FPDFImageObj_GetRenderedBitmap()`, producing a clean BGRA bitmap with the image's mask and transformation matrix applied. The bitmap is encoded as PNG with a `.png` extension. If `GetRenderedBitmap` returns null (which can occur for certain image objects with complex clipping contexts), the service falls back to `FPDFImageObj_GetBitmap()`, which returns the raw image at native resolution without mask/matrix processing. The fallback bitmap may be in BGR or grayscale format, which is normalized to BGRA before PNG encoding.

> **PRD REQ-4 fallback note:** REQ-4 requires an alternative extraction method when direct PNG conversion is not possible. This is addressed through multiple extraction strategies: (1) raw JPEG extraction for DCTDecode images bypasses rendering entirely, (2) `FPDFImageObj_GetRenderedBitmap()` handles all other formats through PDFium's decoder pipeline, and (3) `FPDFImageObj_GetBitmap()` provides a final fallback when rendered bitmap extraction fails. Together, these cover virtually all image encodings found in real-world PDFs.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |
| `outputPath` | string | No | — | Absolute path to a directory where extracted images will be written as PNG files. When omitted, only image metadata is returned (no image data is extracted). |

## Outputs

### Default (no `outputPath`)

A JSON object containing image metadata only:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number returned |
| `width` | double | Page width in PDF points |
| `height` | double | Page height in PDF points |
| `images` | array | Array of image elements found on the page |

### When `outputPath` is provided

The same JSON structure, but each image element includes a `file` field with the path to the extracted PNG file on disk. The tool creates the PNG files in the specified directory using a deterministic naming convention.

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number returned |
| `width` | double | Page width in PDF points |
| `height` | double | Page height in PDF points |
| `images` | array | Array of image elements found on the page |

### Image element fields:

| Field | Type | Description |
|-------|------|-------------|
| `x` | double | Left edge X coordinate on the page (PDF points) |
| `y` | double | Bottom edge Y coordinate on the page (PDF points) |
| `w` | double | Display width on the page (PDF points) |
| `h` | double | Display height on the page (PDF points) |
| `pixelWidth` | int | Image width in pixels |
| `pixelHeight` | int | Image height in pixels |
| `bitsPerPixel` | int | Bits per pixel as reported by PDFiumCore's `FPDF_IMAGEOBJ_METADATA.bits_per_pixel` (e.g., 32 for BGRA, 24 for RGB, 8 for grayscale) |
| `file` | string? | Absolute path to the extracted image file — `.jpg` for JPEG-encoded images, `.png` for all others (only when `outputPath` is provided and extraction succeeded) |

> **Note:** The `colorspace` value available from `FPDFImageObj_GetImageMetadata()` is intentionally omitted from the response. The colorspace is an internal PDF encoding detail (e.g., DeviceRGB, DeviceCMYK, ICCBased) that is not actionable for the AI agent — the agent works with the rendered image, which is always BGRA regardless of source colorspace. Omitting it keeps the payload focused on layout-relevant data.

## Functional Requirements

### Core Metadata Extraction

1. The tool must operate on a single page per call (REQ-6).
2. The tool must use PDFiumCore's `fpdf_edit` API to enumerate page objects via `FPDFPage_CountObjects()` / `FPDFPage_GetObject()` and filter for image objects where `FPDFPageObj_GetType()` returns `FPDF_PAGEOBJ_IMAGE` (value 3). The enumeration must recursively traverse Form XObjects (`FPDF_PAGEOBJ_FORM`, value 5) using `FPDFFormObj_CountObjects()` / `FPDFFormObj_GetObject()` to discover images embedded at any nesting depth (see requirement 16).
3. Each image's bounding box must be extracted via `FPDFPageObj_GetBounds()`, which returns coordinates in PDF page space (points).
4. Pixel dimensions and bits per pixel must be extracted via `FPDFImageObj_GetImageMetadata()`. The `bits_per_pixel` value from the metadata struct is returned directly as `bitsPerPixel` in the response. The `colorspace` value is extracted for internal use but is not included in the response (see output schema note).
5. When `outputPath` is not provided, the `file` field must be omitted from the response. This keeps responses small for the common case where the agent only needs to know image positions (NFR-1).
6. Coordinates must be rounded to 1 decimal place.
7. If a page has no images, the `images` array must be empty (not null).

### File Naming Convention

8. When `outputPath` is provided, extracted images must be written to that directory using the naming pattern: `{pdfStem}_p{page}_img{index}.{ext}` — where `{pdfStem}` is the PDF filename without its extension, `{page}` is the 1-based page number, `{index}` is the 1-based image index (in discovery order, depth-first pre-order traversal of the page object tree including Form XObjects), and `{ext}` is `jpg` for JPEG-encoded images (single `DCTDecode` filter) or `png` for all other encodings. The `{index}` is assigned sequentially to each *unique* image object — when the same image object appears multiple times (see requirement 17), all occurrences share the same `{index}` and the same output file. Examples: for `report.pdf`, page 3, second unique image (JPEG) → `report_p3_img2.jpg`; third unique image (non-JPEG) → `report_p3_img3.png`.
9. The `{pdfStem}` component must be sanitized to remove or replace characters that are invalid in file names on the host OS. If the sanitized stem is empty (e.g., the PDF filename consists entirely of special characters), a fallback stem such as `"pdf"` must be used.
10. The `file` field in the response must contain the absolute path to the written image file (`.jpg` or `.png`).

### Image Data Extraction

11. When `outputPath` is provided, the tool must extract each image using the optimal strategy for its encoding:
    - **JPEG images** (exactly one compression filter, `DCTDecode`): Extract the raw compressed stream bytes via `FPDFImageObj_GetImageDataRaw()` and write directly to disk with a `.jpg` extension. This avoids bitmap rendering and re-encoding entirely, preserving the original JPEG data with zero quality loss and producing significantly smaller output files.
    - **All other images**: Render via `FPDFImageObj_GetRenderedBitmap(document, page, imageObject)`, which produces a clean BGRA bitmap with the image's mask and transformation matrix applied. Encode as PNG, preserving any alpha transparency. No compositing against a white background is performed — unlike page rendering (FRD-005), extracted images may contain transparent regions.
12. The rendered bitmap is produced at the image's native resolution — no DPI parameter is needed for image extraction.
13. All PDFiumCore operations (document loading, page object enumeration, image rendering) must be serialized through the same semaphore used by the rendering service (FRD-005), since PDFium is not thread-safe.
14. If rendering fails for an individual image (e.g., `FPDFImageObj_GetRenderedBitmap()` returns null), the service must fall back to `FPDFImageObj_GetBitmap()` which returns the raw image at native resolution without mask/matrix processing. The fallback bitmap may be in BGR or grayscale format rather than BGRA; these formats must be normalized to BGRA before PNG encoding. If both `GetRenderedBitmap` and `GetBitmap` return null, the image's `file` field must be null. Rendering failures must not cause the entire tool call to fail — other images and all metadata must still be returned.
15. **PNG encoding** must use the same lightweight manual PNG writer used by FRD-005, built on `System.IO.Compression.ZLibStream` (built into .NET 6+). Unlike FRD-005's page rendering (which composites BGRA onto white to produce opaque RGB output), image extraction must encode **RGBA** (4-channel) output to preserve any alpha transparency in the extracted image. The PNG encoder must support both RGB (3-channel, for page rendering) and RGBA (4-channel, for image extraction) modes.
16. The tool must recursively traverse Form XObjects to discover all embedded images, using **depth-first pre-order traversal** (matching the natural PDF content stream order) to ensure deterministic image indexing across runs. PDF pages frequently embed content inside Form XObjects (nested content streams), especially in complex documents with layers, transparency groups, or repeated content. When `FPDFPageObj_GetType()` returns `FPDF_PAGEOBJ_FORM` (value 5) for a page object, the tool must recurse into it using `FPDFFormObj_CountObjects()` / `FPDFFormObj_GetObject()` to enumerate its child objects, repeating recursively for any nested Form XObjects. Image objects found at any nesting depth must be included in the response with the same metadata and extraction behavior as top-level images. The bounding box from `FPDFPageObj_GetBounds()` returns page-space coordinates regardless of nesting depth, so no manual coordinate transformation is needed. The recursion depth must be capped at a reasonable limit (e.g., 64 levels) to guard against malformed or circular PDF structures; objects beyond this depth are silently skipped.
17. Form XObjects can be referenced multiple times on the same page (e.g., a letterhead logo), causing the same underlying image object to appear at multiple positions. The tool must report **every occurrence** in the response — each with its own bounding box and metadata — since the agent needs all positions to understand text flow. However, when `outputPath` is provided, the image must be **rendered and written to disk only once** per unique image object. All occurrences of the same image object must share the same `file` path and the same image index in the file naming convention (see requirement 8). Image object identity can be determined by comparing the native object pointers returned by `FPDFPage_GetObject()` / `FPDFFormObj_GetObject()`.

### Cancellation

18. The tool must accept a `CancellationToken` and support cancellation while waiting for the PDFium semaphore. If a request is cancelled while queued behind the semaphore, it must return promptly without blocking until the current operation completes. This is consistent with the cancellation behavior specified for `RenderPagePreview` (FRD-005, requirements 21–22).

### Output Path Validation

19. The `outputPath` must be validated: it must be an absolute path, must not contain path traversal sequences (`..`), and the directory must exist. Specific error messages:
    - If `outputPath` is not an absolute path: `"outputPath must be an absolute path."`
    - If `outputPath` contains `..`: `"Invalid output path."`
    - If the directory does not exist: `"Output directory does not exist: {outputPath}"`
20. If a file with the same name already exists in the output directory, it must be overwritten.

### Error Handling

21. If metadata extraction fails for an individual image (e.g., `FPDFPageObj_GetBounds()` or `FPDFImageObj_GetImageMetadata()` fails), that image must be skipped entirely. Remaining images on the page must still be returned.
22. If writing an image file to disk fails (e.g., permissions, disk full), the image's `file` field must be null for all occurrences of that image object. The error must not cause the entire tool call to fail — other images and all metadata must still be returned.
23. Standard file path and page number validation rules apply as defined in FRD-007.

## Response Size Considerations

The inline JSON response always contains only image metadata (bounding boxes, pixel dimensions, and optionally file paths) — never image data. This means the response is well under 30 KB for any typical page, regardless of whether `outputPath` is provided.

Image data is written to disk as separate PNG files, keeping the MCP response payload small and avoiding the 33% base64 encoding overhead that inline image data would incur.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- Feature 005 (RenderPagePreview) must be complete — this feature uses the shared PDFium service and PNG encoder introduced by Feature 005.
- `PDFiumCore` NuGet package (for image discovery, metadata extraction, and per-image bitmap rendering).

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options, coordinate rounding utility, and input validation service. PDFiumCore operations must be serialized through the shared PDFium service's semaphore, since all features use the same underlying PDFium native library. PNG encoding shares the same manual PNG writer introduced by Feature 005, extended to support RGBA (4-channel) output for image extraction.

## Acceptance Criteria

### Core Metadata Extraction
- [ ] Calling `GetPageImages` on a page with images returns bounding boxes and pixel dimensions for each image.
- [ ] Calling `GetPageImages` without `outputPath` does not include any `file` fields in the response and does not write any files.
- [ ] A page with no images returns an empty `images` array.
- [ ] Coordinates are rounded to 1 decimal place.
- [ ] The response is well under 30 KB for any typical page (image data is never inline).

### File-Based Image Extraction
- [ ] When `outputPath` is provided, images are written to the specified directory as `.jpg` (for JPEG-encoded images) or `.png` (for all other encodings).
- [ ] JPEG-encoded images (single `DCTDecode` filter) are extracted as `.jpg` files using raw byte passthrough via `FPDFImageObj_GetImageDataRaw()`, preserving original quality with zero re-encoding loss.
- [ ] File names follow the pattern `{pdfStem}_p{page}_img{index}.{ext}` where `{ext}` is `jpg` for JPEG-encoded images or `png` for all others.
- [ ] The `file` field in each image element contains the absolute path to the written image file.
- [ ] Non-JPEG images are extracted individually via `FPDFImageObj_GetRenderedBitmap()`, producing a clean bitmap without surrounding text or graphics.
- [ ] Existing files with the same name are overwritten without error.
- [ ] If rendering fails for an individual image, the `file` field is null but the tool call still succeeds with all metadata intact.
- [ ] Image extraction is serialized through the PDFium semaphore.
- [ ] Each extracted image file (PNG or JPEG) is valid and can be decoded by standard image viewers.
- [ ] When the same image object appears multiple times on a page (e.g., via repeated Form XObject references), all occurrences are reported with their individual bounding boxes, but the image is rendered and written to disk only once.
- [ ] All occurrences of a duplicated image share the same `file` path and image index.

### Output Path Validation
- [ ] The `outputPath` parameter rejects relative paths with error message `"outputPath must be an absolute path."`
- [ ] The `outputPath` parameter rejects paths containing `..` with error message `"Invalid output path."`
- [ ] A non-existent output directory produces error message `"Output directory does not exist: {outputPath}"`.

### Error Handling
- [ ] When metadata extraction fails for an individual image, that image is skipped and remaining images are still returned.
- [ ] When writing an image file to disk fails, the image's `file` field is null but the tool call succeeds with all metadata.
- [ ] When `outputPath` is provided but the page has no images, the tool succeeds with an empty `images` array and no files are written.
- [ ] File name sanitization handles PDF filenames with special characters (spaces, unicode, filesystem-illegal characters) without errors.
- [ ] Images embedded inside Form XObjects (nested content streams) are discovered and included in the response.
- [ ] Form XObject recursion handles multiple nesting levels without errors.
- [ ] Recursion depth is capped; deeply nested structures beyond the limit do not crash the server.
- [ ] A cancelled image extraction request that is waiting for the PDFium semaphore returns promptly without blocking.
