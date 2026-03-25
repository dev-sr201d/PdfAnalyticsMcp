# FRD-003: Page Text Extraction (GetPageText)

## Traces To

- **PRD:** REQ-2 (Rich text extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Summary

Provide a tool that extracts text content from a single PDF page with full positional and stylistic metadata. This is the core tool for structural document understanding — the agent uses position, font, size, and color to determine reading order, identify headings, distinguish body text from annotations, and detect multi-column layouts.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |
| `granularity` | string | No | `"words"` | Level of detail: `"words"` or `"letters"` |
| `outputFile` | string | No | — | If provided, write the full JSON result to this file path and return a compact summary instead of the full data |

## Outputs

### Default (no `outputFile`)

A JSON object containing:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number returned |
| `width` | double | Page width in PDF points |
| `height` | double | Page height in PDF points |
| `elements` | array | Array of text elements (words or letters) |

### When `outputFile` is provided

The full JSON result (same structure as above) is written to the specified file path. The tool returns a compact summary instead:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number returned |
| `width` | double | Page width in PDF points |
| `height` | double | Page height in PDF points |
| `elementCount` | int | Number of text elements extracted |
| `outputFile` | string | Absolute path where the full data was written |
| `sizeBytes` | long | Size of the written file in bytes |

### Word element fields (granularity = `"words"`):

| Field | Type | Description |
|-------|------|-------------|
| `text` | string | The word text |
| `x` | double | Left edge X coordinate (PDF points) |
| `y` | double | Bottom edge Y coordinate (PDF points) |
| `w` | double | Width of bounding box |
| `h` | double | Height of bounding box |
| `font` | string | Font name |
| `size` | double | Font size in points |
| `color` | string? | RGB fill color as `"#RRGGBB"` (omitted if black/default) |
| `bold` | bool? | True if bold (omitted if false) |
| `italic` | bool? | True if italic (omitted if false) |

### Letter element fields (granularity = `"letters"`):

Same fields as word elements, but each element represents a single character. The `text` field contains a single character value.

## Functional Requirements

1. The tool must operate on a single page per call (REQ-7). It must use `document.GetPage(n)` for direct access, not iterate all pages.
2. Default granularity must be `"words"` — this produces ~5× less data than letter-level and is sufficient for most layout analysis (REQ-6).
3. Word extraction must use `page.GetWords()` from PdfPig. For letter-level, use `page.Letters`.
4. PdfPig's `Word` object does not directly expose font name, font size, or color. These must be **derived from the word's constituent `Letter` objects** (e.g., using the first letter's properties, or the most common values across all letters in the word).
5. Each text element must include its bounding box (x, y, w, h), font name, font size, and RGB fill color.
6. Bold and italic flags are **not explicitly available** from PdfPig. They must be **inferred heuristically from font name patterns** (e.g., `"Arial-Bold"`, `"TimesNewRoman-BoldItalic"`). This is best-effort — not all PDFs use predictable font naming conventions. Flags are only included when true (omitted when false to save payload).
7. Color must be serialized as `"#RRGGBB"` hex strings. Color values are available on `Letter.Color` via `.ToRGBValues()`. Default black (`#000000`) should be omitted to reduce payload size.
8. All coordinates must be rounded to 1 decimal place.
9. The response must be serialized as compact JSON (no indentation, camelCase, nulls omitted).
10. When `outputFile` is provided, the tool must write the complete JSON result to the specified file path and return a compact summary object (page dimensions, element count, file path, file size). The caller can then read the file independently.
11. The `outputFile` path must be validated: the directory must exist, the path must be absolute, and path traversal sequences must be rejected. If the file already exists, it is overwritten.
12. When `outputFile` is not provided, behavior is unchanged — the full JSON is returned inline as the tool result.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `UglyToad.PdfPig` NuGet package.
- Shared serialization configuration (ADR-0005).

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options, coordinate rounding utility, color formatting utility, and input validation service.

## Acceptance Criteria

- [ ] Calling `GetPageText` with `granularity = "words"` returns word-level text elements with bounding boxes, font, size, and color.
- [ ] Calling `GetPageText` with `granularity = "letters"` returns letter-level elements with the same metadata.
- [ ] Default granularity (parameter omitted) returns word-level data.
- [ ] Color is represented as `"#RRGGBB"` and omitted for default black text.
- [ ] Bold/italic flags are only present when true.
- [ ] Coordinates are rounded to 1 decimal place.
- [ ] A typical page (~300 words) at word granularity produces a response ≤ 30 KB.
- [ ] The tool only accesses the requested page, not the full document.
- [ ] When `outputFile` is provided, the full JSON is written to that path and the tool returns a summary containing page dimensions, element count, file path, and file size.
- [ ] When `outputFile` is provided, the inline response is small (< 1 KB) regardless of page density.
- [ ] When `outputFile` is omitted, behavior is identical to the current implementation (full data returned inline).
- [ ] The `outputFile` parameter rejects relative paths and path traversal sequences.
