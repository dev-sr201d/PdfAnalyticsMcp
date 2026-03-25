# ADR-0002: PDF Parsing Library

## Status

Accepted

## Context

The MCP server needs a library to extract rich structured data from PDF files, including:

- Document metadata: page count, dimensions, title, author, bookmarks (REQ-1)
- Text with full positional and stylistic attributes: position, font name, size, bold/italic, color (REQ-2)
- Graphics paths: filled/stroked rectangles, lines, complex paths with color and stroke properties (REQ-3)
- Embedded images: bounding boxes, pixel dimensions, raw image data (REQ-4)

The library must work in pure .NET without external process dependencies, and must provide access to pre-processed graphics paths with color, stroke, and transformation data.

## Decision

Use **PdfPig** (`UglyToad.PdfPig` NuGet package) as the PDF parsing library.

## Alternatives Considered

### iTextSharp / iText 7 for .NET

- **Pros:** Very mature; comprehensive PDF support; extensive documentation.
- **Cons:** AGPL license (requires commercial license for non-open-source use); heavier dependency; more complex API for simple extraction tasks.

### PDFsharp

- **Pros:** MIT-licensed; mature for PDF creation.
- **Cons:** Primarily designed for PDF creation/modification, not content extraction; limited support for reading text with positional attributes; no graphics path extraction API.

### PdfiumViewer / PDFium .NET wrappers

- **Pros:** High rendering fidelity (Chromium's PDF engine); good for page rendering.
- **Cons:** Primarily a rendering engine, not a structural extraction library; does not expose letter-level font/color metadata; native dependencies.

## Consequences

- PdfPig provides `page.Letters` with position (x, y, width, height), font (name, size, bold, italic), and color — directly supporting REQ-2 at both letter and word granularity via `page.GetWords()`.
- PdfPig provides `page.GetImages()` with `IPdfImage` exposing bounds and `TryGetPng()` for image data extraction (REQ-4).
- PdfPig provides `page.Paths` (`IReadOnlyList<PdfPath>`) for graphics extraction (REQ-3). Each `PdfPath` includes fill/stroke colors (`IColor`), line width, dash pattern, and subpath commands (`Move`, `Line`, `CubicBezierCurve`, `Close`, etc.) with coordinates already transformed through the CTM. PdfPig's internal `ContentStreamProcessor` handles graphics state tracking and automatically recurses into Form XObjects — graphics wrapped inside Form XObjects are included in `page.Paths` without custom handling. However, PdfPig does **not** pre-classify shapes — implementing REQ-3 requires classifying each `PdfPath` as a rectangle, line, or complex path based on its subpath commands, and converting colors via `IColor.ToRGBValues()`.
- PdfPig is MIT-licensed with no commercial restrictions.
- PdfPig is a pure C# library with no native dependencies, simplifying deployment.
- PdfPig exposes `document.NumberOfPages`, page dimensions, and document metadata for REQ-1.
- PdfPig does **not** support PDF rendering (relevant to ADR-0004).
