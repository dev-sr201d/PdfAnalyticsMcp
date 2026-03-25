# Task 011: GetPageGraphics Tool and Integration Tests

## Description

Create the MCP tool class for `GetPageGraphics` and comprehensive integration tests that exercise the tool through the MCP protocol. The tool is a thin wrapper that validates the file path, delegates to the page graphics extraction service, and returns serialized JSON. Integration tests verify end-to-end behavior including tool discovery, successful extraction of rectangles/lines/paths, and error handling.

## Traces To

- **FRD:** FRD-004 (Page Graphics Extraction — GetPageGraphics)
- **PRD:** REQ-3 (Graphics extraction and classification), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Dependencies

- **Task 002** — MCP server host with stdio transport (complete)
- **Task 003** — Test project and server verification (complete)
- **Task 007** — GetPdfInfo tool and integration tests — establishes the MCP integration test pattern (complete)
- **Task 010** — GetPageGraphics service and DTOs (must be complete before this task)

## Technical Requirements

### Tool Class

Define a tool class in `Tools/` that:

1. Is decorated with the `[McpServerToolType]` attribute for automatic discovery.
2. Contains a single tool method decorated with `[McpServerTool]` and `[Description]` attributes.
3. Uses primary constructor to inject `IInputValidationService` and `IPageGraphicsService`.
4. Accepts two parameters, each with `[Description]` attributes:
   - `pdfPath` (string, required) — Absolute path to the PDF file
   - `page` (int, required) — 1-based page number
5. The tool method must:
   - Validate the file path using `IInputValidationService.ValidateFilePath()` — this is the **only** validation the tool performs directly; page number validation is handled inside the service (matching the established pattern)
   - Delegate to the page graphics service for extraction
   - Serialize the result using `JsonSerializer.Serialize()` with `SerializerConfig.Options`
   - Return the serialized JSON string
   - Catch `ArgumentException` and rethrow as `McpException` to preserve error messages for the agent

### Tool Description

The `[Description]` on the tool method must clearly communicate to AI agents:
- What the tool returns (classified graphic shapes: rectangles, lines, and complex paths with fill/stroke properties)
- That it operates on a single page
- The use cases (identifying table gridlines, sidebar backgrounds, callout box borders, section dividers, shaded regions)
- That PDF paths are classified into meaningful shapes (rectangles, lines, complex paths)

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema (pdfPath, page — no granularity parameter).
- [ ] Calling the tool on a page with rectangles returns classified rectangles with correct position, dimensions, and fill/stroke colors.
- [ ] Calling the tool on a page with lines returns classified lines with correct start/end points and stroke properties.
- [ ] Calling the tool on a page with complex paths returns path objects with bounding boxes and vertex counts, not raw operations.
- [ ] Stroke-only shapes have `strokeColor` and `strokeWidth` populated, `fillColor` omitted.
- [ ] Fill-only shapes have `fillColor` populated, `strokeColor` and `strokeWidth` omitted.
- [ ] Colors are represented as `"#RRGGBB"` regardless of the original PDF color space.
- [ ] Coordinates in the response are rounded to 1 decimal place.
- [ ] The response uses compact JSON with camelCase and null omission.
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error with "could not be opened as a PDF" in the message.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] Page number zero or negative returns an MCP error with "Page number must be 1 or greater."

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern from `GetPdfInfoIntegrationTests` and `GetPageTextIntegrationTests`:
- Launch the server as a child process communicating over stdio
- Perform MCP handshake (initialize → notifications/initialized) before tool calls
- Send `tools/call` requests with JSON-RPC protocol
- Validate responses with appropriate timeouts

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `GetPageGraphics` (or its snake_case equivalent) appears with the expected input schema including `pdfPath` and `page` parameters (but no `granularity` parameter).
2. **Rectangle extraction** — Call the tool on a test PDF page with known rectangles. Verify the response contains the correct page number, page dimensions, and at least one rectangle with `x`, `y`, `w`, `h`, and appropriate color fields.
3. **Line extraction** — Call the tool on a test PDF page with known lines. Verify the response contains at least one line with `x1`, `y1`, `x2`, `y2`, and `strokeColor` fields.
4. **Complex path extraction** — Call the tool on a test PDF page with at least one complex path. Verify the response contains a path object with `x`, `y`, `w`, `h`, and `vertexCount` fields.
5. **Fill color present** — Verify that a filled shape includes a `fillColor` field with a valid `"#RRGGBB"` value.
6. **Stroke color present** — Verify that a stroked shape includes a `strokeColor` field with a valid `"#RRGGBB"` value and a `strokeWidth` field.
7. **Color format validation** — Verify all returned color values match the `"#RRGGBB"` pattern (6-character hex prefixed with `#`).
8. **Dash pattern on dashed line** — Call the tool on a page with a dashed line. Verify the response includes a line with a non-null `dashPattern` string.
9. **Empty graphics page** — Call the tool on a test PDF page that has no drawn graphics (reuse an existing text-only or blank PDF from prior tasks, such as `sample-no-metadata.pdf`). Verify the response contains empty arrays for rectangles, lines, and paths.
10. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".
11. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".
12. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".
13. **Invalid PDF** — Call with a non-PDF file. Verify the error mentions the file could not be opened as a PDF.
14. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.
15. **Page number zero or negative** — Call with `page = 0`. Verify the error contains "Page number must be 1 or greater."

### Test Data Requirements

Integration tests must use test PDF files with **known, deterministic graphic content** generated by Task 010 (in `TestPdfGenerator`). The following test data contract is expected from Task 010:

| Test Data | Source | Used By Scenarios |
|-----------|--------|-------------------|
| Page with filled rectangles at known positions/colors | `TestPdfGenerator` via `DrawRectangle` | #2, #5 |
| Page with stroked lines at known coordinates | `TestPdfGenerator` via `DrawLine` | #3, #6 |
| Page with complex curved paths | `TestPdfGenerator` via `DrawCircle`/`DrawEllipsis` | #4 |
| Page with dashed lines | `TestPdfGenerator` via `DrawLine` with dash | #8 |
| Page with no drawn graphics (text-only or blank) | Existing `sample-no-metadata.pdf` from Task 006 | #9 |

All generated PDFs should be placed in `tests/TestData/`. If Task 010's test data does not cover a specific scenario, additional test data may be generated in the integration test setup.
