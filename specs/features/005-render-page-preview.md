# FRD-005: Page Rendering (RenderPagePreview)

## Traces To

- **PRD:** REQ-5 (Page rendering), REQ-6 (Page-by-page processing), REQ-7 (Robust error handling), REQ-9 (Concurrent tool safety)
- **ADRs:** ADR-0004 (PDFiumCore/PDF rendering), ADR-0005 (Serialization)

## Summary

Provide a rendering service and MCP tool that renders a single PDF page as an image at a configurable DPI. The tool supports both **PNG** (lossless) and **JPEG** (lossy, smaller) output formats, with a quality parameter that controls compression. This enables multimodal AI models to visually see the page layout and cross-reference visual appearance with the structured data from other tools. This is an optional but high-value capability — it is the most useful tool for validating structural understanding of complex layouts.

JPEG output is particularly important for pages with large multi-colored images or photographic content, where lossless PNG can produce files of several megabytes — too large for some AI agent endpoints. JPEG with moderate quality (e.g., 60–80) typically reduces these to a fraction of the size while remaining fully usable for layout verification.

This feature introduces a separate dependency (PDFiumCore/PDFium) from the core PdfPig-based tools.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |
| `dpi` | int | No | `150` | Rendering resolution in dots per inch |
| `format` | string | No | `"jpeg"` | Output image format: `"png"` (lossless) or `"jpeg"`/`"jpg"` (lossy, smaller file size). Case-insensitive. |
| `quality` | int | No | `80` | Image quality from 1 (smallest file, lowest quality) to 100 (largest file, highest quality). For JPEG, this directly controls compression level. For PNG, this parameter is accepted but ignored — PNG always uses maximum compression effort since it is lossless and compression level has negligible impact on file size compared to the PNG-to-JPEG savings that motivate format selection. |

## Outputs

The tool must return its response as multiple MCP content blocks:

1. **Image content block** — The rendered image, returned as an MCP `ImageContentBlock` with the appropriate MIME type (`image/png` or `image/jpeg`). This allows multimodal AI models to directly consume the image without parsing JSON.
2. **Text content block** — A JSON object containing rendering metadata:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number rendered |
| `dpi` | int | The DPI used for rendering |
| `format` | string | The image format used (`"png"` or `"jpeg"`) |
| `quality` | int | The quality value used |
| `width` | int | Image width in pixels |
| `height` | int | Image height in pixels |
| `sizeBytes` | int | The encoded image size in bytes |

> **Rationale:** The MCP C# SDK natively supports `ImageContentBlock` as a tool return type. Returning the image as a proper image content block (rather than base64-encoded data embedded in JSON) is more effective for multimodal models — they can consume the image directly without parsing. The metadata is returned as a separate text content block. The tool method should return `IEnumerable<ContentBlock>` to provide both. Including `format`, `quality`, and `sizeBytes` in metadata helps agents evaluate whether the chosen format and quality produced an acceptable result and adjust parameters on subsequent calls.

## Functional Requirements

### Rendering Engine

1. The tool must operate on a single page per call (REQ-6).
2. The tool must use PDFiumCore (`PDFiumCore` NuGet package) to render the page via the PDFium rendering engine.
3. The tool must correctly translate user-facing 1-based page numbers to the rendering engine's internal indexing.
4. The default DPI must be 150, providing a good balance between visual clarity and data size. A US Letter page at 150 DPI produces a ~1275×1650 pixel image. Pixel dimensions must be computed deterministically for any DPI value, including non-multiples of 72.
5. The rendering engine must render the page at the requested DPI without requiring PdfPig or any other PDF parsing library. The DPI parameter alone must be sufficient to determine the output resolution.
6. Rendered output must be an opaque image composited against a white background, matching standard PDF viewer behavior.
7. All native rendering resources must be released reliably after each call, even when errors occur. The rendering engine holds unmanaged resources that will leak if not explicitly cleaned up.
8. The tool must accept a DPI range of 72–600. Values outside this range must be rejected with a clear error message.
9. The rendering engine's native libraries are not safe for concurrent use. Concurrent rendering requests (and any future tools sharing the same native engine) must be serialized to ensure stability. Other tool types (text, graphics) are not affected by this constraint.

### Output Format

10. The tool must support two output formats: **PNG** (lossless) and **JPEG** (lossy). The default format must be `"jpeg"` because JPEG produces significantly smaller files than PNG for pages with visual content — the common case for layout verification — and is far more practical for multimodal AI agent endpoints that have response size limits. A default quality of 80 provides a good balance between file size and visual fidelity.
11. The `format` parameter must be case-insensitive. The values `"png"`, `"jpeg"`, and `"jpg"` are accepted; any other value must be rejected with a clear error message listing the valid options. `"jpg"` is treated as an alias for `"jpeg"` — both produce identical JPEG output.
12. The `quality` parameter must accept values from 1 to 100 inclusive. Values outside this range must be rejected with a clear error message.
13. For **JPEG** output, the quality parameter directly controls JPEG compression level (1 = maximum compression / lowest quality, 100 = minimum compression / highest quality). A default quality of 80 provides a good balance between file size and visual fidelity for layout verification.
14. For **PNG** output, the quality parameter is accepted for interface consistency but has no effect. PNG output is always lossless and always uses maximum compression effort. The significant size reduction comes from selecting JPEG format instead.
15. The MIME type in the returned `ImageContentBlock` must match the actual format: `image/png` for PNG, `image/jpeg` for JPEG.
16. The tool must return its response as an `IEnumerable<ContentBlock>` containing two content blocks: an `ImageContentBlock` with the rendered image data and appropriate MIME type, and a `TextContentBlock` with the JSON metadata object. This multi-block return type is distinct from all other tools in this server, which return text only.

### Cancellation

17. The tool must support cancellation via the MCP client's cancellation signal. If a rendering request is cancelled while waiting for access to the serialized rendering engine, it must stop waiting and return promptly rather than blocking until the current render completes.
18. If a rendering request is cancelled during the rendering operation itself, the tool must release rendering engine resources and return promptly.

### Resource Safety

19. If the rendering engine fails to allocate memory for the pixel buffer (e.g., at high DPI values on memory-constrained systems), the tool must return a clear error message rather than crashing the server process.

## Response Size Considerations

This tool intentionally produces larger responses than the other tools in this server. A typical US Letter page at 150 DPI produces a PNG image of approximately 200–800 KB (varying by content complexity), but pages with large multi-colored images or photographic content can produce PNG files of 2–3 MB or more. This exceeds the ≤30 KB guidance for text-based tools but is inherent to the tool's purpose — providing a visual preview. The 30 KB target from NFR-1 applies to text/graphics/image metadata tools, not to full-page rendering.

To manage data volume:
- The default DPI of 150 provides a reasonable balance between visual clarity and size.
- The minimum DPI of 72 allows agents to request a lightweight preview (~50–200 KB) when bandwidth is a concern.
- The maximum DPI of 600 is capped to prevent excessive memory usage during rendering. At 600 DPI, a Letter page requires ~134 MB of raw BGRA pixel data before PNG compression.
- **JPEG format** significantly reduces file size for pages with photographic or multi-colored content. A page that produces a 2–3 MB PNG may compress to 200–500 KB as JPEG at quality 80, making it suitable for agent endpoints with size constraints.
- The **quality parameter** provides further control: agents can reduce quality to 60 or lower for layout verification where pixel-perfect fidelity is not needed.

## Error Handling

Standard file path and page number validation rules apply as defined in FRD-007. The error messages for file access failures and invalid PDF format errors must be consistent with the patterns specified there, even though this tool uses PDFiumCore rather than PdfPig.

1. If the PDF file cannot be opened by PDFiumCore, the tool must return a clear error indicating the file could not be rendered.
2. If the requested page number is out of range, the tool must return a clear error with the valid page range.
3. If PDFium encounters a rendering failure on a valid page (e.g., corrupted page content, unsupported PDF features), the tool must return a clear error rather than returning an empty or corrupt image.
4. If the rendering operation fails due to memory allocation failure (e.g., insufficient memory for the pixel buffer at high DPI), the tool must return a clear error rather than crashing the server.
5. If the rendering engine is unavailable due to missing native platform support, the tool must return a clear error when invoked rather than crashing the server. The server itself must start successfully.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `PDFiumCore` NuGet package (introduces native PDFium dependency).
- A PNG encoding capability — lightweight, without requiring an external imaging library.
- A JPEG encoding capability with 1–100 quality control. See ADR-0004 for library selection.

## Introduced Infrastructure

This is the first feature to use PDFiumCore. Its implementation introduces a shared PDFium service that manages PDFium library lifecycle (`FPDF_InitLibrary` / `FPDF_DestroyLibrary`), thread serialization via a semaphore, and document/page loading with consistent error handling (see ADR-0004). This shared service becomes available infrastructure for subsequent PDFiumCore-based features (e.g., FRD-006 GetPageImages).

> **Note:** This feature reuses the shared input validation service established by Feature 002. It does **not** depend on PdfPig — PDFiumCore's rendering API provides all necessary rendering capabilities independently. It does not use the serialization config, coordinate utilities, or color utilities from other features, except for the metadata JSON text block.

## Acceptance Criteria

### Tool
- [ ] Calling `RenderPagePreview` with default parameters renders the page at 150 DPI as JPEG at quality 80 and returns a valid image as an MCP image content block.
- [ ] Calling `RenderPagePreview` with a custom DPI (e.g., 72, 300) renders at the requested resolution.
- [ ] Calling `RenderPagePreview` with `format = "jpeg"` returns a valid JPEG image with MIME type `image/jpeg`.
- [ ] Calling `RenderPagePreview` with `format = "png"` returns a valid PNG image with MIME type `image/png`.
- [ ] The `format` parameter is case-insensitive (e.g., `"JPEG"`, `"Png"`, `"jpg"` are accepted).
- [ ] `"jpg"` produces identical output to `"jpeg"`.
- [ ] An invalid `format` value (e.g., `"bmp"`, `"gif"`) is rejected with a clear error message listing valid options.
- [ ] The `quality` parameter controls JPEG compression: lower values produce smaller files.
- [ ] The `quality` parameter outside the 1–100 range is rejected with a clear error message.
- [ ] The metadata text block contains correct `width`, `height`, `page`, `dpi`, `format`, `quality`, and `sizeBytes` values matching the rendered image.
- [ ] The image content block contains a valid image that can be decoded by standard image viewers in the returned format.
- [ ] JPEG output for a page with photographic content is significantly smaller than PNG output at the same DPI.
- [ ] Native resources (PDFium document, page, bitmap handles) are properly released.
- [ ] The tool correctly translates 1-based page numbers to PDFium's 0-based indexing.
- [ ] DPI values outside the 72–600 range are rejected with a clear error message.
- [ ] Invalid or corrupted PDF files produce a clear error message, not a crash.
- [ ] Pages that PDFium cannot render produce a clear error message rather than empty/corrupt image data.
- [ ] When multiple `RenderPagePreview` calls are invoked in parallel, each call succeeds independently — no crashes or corrupted output from concurrent native library access.
- [ ] A cancelled rendering request that is waiting for the serialized rendering engine returns promptly without blocking.
- [ ] Error messages for file access and invalid PDF errors are consistent with the patterns defined in FRD-007, despite using a different underlying engine (PDFiumCore vs. PdfPig).
- [ ] A rendering request at maximum DPI (600) that fails due to memory constraints returns a clear error, not a server crash.
- [ ] If the native rendering engine is unavailable on the current platform, the tool returns a clear error and the server remains operational.

## Implementation Notes

> The following notes provide guidance for implementers. They are informational and not normative requirements. Each point is elaborated in the referenced task spec.

- **PNG encoding** should not introduce a heavy external imaging library dependency. A lightweight approach using only built-in .NET APIs is sufficient. See ADR-0004 and Task 012.
- **JPEG encoding** should support a 1–100 quality range with direct control over compression level. See ADR-0004 and Task 012a.
- The rendering service method should accept `format` and `quality` parameters alongside `pdfPath`, `page`, `dpi`, and `CancellationToken`. It should return the encoded image bytes and MIME type so the tool layer can construct the correct `ImageContentBlock`. See Task 013.
- The rendering result model should include the MIME type and encoded image size to support the metadata DTO fields (`format`, `quality`, `sizeBytes`). See Tasks 013 and 014.
