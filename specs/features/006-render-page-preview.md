# FRD-006: Page Rendering Preview (RenderPagePreview)

## Traces To

- **PRD:** REQ-5 (Page rendering), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0004 (Docnet/PDF rendering), ADR-0005 (Serialization)

## Summary

Provide a tool that renders a single PDF page as a PNG image at a configurable DPI. This enables multimodal AI models to visually see the page layout and cross-reference visual appearance with the structured data from other tools. This is an optional but high-value capability — it is the most useful tool for validating structural understanding of complex layouts.

This feature introduces a separate dependency (Docnet/PDFium) from the core PdfPig-based tools.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |
| `dpi` | int | No | `150` | Rendering resolution in dots per inch |

## Outputs

The tool must return its response as multiple MCP content blocks:

1. **Image content block** — The rendered PNG image, returned as an MCP `ImageContentBlock` with MIME type `image/png`. This allows multimodal AI models to directly consume the image without parsing JSON.
2. **Text content block** — A JSON object containing rendering metadata:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number rendered |
| `dpi` | int | The DPI used for rendering |
| `width` | int | Image width in pixels |
| `height` | int | Image height in pixels |

> **Rationale:** The MCP C# SDK natively supports `ImageContentBlock` as a tool return type. Returning the image as a proper image content block (rather than base64-encoded data embedded in JSON) is more effective for multimodal models — they can consume the image directly without parsing. The metadata is returned as a separate text content block. The tool method should return `IEnumerable<ContentBlock>` to provide both.

## Functional Requirements

1. The tool must operate on a single page per call (REQ-7).
2. The tool must use Docnet (`Docnet.Core`) to render the page, which wraps the PDFium rendering engine.
3. Docnet uses **0-based page indexing** — the tool must subtract 1 from the user-provided 1-based page number.
4. The default DPI must be 150, providing a good balance between visual clarity and data size. A US Letter page at 150 DPI produces a ~1275×1650 pixel image.
5. Docnet's `GetDocReader` must be opened using the **scaling factor overload** (`DocLib.Instance.GetDocReader(path, new PageDimensions(scalingFactor))`), where `scalingFactor = dpi / 72.0`. The `PageDimensions(double scalingFactor)` constructor wraps the scaling factor for the `GetDocReader` API. This directly achieves the desired rendering DPI without needing to know the page's intrinsic point dimensions upfront. This eliminates any dependency on PdfPig for this tool.
6. Docnet's `GetImage()` returns raw **BGRA pixel data** (4 bytes per pixel) rendered onto a **transparent buffer**. The PNG encoder must composite the alpha channel against a white background before encoding, producing an opaque RGB PNG. This matches standard PDF viewer behavior — PDF pages have no explicit background color property; viewers conventionally render on white. Without this compositing, pages lacking a drawn background rectangle (which is common) would appear as text on a transparent background.
7. The PNG encoding must use a lightweight approach — not a heavy imaging library. A manual PNG writer using `System.IO.Compression.ZLibStream` (built into .NET 6+) is sufficient for encoding the composited RGB pixel data and avoids adding any external imaging dependency.
8. Both `IDocReader` and `IPageReader` must be disposed after use (they hold native resources).
9. The tool must accept a DPI range of 72–600. Values outside this range must be rejected with a clear error message.

## Response Size Considerations

This tool intentionally produces larger responses than the other tools in this server. A typical US Letter page at 150 DPI produces a PNG image of approximately 200–800 KB (varying by content complexity). This exceeds the ≤30 KB guidance for text-based tools but is inherent to the tool's purpose — providing a visual preview. The 30 KB target from REQ-6 applies to text/graphics/image metadata tools, not to full-page rendering.

To manage data volume:
- The default DPI of 150 provides a reasonable balance between visual clarity and size.
- The minimum DPI of 72 allows agents to request a lightweight preview (~50–200 KB) when bandwidth is a concern.
- The maximum DPI of 600 is capped to prevent excessive memory usage during rendering. At 600 DPI, a Letter page requires ~134 MB of raw BGRA pixel data before PNG compression.

## Error Handling

1. If the PDF file cannot be opened by Docnet/PDFium, the tool must return a clear error indicating the file could not be rendered.
2. If the requested page number is out of range, the tool must return a clear error with the valid page range.
3. If PDFium encounters a rendering failure on a valid page (e.g., corrupted page content, unsupported PDF features), the tool must return a clear error rather than returning an empty or corrupt image.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `Docnet.Core` NuGet package (introduces native PDFium dependency).
- A PNG encoding capability — lightweight, using only built-in .NET APIs (`System.IO.Compression`) without requiring an external imaging library.

> **Note:** This feature reuses the shared input validation service established by Feature 002. It does **not** depend on PdfPig — Docnet's scaling factor API provides all necessary rendering capabilities independently. It does not use the serialization config, coordinate utilities, or color utilities from other features, except for the metadata JSON text block.

## Acceptance Criteria

- [ ] Calling `RenderPagePreview` with default DPI renders the page at 150 DPI and returns a valid PNG as an MCP image content block.
- [ ] Calling `RenderPagePreview` with a custom DPI (e.g., 72, 300) renders at the requested resolution.
- [ ] The metadata text block contains correct `width`, `height`, `page`, and `dpi` values matching the rendered image.
- [ ] The image content block contains a valid PNG image that can be decoded by standard image viewers.
- [ ] Native resources (IDocReader, IPageReader) are properly disposed.
- [ ] The tool correctly translates 1-based page numbers to Docnet's 0-based indexing.
- [ ] DPI values outside the 72–600 range are rejected with a clear error message.
- [ ] Invalid or corrupted PDF files produce a clear error message, not a crash.
- [ ] Pages that PDFium cannot render produce a clear error message rather than empty/corrupt image data.
