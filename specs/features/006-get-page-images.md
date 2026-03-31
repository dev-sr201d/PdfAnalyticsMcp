# FRD-006: Page Image Extraction (GetPageImages)

## Traces To

- **PRD:** REQ-4 (Image extraction), REQ-6 (Data volume management), REQ-7 (Page-by-page processing), REQ-8 (Robust error handling), REQ-10 (Concurrent tool safety)
- **ADRs:** ADR-0002 (PdfPig), ADR-0004 (Docnet/PDF rendering), ADR-0005 (Serialization)

## Summary

Provide a tool that returns embedded images on a single PDF page with their bounding boxes and metadata. The agent uses image positions to understand text flow around images. When an output directory is provided, the tool extracts each image as a PNG file to disk, enabling the agent to reference or embed them when converting to other formats.

For images that PdfPig cannot directly convert to PNG, the tool uses the raw BGRA rendering capability from Feature 005 (Page Rendering) to render the page and crop individual images from the rendered output.

## Inputs

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `pdfPath` | string | Yes | — | Absolute path to the PDF file |
| `page` | int | Yes | — | 1-based page number |
| `outputPath` | string | No | — | Absolute path to a directory where extracted images will be written as PNG files. When omitted, only image metadata is returned (no image data is extracted). |

## Outputs

### Default (no `outputPath`)

A JSON object containing image metadata only:

| Field | Type | Description |
|-------|------|-------------|
| `page` | int | The page number returned |
| `width` | double | Page width in PDF points |
| `height` | double | Page height in PDF points |
| `images` | array | Array of image elements found on the page |

### When `outputPath` is provided

The same JSON structure, but each image element includes a `file` field with the path to the extracted PNG file on disk. The tool creates the PNG files in the specified directory using a deterministic naming convention.

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
| `bitsPerComponent` | int | Bits per color component (e.g., 8 for typical images) |
| `file` | string? | Absolute path to the extracted PNG file (only when `outputPath` is provided and extraction succeeded) |

## Functional Requirements

### Core Metadata Extraction

1. The tool must operate on a single page per call (REQ-7).
2. The tool must use `page.GetImages()` from PdfPig to enumerate embedded images.
3. Each image's bounding box must be extracted from `image.Bounds` (a `PdfRectangle`).
4. Pixel dimensions must be extracted from the image's intrinsic resolution properties.
5. When `outputPath` is not provided, the `file` field must be omitted from the response. This keeps responses small for the common case where the agent only needs to know image positions (REQ-6).
6. Coordinates must be rounded to 1 decimal place.
7. If a page has no images, the `images` array must be empty (not null).

### File Naming Convention

8. When `outputPath` is provided, extracted images must be written as PNG files to that directory using the naming pattern: `{pdfStem}_p{page}_img{index}.png` — where `{pdfStem}` is the PDF filename without its extension, `{page}` is the 1-based page number, and `{index}` is the 1-based image index (in the order returned by PdfPig). Example: for `report.pdf`, page 3, second image → `report_p3_img2.png`.
9. The `{pdfStem}` component must be sanitized to remove or replace characters that are invalid in file names on the host OS. If the sanitized stem is empty (e.g., the PDF filename consists entirely of special characters), a fallback stem such as `"pdf"` must be used.
10. The `file` field in the response must contain the absolute path to the written PNG file.

### Image Data Extraction (Direct)

11. When `outputPath` is provided, the tool must first attempt PNG conversion via `image.TryGetPng()` (direct extraction from the PDF image stream) and write the result to disk.

### Render-Based Fallback for Image Data Extraction

PdfPig's `TryGetPng()` only succeeds for a subset of PDF image encodings. Many real-world PDFs use image formats (JBIG2, CCITT fax, certain colorspace/filter combinations) that PdfPig cannot convert to PNG. To provide reliable image data extraction, the tool uses the raw BGRA rendering capability from Feature 005 to render the page and crop individual images from the rendered output.

12. When `outputPath` is provided and `TryGetPng()` fails for one or more images, the tool must render the page using the raw BGRA rendering method from the rendering service (FRD-005, requirement 14) and crop each failed image from the rendered page using its bounding box.
13. If the page contains multiple images that require fallback extraction, the page must be rendered only once and all fallback crops taken from that single render.
14. The rendered crop must use the image's bounding box (in PDF points) mapped to pixel coordinates in the rendered image. The PDF coordinate system (origin at bottom-left, Y increasing upward) must be correctly mapped to the pixel coordinate system (origin at top-left, Y increasing downward).
15. The render DPI for fallback extraction should be chosen to approximate the native resolution of the images being cropped, capped at a maximum of 600 DPI and floored at 72 DPI. When multiple images need fallback extraction, the DPI should be chosen to best serve the highest-resolution image. If native resolution cannot be determined, the default rendering DPI of 150 must be used.
16. The crop region must be clamped to the page render boundaries to handle images whose bounding boxes extend slightly beyond the page edges.
17. The cropped BGRA pixel data must be composited against a white background (matching the page render behavior) and encoded as PNG.
18. If the fallback rendering itself fails (e.g., Docnet cannot open the file or render the page), the image's `file` field must be null. The fallback must not cause the entire tool call to fail — other images and all metadata must still be returned.
19. The fallback rendering is automatically serialized through the rendering service's semaphore (FRD-005, requirement 10) — no additional concurrency handling is needed in this feature.
20. The `file` field must not indicate to the agent whether the PNG was obtained via direct extraction or render-based fallback. The result is a PNG file in either case.

### Output Path Validation

21. The `outputPath` must be validated: it must be an absolute path, must not contain path traversal sequences (`..`), and the directory must exist.
22. If a file with the same name already exists in the output directory, it must be overwritten.

### Error Handling

23. If metadata extraction fails for an individual image (e.g., PdfPig throws while reading the image's bounding box or pixel dimensions), that image must be skipped entirely. Remaining images on the page must still be returned.
24. If writing a PNG file to disk fails (e.g., permissions, disk full), the image's `file` field must be null. The error must not cause the entire tool call to fail — other images and all metadata must still be returned.
25. Standard file path and page number validation rules apply as defined in FRD-007.

## Response Size Considerations

The inline JSON response always contains only image metadata (bounding boxes, pixel dimensions, and optionally file paths) — never image data. This means the response is well under 30 KB for any typical page, regardless of whether `outputPath` is provided.

Image data is written to disk as separate PNG files, keeping the MCP response payload small and avoiding the 33% base64 encoding overhead that inline image data would incur.

The render-based fallback may produce slightly larger PNG files than direct extraction for the same image, because the rendered crop includes sub-pixel antialiasing artifacts from the page render. This is an acceptable trade-off for being able to extract images that would otherwise be unavailable.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- Feature 005 (Page Rendering) must be complete — the rendering service's raw BGRA buffer API is used for the render-based fallback.
- `UglyToad.PdfPig` NuGet package.
- `Docnet.Core` NuGet package (via Feature 005's rendering service).

> **Note:** This feature reuses the shared infrastructure established by Feature 002: the centralized serialization options, coordinate rounding utility, and input validation service. The render-based fallback delegates to the rendering service from Feature 005, which encapsulates the Docnet lifecycle, rendering semaphore, and native resource management.

## Acceptance Criteria

### Core Metadata Extraction
- [ ] Calling `GetPageImages` on a page with images returns bounding boxes and pixel dimensions for each image.
- [ ] Calling `GetPageImages` without `outputPath` does not include any `file` fields in the response and does not write any files.
- [ ] A page with no images returns an empty `images` array.
- [ ] Coordinates are rounded to 1 decimal place.
- [ ] The response is well under 30 KB for any typical page (image data is never inline).

### File-Based Image Extraction
- [ ] When `outputPath` is provided, images are written as PNG files to the specified directory.
- [ ] File names follow the pattern `{pdfStem}_p{page}_img{index}.png`.
- [ ] The `file` field in each image element contains the absolute path to the written PNG.
- [ ] Images where direct PNG conversion succeeds are written from `TryGetPng()` output.
- [ ] Existing files with the same name are overwritten without error.

### Render-Based Fallback
- [ ] When `outputPath` is provided and `TryGetPng()` fails for an image, the tool falls back to render-and-crop and writes a valid PNG file for that image.
- [ ] When multiple images on a page require fallback, the page is rendered only once (not once per image).
- [ ] The cropped image region correctly maps the PDF bounding box to pixel coordinates, accounting for the Y-axis inversion between PDF and pixel coordinate systems.
- [ ] Crop regions that extend beyond the rendered page boundaries are clamped rather than causing an error.
- [ ] If fallback rendering itself fails, the affected image's `file` field is null but the tool call still succeeds with all metadata intact.
- [ ] Fallback rendering is serialized through the rendering service's semaphore.
- [ ] The fallback-produced PNG is a valid image that can be decoded by standard image viewers.

### Output Path Validation
- [ ] The `outputPath` parameter rejects relative paths and path traversal sequences.
- [ ] A non-existent output directory produces a clear error message.

### Error Handling
- [ ] When metadata extraction fails for an individual image, that image is skipped and remaining images are still returned.
- [ ] When writing a PNG file to disk fails, the image's `file` field is null but the tool call succeeds with all metadata.
- [ ] When `outputPath` is provided but the page has no images, the tool succeeds with an empty `images` array and no files are written.
- [ ] File name sanitization handles PDF filenames with special characters (spaces, unicode, filesystem-illegal characters) without errors.
