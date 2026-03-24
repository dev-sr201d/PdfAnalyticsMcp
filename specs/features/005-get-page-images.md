# FRD-005: Page Image Extraction (GetPageImages)

## Traces To

- **PRD:** REQ-4 (Image extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Summary

Provide a tool that returns embedded images on a single PDF page with their bounding boxes and optional base64-encoded PNG data. The agent uses image positions to understand text flow around images, and the actual image data to include or reference images when converting to other formats.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |
| `includeData` | bool | No | `false` | If true, includes base64-encoded PNG image data in the response |

## Outputs

A JSON object containing:

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
| `bitsPerPixel` | int | Color depth |
| `data` | string? | Base64-encoded PNG data (only when `includeData = true`) |

## Functional Requirements

1. The tool must operate on a single page per call (REQ-7).
2. The tool must use `page.GetImages()` from PdfPig to enumerate embedded images.
3. Each image's bounding box must be extracted from `image.Bounds` (a `PdfRectangle`).
4. Pixel dimensions must be extracted from the image's intrinsic resolution properties.
5. When `includeData` is `false` (default), the `data` field must be omitted. This keeps responses small for the common case where the agent only needs to know image positions (REQ-6).
6. When `includeData` is `true`, the tool must attempt PNG conversion via `image.TryGetPng()`. If conversion fails for a particular image, that image's `data` field must be null and the remaining image metadata must still be returned.
7. Base64 data must be a standard base64 string (no data URI prefix).
8. Coordinates must be rounded to 1 decimal place.
9. If a page has no images, the `images` array must be empty (not null).

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `UglyToad.PdfPig` NuGet package.

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options, coordinate rounding utility, and input validation service.

## Acceptance Criteria

- [ ] Calling `GetPageImages` on a page with images returns bounding boxes and pixel dimensions for each image.
- [ ] Calling `GetPageImages` with `includeData = false` (or omitted) does not include any base64 data in the response.
- [ ] Calling `GetPageImages` with `includeData = true` includes base64-encoded PNG data for images where conversion succeeds.
- [ ] Images that cannot be converted to PNG still appear in the response with metadata but null `data`.
- [ ] A page with no images returns an empty `images` array.
- [ ] Coordinates are rounded to 1 decimal place.
- [ ] The response without image data for a typical page is well under 30 KB.
