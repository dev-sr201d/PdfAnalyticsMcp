# ADR-0004: PDF Page Rendering and Image Encoding

## Status

Accepted

## Context

REQ-5 requires the ability to render a PDF page as an image at a configurable DPI so that multimodal AI models can visually inspect page layout. The tool must support both PNG (lossless) and JPEG (lossy) output formats with a quality parameter, because pages with large multi-colored images can produce PNG files of 2–3 MB — too large for some AI agent endpoints. JPEG at moderate quality typically reduces these to 200–500 KB. PdfPig (ADR-0002) is a parser/extractor and cannot render pages.

This decision covers two concerns:
1. **PDF rendering** — converting a PDF page into raw pixel data.
2. **Image encoding** — encoding the raw pixels into PNG or JPEG for delivery to the agent.

## Decision

1. Use **Docnet** (`Docnet.Core` NuGet package) for PDF page rendering.
2. Use **SkiaSharp** (`SkiaSharp` NuGet package) for JPEG image encoding. Use a lightweight manual PNG writer built on `System.IO.Compression.ZLibStream` for PNG encoding.

## Alternatives Considered

### PDF Rendering

#### External CLI: MuPDF (`mutool draw`) or Poppler (`pdftoppm`)

- **Pros:** High rendering quality; widely available on Linux; no managed native dependency.
- **Cons:** Requires external tool installation — not self-contained; process spawning adds latency and error handling complexity; different tools available on different platforms; harder to distribute as a single package; potential security concerns with arbitrary process execution.

#### SkiaSharp as a custom PDF renderer

- **Pros:** Full control over rendering pipeline; SkiaSharp is widely used in .NET.
- **Cons:** Would require implementing a complete PDF rendering engine (text layout, font substitution, graphics, image compositing) — this is months of work and not practical for this project.

> **Note:** SkiaSharp was rejected as a *PDF renderer* but is used for *image encoding* (see below). These are fundamentally different tasks — rendering a PDF page requires interpreting the PDF specification, while encoding pixels to JPEG is a straightforward image compression operation.

#### No rendering (skip REQ-5)

- **Pros:** Eliminates native dependency entirely.
- **Cons:** Multimodal models lose the ability to visually verify layout understanding; significantly reduces agent effectiveness on complex layouts. The concept document identifies this as "the most valuable tool for complex layouts."

### Image Encoding (JPEG)

.NET 9 has no built-in JPEG encoder suitable for server workloads (`System.Drawing` is Windows-only and deprecated). Since this feature already depends on Docnet's native PDFium binaries, adding another native dependency does not change the deployment model.

#### SkiaSharp (chosen)

- **Pros:** Wraps Google's Skia with libjpeg-turbo for high-quality JPEG compression; direct 1–100 quality mapping; cross-platform native binaries bundled via NuGet (same distribution model as Docnet); battle-tested in .NET production workloads; minimal encoding code.
- **Cons:** Adds native Skia binaries to deployment size.

#### ImageSharp (SixLabors)

- **Pros:** Pure managed (no native dependencies); full-featured image processing.
- **Cons:** Requires a commercial license for paid products since v3; heavier dependency than needed for simple encoding.

#### StbImageWriteSharp

- **Pros:** Minimal, managed, no native dependencies.
- **Cons:** Less maintained; limited encoding options and quality control.

## Consequences

- Docnet targets .NET Standard 2.0, compatible with .NET 9 (ADR-0001).
- Docnet bundles platform-specific PDFium native binaries via NuGet for Windows (x64, x86), Linux (x64, ARM, ARM64), and macOS (x64, ARM64) — cross-platform support is built in.
- SkiaSharp bundles platform-specific Skia native binaries via NuGet with the same cross-platform coverage. Both libraries follow the same NuGet-based native dependency distribution model.
- The combined native binaries (PDFium + Skia) add to deployment size but eliminate the need for users to install external tools.
- Rendering output from Docnet is raw BGRA pixel data. PNG encoding uses a manual writer built on `ZLibStream` (built into .NET 6+), requiring no external dependency. JPEG encoding uses SkiaSharp's `SKImage.Encode()` with the caller's quality parameter. Both formats composite the transparent BGRA buffer onto a white background before encoding, matching standard PDF viewer behavior.
- Docnet operates independently from PdfPig — each library opens the PDF file separately. This is acceptable since tool calls are page-by-page (REQ-7) and short-lived.
- The rendering tool (`RenderPagePreview`) is optional from the agent's perspective; text/graphics/image extraction remains fully functional without it.
