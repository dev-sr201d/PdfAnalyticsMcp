# FRD-004: Page Graphics Extraction (GetPageGraphics)

## Traces To

- **PRD:** REQ-3 (Graphics extraction and classification), REQ-6 (Data volume management), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Summary

Provide a tool that extracts and classifies all drawn graphic elements on a single PDF page. The service uses PdfPig's `page.Paths` API, which returns pre-processed paths with CTM transforms, fill/stroke colors, line widths, and dash patterns already resolved — including automatic recursion into Form XObjects. The service classifies each path into rectangles, lines, and complex shapes and normalizes colors to hex format. This enables the agent to identify table gridlines, sidebar backgrounds, callout box borders, section dividers, and shaded regions.

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
2. The service layer must use PdfPig's **`page.Paths`** API (`IReadOnlyList<PdfPath>`) to read pre-processed paths. PdfPig's internal `ContentStreamProcessor` handles graphics state tracking (CTM, colors, line styles) and automatically recurses into Form XObjects — no custom state machine is needed.
3. Clipping paths (`IsClipping == true`) and invisible paths (neither filled nor stroked) must be filtered out.
4. Extracted paths must be **classified server-side** into rectangles, lines, and complex paths (REQ-6). Raw operations must never be returned.
5. A path is classified as a **rectangle** if it has exactly one subpath with `Move` + 3 `Line` + `Close` (or `Move` + 4 `Line` with coincident endpoints) forming a closed axis-aligned shape with no curves.
6. A path is classified as a **line** if it has exactly one subpath with 1 `Move` + 1 `Line` (two-point path, no close, no curves).
7. All other paths are classified as **complex paths** with a bounding box and vertex count.
8. Colors must be extracted from `PdfPath.FillColor` and `PdfPath.StrokeColor` (type `IColor`) via `ToRGBValues()` and normalized to `"#RRGGBB"` hex format. `PatternColor` types must be handled gracefully (treated as null).
9. Coordinates are already in page space (CTM pre-applied by PdfPig) and must be rounded to 1 decimal place.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `UglyToad.PdfPig` NuGet package.
- The service classifies PdfPig's pre-processed `PdfPath` objects.

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options, coordinate rounding utility, color formatting utility, and input validation service.

## Acceptance Criteria

- [ ] Calling `GetPageGraphics` on a page with table gridlines returns classified lines with correct start/end points and stroke colors.
- [ ] Calling `GetPageGraphics` on a page with a colored sidebar returns a filled rectangle with the correct fill color and bounds.
- [ ] Calling `GetPageGraphics` on a page with bordered boxes returns stroked rectangles with correct stroke color and width.
- [ ] Complex paths (curves, irregular shapes) are returned with bounding boxes and vertex counts, not raw operations.
- [ ] Colors from different PDF color spaces (grayscale, RGB, CMYK) are all normalized to `"#RRGGBB"` via `IColor.ToRGBValues()`.
- [ ] Graphics inside Form XObjects are included (handled automatically by PdfPig's `page.Paths`).
- [ ] Clipping paths and invisible paths are excluded from the output.
- [ ] Coordinates are correctly rounded (CTM transforms are pre-applied by PdfPig).
- [ ] The response uses compact JSON with camelCase, null omission, and 1 decimal coordinate rounding.
