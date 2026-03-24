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

A JSON object containing:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number rendered |
| `dpi` | int | The DPI used for rendering |
| `width` | int | Image width in pixels |
| `height` | int | Image height in pixels |
| `data` | string | Base64-encoded PNG image data |

## Functional Requirements

1. The tool must operate on a single page per call (REQ-7).
2. The tool must use Docnet (`Docnet.Core`) to render the page, which wraps the PDFium rendering engine.
3. Docnet uses **0-based page indexing** — the tool must subtract 1 from the user-provided 1-based page number.
4. The default DPI must be 150, providing a good balance between visual clarity and data size. A US Letter page at 150 DPI produces a ~1275×1650 pixel image.
5. Docnet's `PageDimensions` constructor accepts **pixel dimensions** (`width`, `height`), **not DPI directly**. The tool must convert the requested DPI to pixel dimensions using the formula: `pixelWidth = (int)(pageWidthPts * dpi / 72)`, `pixelHeight = (int)(pageHeightPts * dpi / 72)`. The page size in points can be obtained from PdfPig or by first querying Docnet with a known dimension.
6. Docnet's `GetImage()` returns raw **BGRA pixel data** (4 bytes per pixel). The tool must encode this to PNG format before base64-encoding.
7. The PNG encoding must use a lightweight approach — not a heavy imaging library.
8. The base64-encoded PNG must be returned as a standard base64 string.
9. Both `IDocReader` and `IPageReader` must be disposed after use (they hold native resources).
10. The tool should accept a reasonable DPI range (e.g., 72–600) and reject values outside that range.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `Docnet.Core` NuGet package (introduces native PDFium dependency).
- A PNG encoding capability (lightweight library or manual PNG writer for BGRA-to-PNG conversion).

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options and input validation service. It does not use PdfPig or the coordinate/color utilities (Docnet operates independently).

## Acceptance Criteria

- [ ] Calling `RenderPagePreview` with default DPI renders the page at 150 DPI and returns a valid base64-encoded PNG.
- [ ] Calling `RenderPagePreview` with a custom DPI (e.g., 72, 300) renders at the requested resolution.
- [ ] The returned `width` and `height` match the actual pixel dimensions of the rendered image.
- [ ] The base64 data decodes to a valid PNG image.
- [ ] Native resources (IDocReader, IPageReader) are properly disposed.
- [ ] The tool correctly translates 1-based page numbers to Docnet's 0-based indexing.
- [ ] DPI values outside a reasonable range are rejected with a clear error message.
