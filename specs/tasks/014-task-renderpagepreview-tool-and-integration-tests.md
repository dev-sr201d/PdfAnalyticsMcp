# Task 014: RenderPagePreview Tool and Integration Tests

## Description

Create the MCP tool class for `RenderPagePreview` and comprehensive integration tests that exercise the tool through the MCP protocol. Unlike the other tools in this server, this tool returns multiple MCP content blocks: an `ImageContentBlock` containing the rendered image (PNG or JPEG) and a `TextContentBlock` containing JSON metadata. The tool accepts configurable format and quality parameters in addition to DPI. Integration tests verify end-to-end behavior including tool discovery, image rendering at various DPIs and formats, content block structure, and error handling.

## Traces To

- **FRD:** FRD-005 (Page Rendering — RenderPagePreview)
- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-5 (Page rendering), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK), ADR-0004 (PDF Page Rendering and Image Encoding), ADR-0005 (Serialization)

## Dependencies

- **Task 002** — MCP server host with stdio transport (complete)
- **Task 003** — Test project and server verification (complete)
- **Task 007** — GetPdfInfo tool and integration tests — establishes the MCP integration test pattern (complete)
- **Task 013** — RenderPagePreview service and DTO (must be complete before this task)

## Technical Requirements

### Tool Class

Define a tool class in `Tools/` that:

1. Is decorated with the `[McpServerToolType]` attribute for automatic discovery.
2. Contains a single tool method decorated with `[McpServerTool]` and `[Description]` attributes.
3. Uses primary constructor to inject `IInputValidationService` and `IRenderPagePreviewService`.
4. Accepts five parameters, each with `[Description]` attributes:
   - `pdfPath` (string, required) — Absolute path to the PDF file
   - `page` (int, required) — 1-based page number
   - `dpi` (int, optional, default `150`) — Rendering resolution in DPI
   - `format` (string, optional, default `"png"`) — Output image format: `"png"`, `"jpeg"`, or `"jpg"`
   - `quality` (int, optional, default `80`) — Image quality (1–100; controls JPEG compression, ignored for PNG)
5. The tool method must:
   - **Return `IEnumerable<ContentBlock>`** — This is different from all other tools which return `string`. The MCP C# SDK natively supports this return type and converts each content block into the tool response.
   - Validate the file path using `IInputValidationService.ValidateFilePath()`
   - Delegate to the rendering service, passing `pdfPath`, `page`, `dpi`, `format`, and `quality`
   - Construct two content blocks from the service result:
     1. An `ImageContentBlock` created via `ImageContentBlock.FromBytes(result.ImageData, result.MimeType)` — the SDK handles base64 encoding
     2. A `TextContentBlock` containing a JSON string with the metadata fields (`page`, `dpi`, `format`, `quality`, `width`, `height`, `sizeBytes`) serialized using `SerializerConfig.Options`
   - Return both content blocks as an `IEnumerable<ContentBlock>` (e.g., an array)
   - Catch `ArgumentException` and rethrow as `McpException` to preserve error messages for the agent

### Tool Description

The `[Description]` on the tool method must clearly communicate to AI agents:
- What the tool does (renders a single PDF page as an image)
- That it supports PNG (lossless) and JPEG (lossy, smaller) output formats
- That the default format is PNG and the default quality is 80
- That it returns a visual image that multimodal models can inspect directly
- That the default DPI is 150 and the valid range is 72–600
- The primary use case (visually verifying structural understanding of complex layouts)
- That JPEG is recommended for pages with photographic content to reduce file size
- That the response also includes metadata (page, dpi, format, quality, width, height, sizeBytes) as a separate text block

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number
- `dpi`: That it controls rendering resolution (default 150, range 72–600); lower values produce smaller images, higher values produce sharper images
- `format`: That it selects the output image format; valid values are `"png"` (lossless, default), `"jpeg"` or `"jpg"` (lossy, smaller file size); case-insensitive
- `quality`: That it controls image quality from 1 (smallest file) to 100 (highest quality); directly controls JPEG compression; ignored for PNG; default is 80

### Content Block Return Type

This tool's return type is `IEnumerable<ContentBlock>` from the `ModelContextProtocol` namespace, specifically including:
- `ImageContentBlock` — wraps base64-encoded image data with a MIME type. The SDK's `ImageContentBlock.FromBytes(byte[] data, string mimeType)` factory method handles encoding. The MIME type must be `"image/png"` or `"image/jpeg"` matching the actual format.
- `TextContentBlock` — wraps a plain text string. Constructed via `new TextContentBlock { Text = "..." }`.

The MCP SDK automatically converts `IEnumerable<ContentBlock>` into the `CallToolResult.Content` list in the JSON-RPC response.

### Metadata DTO

Define a small record type for the metadata JSON (this is internal to the tool, not a full response DTO):
- `page` (int)
- `dpi` (int)
- `format` (string) — the normalized format name (`"png"` or `"jpeg"`)
- `quality` (int) — the quality value used
- `width` (int)
- `height` (int)
- `sizeBytes` (int) — the encoded image size in bytes

Serialize using `SerializerConfig.Options` (camelCase, no nulls, no indentation).

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema (`pdfPath`, `page`, `dpi`, `format`, `quality`).
- [ ] Calling the tool at default parameters returns two content blocks: one image (PNG) and one text.
- [ ] The image content block has type `image` and MIME type `image/png` when format is PNG.
- [ ] The image content block has type `image` and MIME type `image/jpeg` when format is JPEG.
- [ ] The image content block contains valid base64 data that decodes to the correct format (PNG signature or JPEG SOI marker).
- [ ] The text content block contains JSON with `page`, `dpi`, `format`, `quality`, `width`, `height`, and `sizeBytes` fields.
- [ ] The metadata `width` and `height` values match the actual dimensions of the rendered image.
- [ ] The metadata `format` is normalized (`"png"` or `"jpeg"`, never `"jpg"`).
- [ ] The metadata `sizeBytes` matches the actual encoded image size.
- [ ] Calling the tool with a custom DPI (e.g., 72, 300) renders at the requested resolution and reports correct dimensions.
- [ ] Calling the tool with `format="jpeg"` and `quality=60` produces valid JPEG output smaller than quality=100.
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error indicating the file could not be rendered.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] DPI below 72 or above 600 returns an MCP error with a descriptive message about the valid range.
- [ ] Invalid format value returns an MCP error listing valid options.
- [ ] Quality outside 1–100 returns an MCP error with a descriptive message.

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern. The test class inherits from `McpIntegrationTestBase` (created in Task 003), using its `PerformHandshakeAsync()`, `CallToolAsync()`, and protocol helper methods.

### Important: Response Structure Differences

Unlike the other tools (which return a single `TextContentBlock`), this tool returns **multiple content blocks** in the response. The integration tests must handle this:
- The `result.content` array will contain 2 elements
- Content blocks have a `type` field: `"image"` for `ImageContentBlock`, `"text"` for `TextContentBlock`
- `ImageContentBlock` has `data` (base64 string) and `mimeType` fields (either `"image/png"` or `"image/jpeg"`)
- `TextContentBlock` has a `text` field containing the JSON metadata string

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `RenderPagePreview` (or its snake_case equivalent `render_page_preview`) appears with the expected input schema including `pdfPath`, `page`, `dpi`, `format`, and `quality` parameters.

2. **Default rendering (PNG)** — Call the tool on a known test PDF (e.g., `sample-with-metadata.pdf`, page 1) without specifying `dpi`, `format`, or `quality`. Verify:
   - The response `content` array contains exactly 2 elements.
   - One element has `type` = `"image"`, `mimeType` = `"image/png"`, and non-empty `data`.
   - One element has `type` = `"text"` with parseable JSON containing `page`, `dpi`, `format`, `quality`, `width`, `height`, `sizeBytes`.
   - The metadata `dpi` value is 150 (default), `format` is `"png"`, `quality` is 80 (default).

3. **PNG image data validity** — Decode the base64 image data from the image content block. Verify the decoded bytes start with the PNG signature `[137, 80, 78, 71, 13, 10, 26, 10]`.

4. **JPEG rendering** — Call with `format="jpeg"`. Verify:
   - The image content block has `mimeType` = `"image/jpeg"`.
   - The decoded base64 data starts with the JPEG SOI marker `[0xFF, 0xD8]`.
   - The metadata `format` is `"jpeg"`.

5. **JPEG with "jpg" alias** — Call with `format="jpg"`. Verify the response succeeds with `mimeType` = `"image/jpeg"` and metadata `format` = `"jpeg"`.

6. **Format case-insensitivity** — Call with `format="JPEG"`. Verify it succeeds with the correct MIME type.

7. **JPEG quality control** — Call with `format="jpeg"` and `quality=60`, then with `quality=100`. Verify the `sizeBytes` in metadata is smaller for quality=60.

8. **Metadata dimensions** — Verify the `width` and `height` in the metadata text block are positive integers that are plausible for the test PDF at the requested DPI.

9. **Metadata sizeBytes** — Verify `sizeBytes` in the metadata is a positive integer matching the decoded base64 data length.

10. **Custom DPI (72)** — Call with `dpi=72`. Verify the metadata reports `dpi=72` and the dimensions are smaller than the default (150 DPI) rendering.

11. **Custom DPI (300)** — Call with `dpi=300`. Verify the metadata reports `dpi=300` and the dimensions are larger than the default rendering.

12. **dpi omitted (default behavior)** — Call the tool without specifying `dpi` in the request arguments at all. Verify the response uses 150 DPI. This validates the MCP SDK correctly applies the default value for the optional integer parameter.

13. **Page 2 rendering** — Call with page 2 on a multi-page test PDF. Verify the metadata reports `page=2`.

14. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".

15. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".

16. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".

17. **Invalid PDF** — Call with `not-a-pdf.txt`. Verify the error indicates the file could not be rendered as a PDF.

18. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.

19. **Page number zero** — Call with `page=0`. Verify the error contains "Page number must be 1 or greater."

20. **DPI too low** — Call with `dpi=50`. Verify the error mentions the valid DPI range.

21. **DPI too high** — Call with `dpi=700`. Verify the error mentions the valid DPI range.

22. **Invalid format** — Call with `format="bmp"`. Verify the error lists valid format options.

23. **Quality too low** — Call with `quality=0`. Verify the error mentions the valid quality range.

24. **Quality too high** — Call with `quality=101`. Verify the error mentions the valid quality range.

> **Note:** FRD-005 acceptance criteria include "Pages that PDFium cannot render produce a clear error message rather than empty/corrupt image data." This scenario is covered at the unit level in Task 013 (null/empty pixel data check) but is not integration-tested here because it requires a specially crafted PDF with a corrupted page stream, which is not feasible with standard test data. The unit-level guard in the service is sufficient. Similarly, the "JPEG output for a page with photographic content is significantly smaller than PNG" FRD acceptance criterion is covered at the unit level in Task 013 (JPEG quality affects size test).
