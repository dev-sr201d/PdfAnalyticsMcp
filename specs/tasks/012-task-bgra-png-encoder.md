# Task 012: BGRA-to-PNG Encoder Utility

## Description

Create an internal utility that encodes raw BGRA pixel data (as produced by Docnet's `GetImage()`) into a valid PNG byte array. This utility uses only built-in .NET APIs (`System.IO.Compression`) — no external imaging libraries. It is a prerequisite for the page rendering service and is independently unit-testable.

## Traces To

- **FRD:** FRD-005 (Page Rendering — RenderPagePreview), Functional Requirement 6–7
- **PRD:** REQ-5 (Page rendering)
- **ADRs:** ADR-0004 (Docnet/PDF rendering)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)

No other task dependencies. This is a standalone utility with no external library requirements.

## Technical Requirements

### Utility Location and Signature

Define an internal static class in `Services/` (e.g., `PngEncoder`) with a method that:
- Accepts raw pixel data (`byte[]`), image width (`int`), and image height (`int`)
- The pixel data is in **BGRA format** (Blue, Green, Red, Alpha — 4 bytes per pixel), which is what Docnet's `IPageReader.GetImage()` produces (which returns `byte[]`)
- Returns a `byte[]` containing a valid PNG file
- Throws `ArgumentException` if the pixel data length does not match `width * height * 4`

### PNG Format Requirements

The encoder must produce a valid PNG file conforming to the PNG specification (ISO/IEC 15948). The minimum required structure:

1. **PNG Signature** — 8 bytes: `[137, 80, 78, 71, 13, 10, 26, 10]`
2. **IHDR chunk** — Image header containing:
   - Width (4 bytes, big-endian)
   - Height (4 bytes, big-endian)
   - Bit depth: `8`
   - Color type: `2` (RGB — alpha is composited against white, not preserved)
   - Compression method: `0` (deflate)
   - Filter method: `0`
   - Interlace method: `0` (no interlace)
3. **IDAT chunk(s)** — Compressed image data:
   - For each row: prepend a filter byte (`0` = None filter) followed by 3-byte RGB pixel data for that row
   - Convert each pixel from BGRA to RGB, compositing the alpha channel against a white background using the formula: `out = src * a/255 + 255 * (255 - a) / 255`. This matches standard PDF viewer behavior — PDF pages have no explicit background color, and viewers conventionally render on white. Docnet/PDFium renders onto a transparent BGRA buffer, so without compositing, pages with no drawn background rectangle would appear transparent.
   - Compress all filtered row data using `System.IO.Compression.ZLibStream` (available in .NET 6+). `ZLibStream` produces the complete zlib format (header + deflate + Adler-32 checksum) natively — no manual zlib header or Adler-32 implementation is needed.
4. **IEND chunk** — Empty end marker

Each chunk follows the PNG chunk structure: `[4-byte data length (big-endian)][4-byte chunk type (ASCII)][data bytes][4-byte CRC-32 of type + data]`

### CRC-32

- **CRC-32** is required for each PNG chunk. Use the standard CRC-32 polynomial (`0xEDB88320` reflected). This can be implemented as a small internal helper or lookup table.

### Performance Considerations

- The encoder processes raw pixel buffers that can be large (e.g., ~8.4 MB for a 1275×1650 BGRA image at 150 DPI). Avoid unnecessary copies of the pixel data.
- Use `MemoryStream` for building the output. Pre-allocate a reasonable capacity based on image dimensions (compressed output is typically much smaller than raw data).
- The filter byte `0` (None) on every row is acceptable for this use case. Optimized PNG filter selection (Sub, Up, Average, Paeth) is not required — the purpose is functional correctness, not minimal file size.

## Acceptance Criteria

- [ ] The encoder produces output that starts with the 8-byte PNG signature.
- [ ] The encoder produces output that contains a valid IHDR chunk with the correct width, height, bit depth (8), and color type (2 = RGB).
- [ ] The encoder correctly composites BGRA pixel data against a white background and outputs RGB pixel order.
- [ ] The encoder produces output that contains at least one IDAT chunk with zlib-compressed data.
- [ ] The encoder produces output that ends with an IEND chunk.
- [ ] The encoder throws `ArgumentException` if pixel data length does not match `width * height * 4`.
- [ ] The CRC-32 values in each chunk are correct (verifiable by any PNG decoder accepting the output).
- [ ] The output of the encoder is a valid PNG that can be decoded by standard PNG decoders (tested by parsing the PNG signature and IHDR chunk in unit tests).

## Testing Requirements

Unit tests must be placed alongside existing unit tests in the test project.

### Required Unit Test Scenarios

1. **PNG signature** — Encode a small pixel buffer (e.g., 2×2) and verify the output starts with the 8-byte PNG signature `[137, 80, 78, 71, 13, 10, 26, 10]`.
2. **IHDR dimensions** — Encode a known-size image (e.g., 4×3) and parse the IHDR chunk from the output. Verify the width and height fields match the input dimensions.
3. **IHDR color type** — Verify the IHDR chunk specifies bit depth 8 and color type 2 (RGB — alpha composited against white).
4. **BGRA to RGB compositing** — Encode a single pixel with known BGRA values (e.g., B=0, G=128, R=255, A=200). Decompress the IDAT data and verify the pixel in the output is alpha-composited against white in RGB order (R=255, G=155, B=55).
5. **IEND present** — Verify the output ends with an IEND chunk (4 bytes length=0, type="IEND", CRC).
6. **Invalid data length** — Call with a pixel buffer whose length is not `width * height * 4`. Verify `ArgumentException` is thrown.
7. **Single pixel** — Encode a 1×1 image and verify the output is a valid PNG.
8. **Larger image** — Encode a modestly sized image (e.g., 100×100 with a repeating pattern) and verify the output starts with the PNG signature and contains IHDR with correct dimensions.
