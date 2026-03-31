# Task 016: GetPageImages Tool and Integration Tests

## Description

Create the MCP tool class for `GetPageImages` and comprehensive integration tests that exercise the tool through the MCP protocol. The tool is a thin wrapper that validates the file path and output path, delegates to the page images extraction service, and returns serialized JSON. Integration tests verify end-to-end behavior including tool discovery, image metadata extraction, file-based image extraction with `outputPath`, render-based fallback, and error handling.

## Traces To

- **FRD:** FRD-006 (Page Image Extraction — GetPageImages)
- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-4 (Image extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK), ADR-0004 (Docnet/PDF rendering), ADR-0005 (Serialization)

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
- What the tool returns (embedded images with bounding boxes, pixel dimensions, and bits per component)
- That it operates on a single page
- That image data is not included inline — only metadata is returned by default
- That providing `outputPath` causes images to be extracted as PNG files to that directory, with file paths included in the response
- That the tool uses a render-based fallback for images that cannot be directly extracted, maximizing extraction coverage
- The use cases (understanding text flow around images, extracting images for format conversion)

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number
- `outputPath`: That when provided, images are extracted as PNG files to this directory with deterministic names (`{pdfStem}_p{page}_img{index}.png`) and file paths appear in the response; when omitted, only image metadata is returned

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema (`pdfPath`, `page`, `outputPath`).
- [ ] Calling the tool on a page with images returns image elements with correct bounding box coordinates (`x`, `y`, `w`, `h`), pixel dimensions (`pixelWidth`, `pixelHeight`), and bits per component (`bitsPerComponent`).
- [ ] Calling the tool without `outputPath` returns image elements without a `file` field (omitted from JSON).
- [ ] Calling the tool with a valid `outputPath` extracts images as PNG files to the specified directory and returns `file` paths in the response.
- [ ] File names follow the pattern `{pdfStem}_p{page}_img{index}.png`.
- [ ] Images where direct PNG conversion fails are extracted via render-based fallback.
- [ ] Images where both direct extraction and fallback fail appear in the response with metadata but `file` as null.
- [ ] A page with no images returns an empty `images` array.
- [ ] Coordinates in the response are rounded to 1 decimal place.
- [ ] The response uses compact JSON with camelCase and null omission.
- [ ] The response is well under 30 KB for any typical page (image data is never inline).
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error with "could not be opened as a PDF" in the message.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] Page number zero or negative returns an MCP error with "Page number must be 1 or greater."
- [ ] Invalid `outputPath` (relative, contains `..`, or non-existent directory) returns an appropriate MCP error.

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern. The test class inherits from `McpIntegrationTestBase` (created in Task 003):
- Perform MCP handshake via `PerformHandshakeAsync()` before tool calls
- Use `CallToolAsync()` for `tools/call` requests
- Use `GetToolResultContent()` to extract text content from responses
- Validate responses with appropriate timeouts

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `GetPageImages` (or its snake_case equivalent) appears with the expected input schema including `pdfPath`, `page`, and `outputPath` parameters.
2. **Image metadata extraction** — Call the tool on a test PDF page with known embedded images. Verify the response contains the correct page number, page dimensions, and at least one image element with `x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, and `bitsPerComponent` fields.
3. **outputPath omitted (default)** — Call the tool on a page with images without specifying `outputPath` in the request arguments at all. Verify that no `file` field appears in any image element in the JSON response and no files are written. This validates the MCP SDK correctly applies the default null value for the optional parameter.
4. **outputPath with file extraction** — Call the tool with a valid `outputPath` (temp directory). Verify that PNG files are created in the specified directory, file names follow the `{pdfStem}_p{page}_img{index}.png` pattern, and the `file` field in each image element contains the absolute path to the written file.
5. **Extracted files are valid PNGs** — Verify that files written to `outputPath` begin with the PNG header bytes (`\x89PNG`) and can be read as valid PNG data.
6. **Empty images page** — Call the tool on a test PDF page that has no embedded images (reuse an existing text-only or blank PDF from prior tasks). Verify the response contains an empty `images` array and no files are written to `outputPath` (if provided).
7. **Coordinate rounding** — Verify that all coordinate values in the response have at most 1 decimal place.
8. **Response size** — Verify the response JSON size is well under 30 KB for a typical test page (image data is never inline).
9. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".
10. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".
11. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".
12. **Invalid PDF** — Call with a non-PDF file. Verify the error mentions the file could not be opened as a PDF.
13. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.
14. **Page number zero or negative** — Call with `page = 0`. Verify the error contains "Page number must be 1 or greater."
15. **Invalid outputPath — relative path** — Call with a relative `outputPath`. Verify a clear error is returned.
16. **Invalid outputPath — path traversal** — Call with an `outputPath` containing `..`. Verify a clear error is returned.
17. **Invalid outputPath — non-existent directory** — Call with an `outputPath` that does not exist. Verify a clear error is returned.

### Test Data Requirements

Integration tests must use test PDF files with **known, deterministic embedded images** generated by Task 015 (in `TestPdfGenerator`). The following test data contract is expected from Task 015:

| Test Data | Source | Used By Scenarios |
|-----------|--------|-------------------|
| Page with embedded images at known positions/sizes | `TestPdfGenerator` via `AddPng` | #2, #3, #4, #5, #7, #8 |
| Page with no embedded images (text-only or blank) | Existing `sample-no-metadata.pdf` from Task 006 | #6 |

All generated PDFs should be placed in `tests/TestData/`. If Task 015's test data does not cover a specific scenario, additional test data may be generated in the integration test setup.
