# Task 012a: JPEG Encoder Utility (SkiaSharp)

## Description

Create an internal utility that encodes raw BGRA pixel data (as produced by Docnet's `GetImage()`) into a JPEG byte array using SkiaSharp. This utility complements the PNG encoder from Task 012, providing lossy compression that significantly reduces file size for pages with photographic or multi-colored content. It is a prerequisite for the page rendering service (Task 013) and is independently unit-testable.

## Traces To

- **FRD:** FRD-005 (Page Rendering — RenderPagePreview), Functional Requirements 13, 16–17
- **PRD:** REQ-5 (Page rendering)
- **ADRs:** ADR-0004 (PDF Page Rendering and Image Encoding)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)

No other task dependencies. This is a standalone utility. The SkiaSharp NuGet package must be added to the main project.

## Technical Requirements

### NuGet Dependency

Add `SkiaSharp` to the main project's `.csproj`:
```xml
<PackageReference Include="SkiaSharp" Version="3.*" />
```

This introduces native Skia binaries (bundled per platform in the NuGet package). No manual configuration is required. The distribution model is the same as Docnet (ADR-0004).

### Utility Location and Signature

Define an internal static class in `Services/` (e.g., `JpegEncoder`) with a method that:
- Accepts raw pixel data (`byte[]`), image width (`int`), image height (`int`), and quality (`int`, range 1–100)
- The pixel data is in **BGRA format** (Blue, Green, Red, Alpha — 4 bytes per pixel), which is what Docnet's `IPageReader.GetImage()` produces
- Returns a `byte[]` containing a valid JPEG file
- Throws `ArgumentException` if the pixel data length does not match `width * height * 4`
- Throws `ArgumentException` if quality is outside the 1–100 range

### JPEG Encoding Requirements

1. **Alpha compositing** — Before encoding, composite the BGRA alpha channel against a white background, matching the same behavior as the PNG encoder (Task 012). PDF pages rendered by Docnet have a transparent buffer; without compositing, pages with no drawn background rectangle would appear as black in JPEG (JPEG has no alpha channel, so unhandled transparency defaults to black).
2. **BGRA to pixel conversion** — Load the composited pixel data into an `SKBitmap` configured for the BGRA color space (`SKColorType.Bgra8888`, `SKAlphaType.Opaque`). After compositing against white, all alpha values are 255 (fully opaque), so `Opaque` is the correct alpha type — it avoids unnecessary premultiplication math and clearly communicates that no transparency remains.
3. **Quality mapping** — Pass the quality parameter directly to SkiaSharp's JPEG encoder. SkiaSharp's quality parameter maps 1–100 to libjpeg-turbo's compression level (1 = maximum compression / lowest quality, 100 = minimum compression / highest quality). This matches the FRD-005 specification.
4. **Encoding** — Use `SKImage.FromBitmap()` then `image.Encode(SKEncodedImageFormat.Jpeg, quality)` to produce the JPEG bytes. Dispose all SkiaSharp objects (`SKBitmap`, `SKImage`, `SKData`) after use.
5. **Return** — Return the JPEG bytes from `SKData.ToArray()`.

### Performance Considerations

- The encoder processes raw pixel buffers that can be large (e.g., ~8.4 MB for a 1275×1650 BGRA image at 150 DPI). The alpha compositing pass modifies pixel data in place or into a pre-allocated buffer — avoid unnecessary copies.
- SkiaSharp's `SKBitmap` can be constructed with `InstallPixels` to wrap an existing byte array, avoiding a copy when the pixel data is already in the correct format. However, since alpha compositing modifies the data, a working copy may be needed to avoid mutating the caller's buffer.
- Dispose all SkiaSharp native objects promptly to release unmanaged memory.

## Acceptance Criteria

- [ ] `SkiaSharp` NuGet package is added to the main project.
- [ ] The encoder produces output that is a valid JPEG file (starts with the JPEG SOI marker `[0xFF, 0xD8]`).
- [ ] The encoder correctly composites BGRA pixel data against a white background before encoding (transparent pixels appear white, not black).
- [ ] The encoder respects the quality parameter: quality=100 produces a larger file than quality=10 for the same input.
- [ ] The encoder throws `ArgumentException` if pixel data length does not match `width * height * 4`.
- [ ] The encoder throws `ArgumentException` if quality is below 1 or above 100.
- [ ] The output of the encoder is a valid JPEG that can be decoded by standard JPEG decoders.
- [ ] All SkiaSharp native objects are disposed after encoding.

## Testing Requirements

Unit tests must be placed alongside existing unit tests in the test project.

### Required Unit Test Scenarios

1. **JPEG signature** — Encode a small pixel buffer (e.g., 2×2) and verify the output starts with the JPEG SOI marker `[0xFF, 0xD8]` and ends with the EOI marker `[0xFF, 0xD9]`.
2. **Quality affects file size** — Encode a moderately sized image (e.g., 100×100 with varied colors) at quality=10 and quality=100. Verify the quality=100 output is larger than quality=10.
3. **Alpha compositing against white** — Encode a single pixel with known BGRA values including partial transparency (e.g., B=0, G=0, R=0, A=0 — fully transparent). Decode or inspect the output to verify the pixel appears white (not black). A practical approach: encode a 1×1 fully transparent pixel at quality=100 and verify the output is close to a 1×1 white pixel encoding.
4. **Opaque pixel passthrough** — Encode a pixel with A=255 (fully opaque) and known RGB values. Verify the JPEG output represents approximately the same color (within JPEG lossy tolerance).
5. **Invalid data length** — Call with a pixel buffer whose length is not `width * height * 4`. Verify `ArgumentException` is thrown.
6. **Quality below range** — Call with quality=0. Verify `ArgumentException` is thrown.
7. **Quality above range** — Call with quality=101. Verify `ArgumentException` is thrown.
8. **Quality at boundaries** — Call with quality=1 and quality=100. Verify both succeed without exceptions.
9. **Larger image** — Encode a modestly sized image (e.g., 100×100 with a repeating color pattern) and verify the output starts with the JPEG SOI marker and has non-trivial length.
