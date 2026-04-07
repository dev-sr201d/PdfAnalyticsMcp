# Task 016: GetPageImages Tool and Integration Tests

## Description

Create the MCP tool class for `GetPageImages` and comprehensive integration tests that exercise the tool through the MCP protocol. The tool is a thin wrapper that validates the file path and page minimum, delegates to the page images extraction service, and returns serialized JSON. Integration tests verify end-to-end behavior including tool discovery, image metadata extraction (bounding boxes, pixel dimensions, bits per pixel), file-based image extraction with `outputPath` (raw JPEG extraction for DCTDecode images, PNG for all others), Form XObject recursion, duplicate image deduplication, per-image error resilience, and error handling.

> **Note:** The tool class (`GetPageImagesTool`) was created alongside the service in Task 015. This task focuses on verifying it via integration tests and confirming tool discovery, parameter schema, and end-to-end behavior through the MCP protocol.

## Traces To

- **FRD:** FRD-006 (Page Image Extraction — GetPageImages)
- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-4 (Image extraction), NFR-1 (Data volume management), REQ-6 (Page-by-page processing), REQ-7 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK), ADR-0004 (PDFiumCore/PDF rendering and image extraction), ADR-0005 (Serialization)

## Dependencies

- **Task 002** — MCP server host with stdio transport (complete)
- **Task 003** — Test project and server verification (complete)
- **Task 007** — GetPdfInfo tool and integration tests — establishes the MCP integration test pattern (complete)
- **Task 015** — GetPageImages service and DTOs (must be complete before this task)

## Technical Requirements

### Tool Class

Define a tool class in `Tools/` that:

1. Is decorated with the `[McpServerToolType]` attribute for automatic discovery.
2. Contains a single tool method decorated with `[McpServerTool]` and `[Description]` attributes.
3. Uses primary constructor to inject `IInputValidationService` and `IPageImagesService`.
4. Accepts three parameters, each with `[Description]` attributes:
   - `pdfPath` (string, required) — Absolute path to the PDF file
   - `page` (int, required) — 1-based page number
   - `outputPath` (string?, optional, default `null`) — Absolute path to an output directory for extracted PNG files
5. The tool method must:
   - Validate the file path using `IInputValidationService.ValidateFilePath()` — this is the **only** validation the tool performs directly for the file path; page number validation and output path validation are handled inside the service (matching the established pattern)
   - Validate the page number minimum using `IInputValidationService.ValidatePageMinimum()` — early fail-fast for obviously invalid values before opening the PDF
   - Delegate to the page images service for extraction, passing `pdfPath`, `page`, and `outputPath`
   - Serialize the result using `JsonSerializer.Serialize()` with `SerializerConfig.Options`
   - Return the serialized JSON string
   - Catch `ArgumentException` and rethrow as `McpException` to preserve error messages for the agent

### Tool Description

The `[Description]` on the tool method must clearly communicate to AI agents:
- What the tool returns (embedded images with bounding boxes, pixel dimensions, and bits per pixel)
- That it operates on a single page
- That image data is not included inline — only metadata is returned by default
- That providing `outputPath` causes images to be extracted to that directory — JPEG-encoded images as `.jpg` files (raw bytes, zero quality loss) and all others as `.png` files — with file paths included in the response
- That images are extracted individually via `FPDFImageObj_GetRenderedBitmap()`, producing clean bitmaps regardless of source encoding
- That images embedded inside Form XObjects (nested content streams) are discovered and included
- The use cases (understanding text flow around images, extracting images for format conversion)

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number
- `outputPath`: That when provided, images are extracted to this directory — JPEG images as `.jpg` files (raw data, no re-encoding), all others as `.png` files — with deterministic names (`{pdfStem}_p{page}_img{index}.{ext}`) and file paths appear in the response; when omitted, only image metadata is returned

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema (`pdfPath`, `page`, `outputPath`).
- [ ] Calling the tool on a page with images returns image elements with correct bounding box coordinates (`x`, `y`, `w`, `h`), pixel dimensions (`pixelWidth`, `pixelHeight`), and bits per pixel (`bitsPerPixel`).
- [ ] Calling the tool without `outputPath` returns image elements without a `file` field (omitted from JSON).
- [ ] Calling the tool with a valid `outputPath` extracts images to the specified directory — JPEG-encoded images as `.jpg` files and all others as `.png` files — and returns `file` paths in the response.
- [ ] JPEG images are extracted as raw JPEG data (`.jpg` extension, starts with `FF D8`).
- [ ] Non-JPEG images are extracted via bitmap rendering and written as PNG files (`.png` extension).
- [ ] File names follow the pattern `{pdfStem}_p{page}_img{index}.{ext}` where `{ext}` is `jpg` for JPEG images or `png` for others.
- [ ] Images where rendering returns null (both `GetRenderedBitmap` and `GetBitmap`) appear in the response with metadata but `file` as null.
- [ ] When metadata extraction fails for an individual image (e.g., `FPDFPageObj_GetBounds()` or `FPDFImageObj_GetImageMetadata()` fails), that image is skipped entirely but remaining images and the tool call still succeed (FRD-006 req 21).
- [ ] When writing an image file to disk fails, the image's `file` field is null for all occurrences of that image object but the tool call still succeeds with all metadata intact (FRD-006 req 22).
- [ ] A page with no images returns an empty `images` array.
- [ ] Images embedded inside Form XObjects (nested content streams) are discovered and included in the response.
- [ ] When the same image object appears multiple times on a page (e.g., via repeated Form XObject references), all occurrences are reported with individual bounding boxes, but the image is rendered and written to disk only once.
- [ ] All occurrences of a duplicated image share the same `file` path and image index.
- [ ] Coordinates in the response are rounded to 1 decimal place.
- [ ] The response uses compact JSON with camelCase and null omission.
- [ ] The response is well under 30 KB for any typical page (image data is never inline).
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error with "The file could not be opened as a PDF" in the message.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] Page number zero or negative returns an MCP error with "Page number must be 1 or greater."
- [ ] `outputPath` that is not absolute returns an MCP error containing "outputPath must be an absolute path."
- [ ] `outputPath` containing `..` returns an MCP error containing "Invalid output path."
- [ ] Non-existent `outputPath` directory returns an MCP error containing "Output directory does not exist: {outputPath}".

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern. The test class inherits from `McpIntegrationTestBase` (created in Task 003):
- Perform MCP handshake via `PerformHandshakeAsync()` before tool calls
- Use `CallToolAsync()` for `tools/call` requests
- Use `GetToolResultContent()` to extract text content from responses
- Validate responses with appropriate timeouts

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `GetPageImages` (or its snake_case equivalent) appears with the expected input schema including `pdfPath`, `page`, and `outputPath` parameters.
2. **Image metadata extraction** — Call the tool on a test PDF page with known embedded images. Verify the response contains the correct page number, page dimensions, and at least one image element with `x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, and `bitsPerPixel` fields.
3. **outputPath omitted (default)** — Call the tool on a page with images without specifying `outputPath` in the request arguments at all. Verify that no `file` field appears in any image element in the JSON response and no files are written. This validates the MCP SDK correctly applies the default null value for the optional parameter.
4. **outputPath with file extraction** — Call the tool with a valid `outputPath` (temp directory). Verify that image files are created in the specified directory, file names follow the `{pdfStem}_p{page}_img{index}.{ext}` pattern, and the `file` field in each image element contains the absolute path to the written file.
5. **Extracted PNG files are valid** — For PNG-sourced test images, verify that files written to `outputPath` begin with the PNG header bytes (`\x89PNG`).
5a. **Extracted JPEG files are valid** — For JPEG-sourced test images, verify that files written to `outputPath` begin with the JPEG SOI marker (`\xFF\xD8`) and end with the EOI marker (`\xFF\xD9`).
6. **Empty images page** — Call the tool on a test PDF page that has no embedded images (reuse an existing text-only or blank PDF from prior tasks). Verify the response contains an empty `images` array and no files are written to `outputPath` (if provided).
7. **Coordinate rounding** — Verify that all coordinate values in the response have at most 1 decimal place.
8. **Response size** — Verify the response JSON size is well under 30 KB for a typical test page (image data is never inline).
9. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".
10. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".
11. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".
12. **Invalid PDF** — Call with a non-PDF file. Verify the error mentions the file could not be opened as a PDF.
13. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.
14. **Page number zero or negative** — Call with `page = 0`. Verify the error contains "Page number must be 1 or greater."
15. **Invalid outputPath — relative path** — Call with a relative `outputPath` (e.g., `"relative/path"`). Verify the MCP error response contains `"absolute"` (case-insensitive), matching the service's `"outputPath must be an absolute path."` message.
16. **Invalid outputPath — path traversal** — Call with an `outputPath` containing `..` (e.g., `"C:\temp\..\secret"`). Verify the error contains `"Invalid output path"`.
17. **Invalid outputPath — non-existent directory** — Call with an `outputPath` that does not exist. Verify the error contains `"does not exist"` (case-insensitive), matching the service's `"Output directory does not exist: {outputPath}"` message.
18. **Form XObject recursion** — Call the tool on `sample-formxobj-image.pdf` (generated by `TestPdfGenerator.CreateFormXObjectImageTestPdf()`), which contains an image embedded inside a Form XObject. Verify that the image is discovered and included in the response with bounding box, pixel dimensions, and bits per pixel. When `outputPath` is provided, verify the image is extracted as a valid PNG file.
19. **Duplicate image deduplication** — Call the tool with `outputPath` on `sample-dupformxobj-image.pdf` (generated by `TestPdfGenerator.CreateDuplicateFormXObjectImageTestPdf()`), where the same Form XObject — containing a single image — is referenced twice at different positions. PDFium creates separate object instances for each Form XObject reference, so both occurrences are reported with their own metadata and file extraction. Verify both occurrences appear in the response with positive dimensions, both have valid file paths, and both extracted files are valid PNGs. Note: native pointer–based deduplication applies only when PDFium returns the same object pointer for repeated references, which does not occur with Form XObject-based repetition.

### Test Data Requirements

Integration tests use test PDF files with **known, deterministic embedded images** generated by Task 015's `TestPdfGenerator` methods. The following test data is available:

| Test Data | Generator Method | Output File | Used By Scenarios |
|-----------|-----------------|-------------|-------------------|
| Single embedded 2×2 red PNG at position (100, 500), display size 200×150 | `TestPdfGenerator.CreateImageTestPdf()` | `sample-image.pdf` | #2, #3, #4, #5, #7, #8 |
| Single embedded 2×2 red JPEG at position (100, 500), display size 200×150 | `TestPdfGenerator.CreateJpegImageTestPdf()` | `sample-jpeg-image.pdf` | #5a |
| Two embedded 2×2 PNGs at positions (50, 600) 100×80 and (200, 400) 150×120 | `TestPdfGenerator.CreateMultiImageTestPdf()` | `sample-multi-image.pdf` | (additional metadata verification) |
| Text-only page with no embedded images | Existing from Task 006 | `sample-no-metadata.pdf` | #6 |
| Image embedded inside a Form XObject, positioned at (100, 500) 200×150 | `TestPdfGenerator.CreateFormXObjectImageTestPdf()` | `sample-formxobj-image.pdf` | #18 |
| Same Form XObject (containing one image) referenced twice at positions (100, 500) and (300, 300), both 200×150 | `TestPdfGenerator.CreateDuplicateFormXObjectImageTestPdf()` | `sample-dupformxobj-image.pdf` | #19 |

All generated PDFs are placed in `tests/TestData/`. Test methods call the generator to ensure the file exists before use (generators are idempotent — they only write the file if it doesn't exist or always overwrite).
