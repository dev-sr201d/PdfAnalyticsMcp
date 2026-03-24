# ADR-0004: PDF Page Rendering Library

## Status

Accepted

## Context

REQ-5 requires the ability to render a PDF page as a PNG image at a configurable DPI so that multimodal AI models can visually inspect page layout. PdfPig (ADR-0002) is a parser/extractor and cannot render pages. A separate rendering solution is needed.

The concept document identifies three options:
1. **Docnet** — a .NET wrapper around PDFium (Chromium's PDF engine)
2. **External CLI tools** — spawning `mutool draw` (MuPDF) or `pdftoppm` (Poppler)
3. **SkiaSharp** — building a custom renderer (deemed impractical)

## Decision

Use **Docnet** (`Docnet.Core` NuGet package) for PDF page rendering.

## Alternatives Considered

### External CLI: MuPDF (`mutool draw`) or Poppler (`pdftoppm`)

- **Pros:** High rendering quality; widely available on Linux; no managed native dependency.
- **Cons:** Requires external tool installation — not self-contained; process spawning adds latency and error handling complexity; different tools available on different platforms; harder to distribute as a single package; potential security concerns with arbitrary process execution.

### SkiaSharp custom renderer

- **Pros:** Full control over rendering pipeline; SkiaSharp is widely used in .NET.
- **Cons:** Would require implementing a complete PDF rendering engine (text layout, font substitution, graphics, image compositing) — this is months of work and not practical for this project.

### No rendering (skip REQ-5)

- **Pros:** Eliminates native dependency entirely.
- **Cons:** Multimodal models lose the ability to visually verify layout understanding; significantly reduces agent effectiveness on complex layouts. The concept document identifies this as "the most valuable tool for complex layouts."

## Consequences

- Docnet targets .NET Standard 2.0, compatible with .NET 9 (ADR-0001).
- Docnet bundles platform-specific PDFium native binaries via NuGet for Windows (x64, x86), Linux (x64, ARM, ARM64), and macOS (x64, ARM64) — cross-platform support is built in.
- The native binaries add to the deployment size but eliminate the need for users to install external tools.
- Rendering output is raw BGRA pixel data; the server will need to encode it to PNG (using `System.Drawing`, `ImageSharp`, or `SkiaSharp` for encoding only — a lightweight operation).
- Docnet operates independently from PdfPig — each library opens the PDF file separately. This is acceptable since tool calls are page-by-page (REQ-7) and short-lived.
- The rendering tool (`RenderPagePreview`) is optional from the agent's perspective; text/graphics/image extraction remains fully functional without it.
