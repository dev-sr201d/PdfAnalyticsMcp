# Task 008: GetPageText Service and DTOs

## Description

Create the data transfer objects and extraction service for the `GetPageText` tool (FRD-003). This task provides the core logic that extracts text content from a single PDF page with full positional and stylistic metadata, supporting both word-level and letter-level granularity. The service derives font name, font size, and color from PdfPig's constituent `Letter` objects, and infers bold/italic flags heuristically from font name patterns.

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 004** — Shared serialization configuration: `SerializerConfig`, `FormatUtils` (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)

## Technical Requirements

### DTOs

Define response DTOs in the `Models/` directory as immutable `record` types:

1. **Text element DTO** — Represents a single word or letter with the following fields:
   - `text` (string) — The word text or single character value
   - `x` (double) — Left edge X coordinate in PDF points, rounded to 1 decimal place
   - `y` (double) — Bottom edge Y coordinate in PDF points, rounded to 1 decimal place
   - `w` (double) — Width of the bounding box, rounded to 1 decimal place
   - `h` (double) — Height of the bounding box, rounded to 1 decimal place
   - `font` (string) — Font name
   - `size` (double) — Font size in points, rounded to 1 decimal place
   - `color` (string?, nullable) — RGB fill color as `"#RRGGBB"`; omitted (null) when the color is black (`#000000`) or default
   - `bold` (bool?, nullable) — True when bold is detected; omitted (null) when false
   - `italic` (bool?, nullable) — True when italic is detected; omitted (null) when false

2. **Page text response DTO** — Envelope for the full tool response:
   - `page` (int) — The 1-based page number returned
   - `width` (double) — Page width in PDF points, rounded to 1 decimal place
   - `height` (double) — Page height in PDF points, rounded to 1 decimal place
   - `elements` (array of text element DTOs) — The extracted text elements

3. **Page text summary DTO** — Compact response returned when `outputFile` is used:
   - `page` (int) — The 1-based page number returned
   - `width` (double) — Page width in PDF points, rounded to 1 decimal place
   - `height` (double) — Page height in PDF points, rounded to 1 decimal place
   - `elementCount` (int) — Number of text elements extracted
   - `outputFile` (string) — Absolute path where the full data was written
   - `sizeBytes` (long) — Size of the written file in bytes

### Service Interface

Define a service interface in `Services/` with **two methods** (avoiding polymorphic return types and keeping each method's contract type-safe):

1. **`Extract`** — Accepts a file path (string), a 1-based page number (int), and a granularity value (string). Returns the page text response DTO (full data inline). This is the existing method signature — it remains unchanged for backward compatibility.
2. **`ExtractToFile`** — Accepts a file path (string), a 1-based page number (int), a granularity value (string), and an output file path (string). Returns the page text summary DTO. Internally calls `Extract` to get the full data, serializes it to the file, then builds and returns the summary.

Both methods use `IInputValidationService` for page number validation (file path validation is the tool layer's responsibility, matching the established pattern in `GetPdfInfoTool`).

### Service Implementation

The service must:

1. **Validate granularity** before any I/O — if the value is not `"words"` or `"letters"`, throw an `ArgumentException` with a clear message listing valid options. This fails fast and avoids opening the PDF unnecessarily.
2. **Open the PDF** using PdfPig's `PdfDocument.Open()` with a `using` declaration. If the file cannot be opened as a PDF, catch the exception and throw `ArgumentException` with a message indicating the file could not be opened as a PDF (matching the pattern in `PdfInfoService`).
3. **Validate the page number** against the document's page count via `IInputValidationService.ValidatePageNumber()`.
4. **Access only the requested page** using `document.GetPage(n)` — never iterate all pages.
5. **Extract text elements** based on the `granularity` parameter:
   - `"words"`: Use `page.GetWords()` to get word-level elements.
   - `"letters"`: Use `page.Letters` to get individual character elements.
6. **Derive font metadata for words** from constituent `Letter` objects. PdfPig's `Word` does not directly expose font name, font size, or color — these must be derived from the word's `Letters` collection (e.g., using the first letter's properties).
7. **Infer bold/italic flags** heuristically from the font name string. Look for common patterns such as "Bold", "Italic", "BoldItalic", "Oblique" as substrings (case-insensitive). This is best-effort — set to `true` only when detected, otherwise leave null.
8. **Format colors** using `FormatUtils.FormatColor()`. If the resulting color is `"#000000"` (black), set the field to null so it is omitted from JSON.
9. **Round all coordinates** (x, y, w, h) and font size using `FormatUtils.RoundCoordinate()`.
10. **Handle PdfPig color extraction**: Letter color is available via the `Color` property. PdfPig's `IColor` interface has multiple implementations (grayscale, CMYK, RGB, etc.). Use the color's `ToRGBValues()` method to convert to RGB. Handle the conversion safely — if the `Color` property is null, the color space is unsupported, or `ToRGBValues()` throws, treat as default black (null). Do not assume all letters will have a directly accessible RGB color.
11. **Dispose PdfDocument** properly — open per call, not cached.
12. **`ExtractToFile` implementation** — The `ExtractToFile` method must:
    - Validate the output file path is absolute (not relative). Reject relative paths with `ArgumentException`.
    - Reject paths containing path traversal sequences (`..`). Throw `ArgumentException`.
    - Verify the parent directory exists. Throw `ArgumentException` if it does not.
    - Call the existing `Extract` method internally to get the full page text response DTO.
    - Serialize the full DTO to compact JSON using `SerializerConfig.Options`.
    - Write the JSON string to the specified file path (overwrite if it already exists).
    - Return the page text summary DTO with the element count, the absolute file path, and the file size in bytes.
    - Round `width` and `height` in the summary DTO to 1 decimal place (consistent with the full response DTO).
13. **`Extract` method unchanged** — The existing `Extract` method continues to return the full page text response DTO. No changes needed for backward compatibility.

### Test Data

Create test PDF files programmatically using PdfPig's `PdfDocumentBuilder` API in a test setup helper or a standalone generator script. This ensures test PDFs have deterministic, known content that tests can assert against. Do not rely on externally-created binary PDF files for text extraction tests.

The test data PDF(s) must include:
- Known text content with identifiable words
- At least two different fonts (e.g., one regular, one bold)
- At least one non-black colored text element
- A page with approximately 300 words to support the ≤ 30 KB response size acceptance criterion (this PDF is also needed by Task 009's integration tests)
- A dense page with enough words to produce > 30 KB of JSON at word granularity (needed by Task 009 test #16 for verifying `outputFile` on large pages)
- Multiple words on a single page for word-level extraction testing
- Content suitable for verifying letter-level extraction

Place generated PDFs in `tests/TestData/`. If using a programmatic generator, include it as a test utility in the test project so the files can be regenerated if needed.

### Registration

Register the service in `Program.cs` dependency injection as a singleton, following the existing pattern used by `IPdfInfoService`.

## Acceptance Criteria

- [ ] Text element DTO is defined as an immutable record with all specified fields, using nullable types for optional fields (color, bold, italic).
- [ ] Page text response DTO is defined as an immutable record with page number, dimensions, and elements array.
- [ ] Service interface defines two methods: `Extract` (returns full DTO) and `ExtractToFile` (returns summary DTO), each with explicit return types.
- [ ] Service implementation extracts word-level text elements using `page.GetWords()` when granularity is `"words"`.
- [ ] Service implementation extracts letter-level text elements using `page.Letters` when granularity is `"letters"`.
- [ ] Service throws `ArgumentException` for invalid granularity values.
- [ ] Font name and font size are correctly derived from constituent letters for word-level extraction.
- [ ] Bold and italic flags are inferred from font name patterns and set to `true` only when detected (null otherwise).
- [ ] Color is formatted as `"#RRGGBB"` and set to null for black/default text.
- [ ] All coordinates and font size are rounded to 1 decimal place.
- [ ] PdfDocument is opened with `using` and disposed after each call.
- [ ] Only the requested page is accessed via `document.GetPage(n)`.
- [ ] Service is registered as a singleton in `Program.cs`.
- [ ] Test data PDF(s) exist in `tests/TestData/` with known text, fonts, and colors.
- [ ] Page text summary DTO is defined as an immutable record with page, width, height, elementCount, outputFile, and sizeBytes fields.
- [ ] `ExtractToFile` writes the full JSON to disk, then returns a summary DTO with correct fields.
- [ ] `Extract` continues to return the full page text response DTO unchanged (backward-compatible).
- [ ] `ExtractToFile` internally delegates to `Extract` to obtain the full data before writing.
- [ ] The `outputFile` path is validated: relative paths and path traversal sequences are rejected with `ArgumentException`.
- [ ] If the parent directory of `outputFile` does not exist, `ArgumentException` is thrown.

## Testing Requirements

Unit tests must cover:

1. **Word extraction** — Given a PDF with known words, verify the service returns the correct number of word elements with matching text values.
2. **Letter extraction** — Given the same PDF, verify letter-level extraction returns individual characters with correct text values.
3. **Font metadata derivation** — Verify that font name and font size on word elements are correctly derived from constituent letters.
4. **Bold/italic inference** — Verify that a font name containing "Bold" results in `bold = true`, "Italic" results in `italic = true`, and a regular font name results in both being null.
5. **Color formatting** — Verify that non-black text produces a `"#RRGGBB"` color value and black text produces null.
6. **Coordinate rounding** — Verify all positional values and font size are rounded to 1 decimal place.
7. **Page dimensions** — Verify the response includes correct page width and height.
8. **Invalid granularity** — Verify that an unsupported granularity value throws `ArgumentException`.
9. **Page number validation** — Verify that out-of-range page numbers throw `ArgumentException` (via `IInputValidationService`).
10. **Invalid PDF** — Verify that a non-PDF file throws `ArgumentException`.
11. **Serialization** — Verify that the DTOs serialize to expected JSON structure: camelCase properties, null fields omitted, compact format.
12. **ExtractToFile writes to disk** — Given a valid output file path, verify `ExtractToFile` writes JSON to disk and returns a summary DTO with correct `elementCount`, `outputFile`, and `sizeBytes` values.
13. **ExtractToFile file content** — Verify the written file contains valid JSON matching the full page text response structure.
14. **ExtractToFile overwrite** — Verify that if the output file already exists, it is overwritten without error.
15. **ExtractToFile relative path rejected** — Verify that a relative output file path throws `ArgumentException`.
16. **ExtractToFile path traversal rejected** — Verify that an output file path containing `..` throws `ArgumentException`.
17. **ExtractToFile directory missing** — Verify that an output file path in a nonexistent directory throws `ArgumentException`.
18. **Extract unchanged** — Verify that `Extract` continues to return the full page text response DTO with no file I/O side effects.
