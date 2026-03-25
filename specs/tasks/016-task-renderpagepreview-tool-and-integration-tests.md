# Task 016: RenderPagePreview Tool and Integration Tests

## Description

Create the MCP tool class for `RenderPagePreview` and comprehensive integration tests that exercise the tool through the MCP protocol. Unlike the other tools in this server, this tool returns multiple MCP content blocks: an `ImageContentBlock` containing the rendered PNG and a `TextContentBlock` containing JSON metadata. Integration tests verify end-to-end behavior including tool discovery, image rendering at various DPIs, content block structure, and error handling.

## Traces To

- **FRD:** FRD-006 (Page Rendering Preview — RenderPagePreview)
- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-5 (Page rendering), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK), ADR-0004 (Docnet/PDF rendering), ADR-0005 (Serialization)

## Dependencies

- **Task 002** — MCP server host with stdio transport (complete)
- **Task 003** — Test project and server verification (complete)
- **Task 007** — GetPdfInfo tool and integration tests — establishes the MCP integration test pattern (complete)
- **Task 015** — RenderPagePreview service and DTO (must be complete before this task)

## Technical Requirements

### Tool Class

Define a tool class in `Tools/` that:

1. Is decorated with the `[McpServerToolType]` attribute for automatic discovery.
2. Contains a single tool method decorated with `[McpServerTool]` and `[Description]` attributes.
3. Uses primary constructor to inject `IInputValidationService` and `IRenderPagePreviewService`.
4. Accepts three parameters, each with `[Description]` attributes:
   - `pdfPath` (string, required) — Absolute path to the PDF file
   - `page` (int, required) — 1-based page number
   - `dpi` (int, optional, default `150`) — Rendering resolution in DPI
5. The tool method must:
   - **Return `IEnumerable<ContentBlock>`** — This is different from all other tools which return `string`. The MCP C# SDK natively supports this return type and converts each content block into the tool response.
   - Validate the file path using `IInputValidationService.ValidateFilePath()`
   - Delegate to the rendering service, passing `pdfPath`, `page`, and `dpi`
   - Construct two content blocks from the service result:
     1. An `ImageContentBlock` created via `ImageContentBlock.FromBytes(result.PngData, "image/png")` — the SDK handles base64 encoding
     2. A `TextContentBlock` containing a JSON string with the metadata fields (`page`, `dpi`, `width`, `height`) serialized using `SerializerConfig.Options`
   - Return both content blocks as an `IEnumerable<ContentBlock>` (e.g., an array)
   - Catch `ArgumentException` and rethrow as `McpException` to preserve error messages for the agent

### Tool Description

The `[Description]` on the tool method must clearly communicate to AI agents:
- What the tool does (renders a single PDF page as a PNG image)
- That it returns a visual image that multimodal models can inspect directly
- That the default DPI is 150 and the valid range is 72–600
- The primary use case (visually verifying structural understanding of complex layouts)
- That the response also includes metadata (page, dpi, width, height) as a separate text block

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number
- `dpi`: That it controls rendering resolution (default 150, range 72–600); lower values produce smaller images, higher values produce sharper images

### Content Block Return Type

This tool's return type is `IEnumerable<ContentBlock>` from the `ModelContextProtocol` namespace, specifically including:
- `ImageContentBlock` — wraps base64-encoded image data with a MIME type. The SDK's `ImageContentBlock.FromBytes(byte[] data, string mimeType)` factory method handles encoding.
- `TextContentBlock` — wraps a plain text string. Constructed via `new TextContentBlock { Text = "..." }`.

The MCP SDK automatically converts `IEnumerable<ContentBlock>` into the `CallToolResult.Content` list in the JSON-RPC response.

### Metadata DTO

Define a small record type for the metadata JSON (this is internal to the tool, not a full response DTO):
- `page` (int)
- `dpi` (int)
- `width` (int)
- `height` (int)

Serialize using `SerializerConfig.Options` (camelCase, no nulls, no indentation).

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema (`pdfPath`, `page`, `dpi`).
- [ ] Calling the tool at default DPI returns two content blocks: one image and one text.
- [ ] The image content block has type `image` and MIME type `image/png`.
- [ ] The image content block contains valid base64 data that decodes to a PNG (starts with PNG signature).
- [ ] The text content block contains JSON with `page`, `dpi`, `width`, and `height` fields.
- [ ] The metadata `width` and `height` values match the actual dimensions of the rendered image.
- [ ] Calling the tool with a custom DPI (e.g., 72, 300) renders at the requested resolution and reports correct dimensions.
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error indicating the file could not be rendered.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] DPI below 72 or above 600 returns an MCP error with a descriptive message about the valid range.

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern from existing integration test classes: launch the server as a child process, perform MCP handshake, send `tools/call` JSON-RPC requests, and validate responses.

### Important: Response Structure Differences

Unlike the other tools (which return a single `TextContentBlock`), this tool returns **multiple content blocks** in the response. The integration tests must handle this:
- The `result.content` array will contain 2 elements
- Content blocks have a `type` field: `"image"` for `ImageContentBlock`, `"text"` for `TextContentBlock`
- `ImageContentBlock` has `data` (base64 string) and `mimeType` fields
- `TextContentBlock` has a `text` field containing the JSON metadata string

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `RenderPagePreview` (or its snake_case equivalent `render_page_preview`) appears with the expected input schema including `pdfPath`, `page`, and `dpi` parameters.

2. **Default DPI rendering** — Call the tool on a known test PDF (e.g., `sample-with-metadata.pdf`, page 1) without specifying `dpi`. Verify:
   - The response `content` array contains exactly 2 elements.
   - One element has `type` = `"image"`, `mimeType` = `"image/png"`, and non-empty `data`.
   - One element has `type` = `"text"` with parseable JSON containing `page`, `dpi`, `width`, `height`.
   - The metadata `dpi` value is 150 (default).

3. **Image data validity** — Decode the base64 image data from the image content block. Verify the decoded bytes start with the PNG signature `[137, 80, 78, 71, 13, 10, 26, 10]`.

4. **Metadata dimensions** — Verify the `width` and `height` in the metadata text block are positive integers that are plausible for the test PDF at the requested DPI.

5. **Custom DPI (72)** — Call with `dpi=72`. Verify the metadata reports `dpi=72` and the dimensions are smaller than the default (150 DPI) rendering.

6. **Custom DPI (300)** — Call with `dpi=300`. Verify the metadata reports `dpi=300` and the dimensions are larger than the default rendering.

7. **dpi omitted (default behavior)** — Call the tool without specifying `dpi` in the request arguments at all. Verify the response uses 150 DPI. This validates the MCP SDK correctly applies the default value for the optional integer parameter.

8. **Page 2 rendering** — Call with page 2 on a multi-page test PDF. Verify the metadata reports `page=2`.

9. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".

10. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".

11. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".

12. **Invalid PDF** — Call with `not-a-pdf.txt`. Verify the error indicates the file could not be rendered as a PDF.

13. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.

14. **Page number zero** — Call with `page=0`. Verify the error contains "Page number must be 1 or greater."

15. **DPI too low** — Call with `dpi=50`. Verify the error mentions the valid DPI range.

16. **DPI too high** — Call with `dpi=700`. Verify the error mentions the valid DPI range.

> **Note:** FRD-006 acceptance criteria include "Pages that PDFium cannot render produce a clear error message rather than empty/corrupt image data." This scenario is covered at the unit level in Task 015 (null/empty pixel data check) but is not integration-tested here because it requires a specially crafted PDF with a corrupted page stream, which is not feasible with standard test data. The unit-level guard in the service is sufficient.
