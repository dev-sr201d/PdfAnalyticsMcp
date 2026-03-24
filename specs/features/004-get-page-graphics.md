# FRD-004: Page Graphics Extraction (GetPageGraphics)

## Traces To

- **PRD:** REQ-3 (Graphics extraction and classification), REQ-6 (Data volume management), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Summary

Provide a tool that extracts and classifies all drawn graphic elements on a single PDF page. This is the **highest complexity feature** in the project. Raw PDF graphics operations are meaningless to an AI agent — the server must replay the graphics state machine, track colors and transforms, and classify the resulting paths into rectangles, lines, and complex shapes. This enables the agent to identify table gridlines, sidebar backgrounds, callout box borders, section dividers, and shaded regions.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |

## Outputs

A JSON object containing:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number returned |
| `width` | double | Page width in PDF points |
| `height` | double | Page height in PDF points |
| `rectangles` | array | Classified rectangle shapes |
| `lines` | array | Classified line segments |
| `paths` | array | Complex paths that are not simple rectangles or lines |

### Rectangle fields:

| Field | Type | Description |
|-------|------|-------------|
| `x` | double | Left edge X coordinate |
| `y` | double | Bottom edge Y coordinate |
| `w` | double | Width |
| `h` | double | Height |
| `fillColor` | string? | Fill color as `"#RRGGBB"` (null if not filled) |
| `strokeColor` | string? | Stroke color as `"#RRGGBB"` (null if not stroked) |
| `strokeWidth` | double? | Stroke line width (null if not stroked) |

### Line fields:

| Field | Type | Description |
|-------|------|-------------|
| `x1` | double | Start X |
| `y1` | double | Start Y |
| `x2` | double | End X |
| `y2` | double | End Y |
| `strokeColor` | string? | Stroke color as `"#RRGGBB"` |
| `strokeWidth` | double? | Line width |
| `dashPattern` | string? | Dash pattern description (null if solid) |

### Complex path fields:

| Field | Type | Description |
|-------|------|-------------|
| `x` | double | Bounding box left edge |
| `y` | double | Bounding box bottom edge |
| `w` | double | Bounding box width |
| `h` | double | Bounding box height |
| `fillColor` | string? | Fill color (null if not filled) |
| `strokeColor` | string? | Stroke color (null if not stroked) |
| `vertexCount` | int | Number of vertices in the path |

## Functional Requirements

1. The tool must operate on a single page per call (REQ-7).
2. The service layer must implement a **graphics state machine** that replays `page.Operations` from PdfPig, tracking:
   - Current transformation matrix (CTM) via `cm`, `q`/`Q` (save/restore) operations
   - Fill color via `g`/`rg`/`k` (grayscale/RGB/CMYK) operations
   - Stroke color via `G`/`RG`/`K` operations
   - Line width via `w` operation
   - Dash pattern via `d` operation
   - Path construction via `m` (moveto), `l` (lineto), `re` (rectangle) operations
   - Path painting via `S`/`s` (stroke), `f`/`F` (fill), `B`/`b` (fill+stroke), `n` (no-op) operations
3. Extracted paths must be **classified server-side** into rectangles, lines, and complex paths (REQ-6). Raw operations must never be returned.
4. A path is classified as a **rectangle** if it consists of a single `re` operation or four line segments forming a closed axis-aligned rectangle.
5. A path is classified as a **line** if it consists of a single `m` followed by a single `l` (two-point path).
6. All other paths are classified as **complex paths** with a bounding box and vertex count.
7. Colors must be normalized to `"#RRGGBB"` hex format regardless of the PDF color space (grayscale, RGB, CMYK).
8. All coordinates must be transformed through the CTM to produce page-space coordinates.
9. Coordinates must be rounded to 1 decimal place.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `UglyToad.PdfPig` NuGet package.
- This feature requires the most significant service-layer implementation (graphics state machine in `Services/`).

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options, coordinate rounding utility, color formatting utility, and input validation service.

## Acceptance Criteria

- [ ] Calling `GetPageGraphics` on a page with table gridlines returns classified lines with correct start/end points and stroke colors.
- [ ] Calling `GetPageGraphics` on a page with a colored sidebar returns a filled rectangle with the correct fill color and bounds.
- [ ] Calling `GetPageGraphics` on a page with bordered boxes returns stroked rectangles with correct stroke color and width.
- [ ] Complex paths (curves, irregular shapes) are returned with bounding boxes and vertex counts, not raw operations.
- [ ] Colors from different PDF color spaces (grayscale, RGB, CMYK) are all normalized to `"#RRGGBB"`.
- [ ] The graphics state machine correctly handles nested `q`/`Q` (save/restore) operations.
- [ ] Coordinates reflect the cumulative transformation matrix.
- [ ] The response uses compact JSON with camelCase, null omission, and 1 decimal coordinate rounding.
