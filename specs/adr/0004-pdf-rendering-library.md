# ADR-0004: PDF Page Rendering, Image Extraction, and Image Encoding

## Status

Accepted

## Context

Two requirements drive the need for a PDFium-based native library:

1. **REQ-5 (Page rendering)** — Render a PDF page as an image at a configurable DPI so that multimodal AI models can visually inspect page layout. The tool must support both PNG (lossless) and JPEG (lossy) output formats with a quality parameter, because pages with large multi-colored images can produce PNG files of 2–3 MB — too large for some AI agent endpoints. JPEG at moderate quality typically reduces these to 200–500 KB. PdfPig (ADR-0002) is a parser/extractor and cannot render pages.

2. **REQ-4 (Image extraction)** — Extract embedded images from PDF pages with bounding boxes, pixel dimensions, and optional file-based PNG extraction to disk. PdfPig's `IPdfImage.TryGetPng()` fails for many image formats (JPEG2000, JBIG2, CMYK, images with soft masks), so a more capable extraction mechanism is needed.

PDFium's `fpdf_edit.h` API exposes per-object access to page content, including `FPDFImageObj_GetRenderedBitmap()` which renders a single image object with its mask and matrix applied, producing clean per-image bitmaps regardless of the source encoding. Additionally, `FPDFImageObj_GetImageFilterCount()` / `FPDFImageObj_GetImageFilter()` expose the compression filter names, and `FPDFImageObj_GetImageDataRaw()` provides direct access to the raw compressed stream — enabling lossless extraction of JPEG images without re-encoding.

This decision covers three concerns:
1. **PDF rendering** — converting a PDF page into raw pixel data (REQ-5).
2. **Image extraction** — extracting individual embedded images as bitmaps (REQ-4).
3. **Image encoding** — encoding the raw pixels into PNG or JPEG for delivery to the agent.

## Decision

1. Use **PDFiumCore** (`PDFiumCore` NuGet package from `dtronix/pdfiumcore`) for PDF page rendering and embedded image extraction.
2. Use **SkiaSharp** (`SkiaSharp` NuGet package) for JPEG image encoding. Use a lightweight manual PNG writer built on `System.IO.Compression.ZLibStream` for PNG encoding.

## Alternatives Considered

### PDF Rendering

#### Docnet (`Docnet.Core`)

- **Pros:** Simple high-level API (`GetDocReader` / `GetPageReader` / `GetImage()`); .NET Standard 2.0; bundles PDFium binaries via NuGet.
- **Cons:** Wraps only a subset of PDFium's API — no access to page objects, image objects, or `fpdf_edit.h` functions; cannot extract individual images from pages; unmaintained (last commit 3+ years ago).

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

### Image Extraction

#### PdfPig-only (current baseline)

- **Pros:** Pure managed; no additional dependencies; `TryGetPng()` works for common image encodings.
- **Cons:** `TryGetPng()` fails for JPEG2000, JBIG2, CMYK images, and images with soft masks. Failed images require a fallback mechanism.

#### PdfPig + render-and-crop fallback

- **Pros:** Recovers images that `TryGetPng()` cannot handle using a full-page render and crop.
- **Cons:** Renders the entire page at high DPI and crops the region — slow, memory-intensive, and includes surrounding text/graphics in the crop when image bounding boxes overlap with other content. Requires computing DPI sufficient for the highest-resolution image on the page.

#### PDFiumCore `FPDFImageObj_GetRenderedBitmap()` (chosen)

- **Pros:** Renders each image object individually with its mask and transformation matrix applied; produces a clean bitmap of just the image regardless of source encoding; no text or graphics contamination; works for all image formats PDFium supports (which is virtually all formats found in real-world PDFs); the bitmap is returned at the image's native resolution, not at an arbitrary DPI. Additionally, `FPDFImageObj_GetImageDataRaw()` enables direct extraction of raw JPEG bytes for DCTDecode images, avoiding lossy re-encoding and producing significantly smaller output files. `FPDFImageObj_GetBitmap()` provides a fallback when `GetRenderedBitmap` returns null (e.g., for images with complex clipping contexts), returning the raw image at native resolution without mask/matrix processing.
- **Cons:** Lower-level API requiring manual handle management and `Marshal.Copy` for pixel data; fallback bitmap may be in BGR or grayscale format requiring normalization to BGRA.

.NET 9 has no built-in JPEG encoder suitable for server workloads (`System.Drawing` is Windows-only and deprecated). Since this feature already depends on PDFiumCore's native PDFium binaries, adding another native dependency does not change the deployment model.

#### SkiaSharp (chosen)

- **Pros:** Wraps Google's Skia with libjpeg-turbo for high-quality JPEG compression; direct 1–100 quality mapping; cross-platform native binaries bundled via NuGet (same distribution model as PDFiumCore); battle-tested in .NET production workloads; minimal encoding code.
- **Cons:** Adds native Skia binaries to deployment size.

#### ImageSharp (SixLabors)

- **Pros:** Pure managed (no native dependencies); full-featured image processing.
- **Cons:** Requires a commercial license for paid products since v3; heavier dependency than needed for simple encoding.

#### StbImageWriteSharp

- **Pros:** Minimal, managed, no native dependencies.
- **Cons:** Less maintained; limited encoding options and quality control.

## Consequences

### Rendering (REQ-5)

- PDFiumCore targets .NET Standard 2.1, compatible with .NET 9 (ADR-0001).
- PDFiumCore bundles platform-specific PDFium native binaries via NuGet for Windows (x64, x86), Linux (x64), and macOS (x64) — cross-platform support is built in.
- `FPDF_InitLibrary()` must be called once at application startup (e.g., in a hosted service or static initializer). `FPDF_DestroyLibrary()` should be called at shutdown.
- Rendering uses `FPDF_RenderPageBitmapWithMatrix()` with a scaling matrix where `scalingFactor = dpi / 72.0`, producing raw BGRA pixel data at the desired DPI. Default to 150 DPI.
- Page numbers in PDFium are 0-based — subtract 1 from the user-facing 1-based page number.
- PNG encoding uses a manual writer built on `ZLibStream` (built into .NET 6+), requiring no external dependency. JPEG encoding uses SkiaSharp's `SKImage.Encode()` with the caller's quality parameter. Both formats composite the transparent BGRA buffer onto a white background before encoding, matching standard PDF viewer behavior.
- SkiaSharp bundles platform-specific Skia native binaries via NuGet with the same cross-platform coverage. Both libraries follow the same NuGet-based native dependency distribution model.

### Image Extraction (REQ-4)

- Image discovery uses `FPDFPage_CountObjects()` / `FPDFPage_GetObject()` / `FPDFPageObj_GetType()` to enumerate page objects and filter for `FPDF_PAGEOBJ_IMAGE` (value 3).
- Bounding boxes are obtained via `FPDFPageObj_GetBounds()`, returning coordinates in PDF page space (points).
- Image metadata (pixel width, height, DPI, bits per pixel, colorspace) is obtained via `FPDFImageObj_GetImageMetadata()`.
- When file extraction is requested, the extraction strategy is optimized per encoding:
  - **JPEG images** (single `DCTDecode` filter): Raw JPEG bytes are extracted directly via `FPDFImageObj_GetImageDataRaw()` and written to disk as `.jpg` files. This avoids bitmap rendering and re-encoding entirely, preserving the original image data with zero quality loss.
  - **All other images**: `FPDFImageObj_GetRenderedBitmap(document, page, imageObject)` produces a rendered BGRA bitmap of each image individually — with mask and matrix applied. The bitmap is encoded to PNG and written to disk as `.png` files. If `GetRenderedBitmap` returns null (which can occur for images with complex clipping contexts), the service falls back to `FPDFImageObj_GetBitmap()`, which returns the raw image at native resolution in BGR or grayscale format. The fallback data is normalized to BGRA before PNG encoding.
- Every image can be extracted cleanly regardless of source encoding, with no text or graphics contamination.
- PdfPig remains the primary library for text extraction (REQ-2) and graphics extraction (REQ-3). Image extraction (REQ-4) is handled entirely by PDFiumCore.

### Thread Safety

- PDFium's native library is **not thread-safe**. Use a `static SemaphoreSlim(1, 1)` to serialize all calls through PDFiumCore. Accept `CancellationToken` in service methods so that callers queued behind the semaphore can be cancelled by the MCP client.

### Shared PDFiumCore Service

- Both page rendering (REQ-5) and image extraction (REQ-4) depend on PDFiumCore and share the same constraints: library lifecycle, thread serialization, document/page loading, error handling, and native resource cleanup. These cross-cutting concerns should be encapsulated in a single shared service rather than duplicated across feature-specific services.
- The shared service owns the `FPDF_InitLibrary()` / `FPDF_DestroyLibrary()` lifecycle (called once at startup/shutdown), the serialization semaphore, and provides methods for loading documents and pages with consistent error handling (file access errors, invalid PDFs, page number validation) aligned with the error message patterns defined in FRD-007.
- The rendering service and image extraction service consume this shared service for all PDFiumCore interactions, keeping their implementations focused on domain logic (scaling/encoding for rendering, object enumeration/bitmap extraction for images).

### Resource Management

- All PDFium handles (`FpdfDocumentT`, `FpdfPageT`, `FpdfBitmapT`) must be explicitly released via their corresponding close/destroy functions. Use `try/finally` blocks to ensure cleanup.
- PDFiumCore operates independently from PdfPig — each library opens the PDF file separately. This is acceptable since tool calls are page-by-page (REQ-6) and short-lived.
- The rendering tool (`RenderPagePreview`) is optional from the agent's perspective; text/graphics extraction remains fully functional without it.
