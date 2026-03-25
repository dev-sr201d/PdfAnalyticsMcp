# Task 013: GetPageImages Tool and Integration Tests

## Description

Create the MCP tool class for `GetPageImages` and comprehensive integration tests that exercise the tool through the MCP protocol. The tool is a thin wrapper that validates the file path, delegates to the page images extraction service, and returns serialized JSON. Integration tests verify end-to-end behavior including tool discovery, image metadata extraction, base64 data inclusion/exclusion, and error handling.

## Traces To

- **FRD:** FRD-005 (Page Image Extraction — GetPageImages)
- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-4 (Image extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK), ADR-0005 (Serialization)

## Dependencies

- **Task 002** — MCP server host with stdio transport (complete)
- **Task 003** — Test project and server verification (complete)
- **Task 007** — GetPdfInfo tool and integration tests — establishes the MCP integration test pattern (complete)
- **Task 012** — GetPageImages service and DTOs (must be complete before this task)

## Technical Requirements

### Tool Class

Define a tool class in `Tools/` that:

1. Is decorated with the `[McpServerToolType]` attribute for automatic discovery.
2. Contains a single tool method decorated with `[McpServerTool]` and `[Description]` attributes.
3. Uses primary constructor to inject `IInputValidationService` and `IPageImagesService`.
4. Accepts three parameters, each with `[Description]` attributes:
   - `pdfPath` (string, required) — Absolute path to the PDF file
   - `page` (int, required) — 1-based page number
   - `includeData` (bool, optional, default `false`) — Whether to include base64-encoded PNG image data
5. The tool method must:
   - Validate the file path using `IInputValidationService.ValidateFilePath()` — this is the **only** validation the tool performs directly; page number validation is handled inside the service (matching the established pattern)
   - Delegate to the page images service for extraction, passing `pdfPath`, `page`, and `includeData`
   - Serialize the result using `JsonSerializer.Serialize()` with `SerializerConfig.Options`
   - Return the serialized JSON string
   - Catch `ArgumentException` and rethrow as `McpException` to preserve error messages for the agent

### Tool Description

The `[Description]` on the tool method must clearly communicate to AI agents:
- What the tool returns (embedded images with bounding boxes, pixel dimensions, and bits per component)
- That it operates on a single page
- That image data is excluded by default to keep responses small
- That setting `includeData` to true includes base64-encoded PNG data for each image where conversion succeeds
- The use cases (understanding text flow around images, extracting images for format conversion)

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number
- `includeData`: That when true, base64-encoded PNG image data is included in the response; defaults to false

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema (`pdfPath`, `page`, `includeData`).
- [ ] Calling the tool on a page with images returns image elements with correct bounding box coordinates (`x`, `y`, `w`, `h`), pixel dimensions (`pixelWidth`, `pixelHeight`), and bits per component (`bitsPerComponent`).
- [ ] Calling the tool with `includeData` omitted or set to `false` returns image elements without a `data` field (omitted from JSON).
- [ ] Calling the tool with `includeData = true` returns image elements with base64-encoded PNG data in the `data` field where conversion succeeds.
- [ ] Images where PNG conversion fails still appear in the response with metadata but without `data`.
- [ ] Base64 data in the response is a plain base64 string (no data URI prefix).
- [ ] A page with no images returns an empty `images` array.
- [ ] Coordinates in the response are rounded to 1 decimal place.
- [ ] The response uses compact JSON with camelCase and null omission.
- [ ] The response without image data for a typical page is well under 30 KB.
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error with "could not be opened as a PDF" in the message.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] Page number zero or negative returns an MCP error with "Page number must be 1 or greater."

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern from `GetPdfInfoIntegrationTests`, `GetPageTextIntegrationTests`, and `GetPageGraphicsIntegrationTests`:
- Launch the server as a child process communicating over stdio
- Perform MCP handshake (initialize → notifications/initialized) before tool calls
- Send `tools/call` requests with JSON-RPC protocol
- Validate responses with appropriate timeouts

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `GetPageImages` (or its snake_case equivalent) appears with the expected input schema including `pdfPath`, `page`, and `includeData` parameters.
2. **Image metadata extraction** — Call the tool on a test PDF page with known embedded images. Verify the response contains the correct page number, page dimensions, and at least one image element with `x`, `y`, `w`, `h`, `pixelWidth`, `pixelHeight`, and `bitsPerComponent` fields.
3. **includeData omitted (default)** — Call the tool on a page with images without specifying `includeData` in the request arguments at all. Verify that no `data` field appears in any image element in the JSON response. This validates the MCP SDK correctly applies the default value for the optional boolean parameter.
4. **includeData explicitly false** — Call the tool on a page with images with `includeData` explicitly set to `false`. Verify that no `data` field appears in any image element in the JSON response.
5. **includeData true** — Call the tool on a page with images with `includeData = true`. Verify that at least one image element contains a non-null `data` field with a valid base64 string.
6. **Base64 format validation** — Verify the `data` field is a plain base64 string (no `data:image/png;base64,` prefix). Optionally decode it and verify it starts with the PNG header bytes (`\x89PNG`).
7. **Empty images page** — Call the tool on a test PDF page that has no embedded images (reuse an existing text-only or blank PDF from prior tasks, such as `sample-no-metadata.pdf`). Verify the response contains an empty `images` array.
8. **Coordinate rounding** — Verify that all coordinate values in the response have at most 1 decimal place.
9. **Response size without data** — Verify the response JSON size (without `includeData`) is well under 30 KB for a typical test page.
10. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".
11. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".
12. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".
13. **Invalid PDF** — Call with a non-PDF file. Verify the error mentions the file could not be opened as a PDF.
14. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.
15. **Page number zero or negative** — Call with `page = 0`. Verify the error contains "Page number must be 1 or greater."

### Test Data Requirements

Integration tests must use test PDF files with **known, deterministic embedded images** generated by Task 012 (in `TestPdfGenerator`). The following test data contract is expected from Task 012:

| Test Data | Source | Used By Scenarios |
|-----------|--------|-------------------|
| Page with embedded images at known positions/sizes | `TestPdfGenerator` via `AddPng` | #2, #3, #4, #5, #6, #8, #9 |
| Page with no embedded images (text-only or blank) | Existing `sample-no-metadata.pdf` from Task 006 | #7 |

All generated PDFs should be placed in `tests/TestData/`. If Task 012's test data does not cover a specific scenario, additional test data may be generated in the integration test setup.
