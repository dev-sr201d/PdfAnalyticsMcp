# Task 020: Move Fail-Fast Parameter Validations to Tool Boundary

## Description

FRD-007 requires that input validation happen at the tool method boundary, before any PDF processing begins. Currently, file path validation is correctly performed at the tool boundary, but three other parameter validations occur inside service methods — after the PDF has already been opened or processing has started:

- **Granularity** (`GetPageText`) — validated in `PageTextService`, not in `GetPageTextTool`.
- **DPI** (`RenderPagePreview`) — validated in `RenderPagePreviewService`, not in `RenderPagePreviewTool`.
- **Format** (`RenderPagePreview`) — validated in `RenderPagePreviewService.NormalizeFormat()`, not in `RenderPagePreviewTool`.
- **Quality** (`RenderPagePreview`) — validated in `RenderPagePreviewService.ValidateQuality()`, not in `RenderPagePreviewTool`.
- **outputPath** (`GetPageImages`) — validated in `PageImagesService.ValidateOutputPath()`, not in `GetPageImagesTool`.
- **outputFile** (`GetPageText`) — validated in `PageTextService.ExtractToFile()`, not in `GetPageTextTool`.
- **Page number minimum** (all page-level tools) — the `page >= 1` check is only performed inside services via `ValidatePageNumber`, after the PDF has been opened.

These validations do not require an open PDF document and should fail fast at the tool boundary, before any I/O or resource allocation.

## Traces To

- **FRD:** FRD-007 (Error Handling & Input Validation), Functional Requirement 1
- **PRD:** REQ-7 (Robust error handling)

## Dependencies

- Task 005 (Input Validation Service) — the service interface will be extended
- Tasks 007, 009, 011, 013, 016 (all tool implementations) — tool methods will be updated

## Technical Requirements

### Extend `IInputValidationService` / `InputValidationService`

1. Add a method to validate that a page number is at least 1. This check does not require the document's page count and can be called before opening the PDF. The existing `ValidatePageNumber(int page, int pageCount)` method must remain unchanged for services that validate `page <= pageCount` after opening the document.

2. Add a method to validate the `granularity` parameter. It must accept `"words"` or `"letters"` using a **case-insensitive** comparison (matching the existing behavior in `PageTextService`, which uses `StringComparer.OrdinalIgnoreCase`). On invalid values, throw `ArgumentException` with the message `"Granularity must be 'words' or 'letters'."`.

3. Add a method to validate the `dpi` parameter. It must accept values from 72 to 600 inclusive. On out-of-range values, throw `ArgumentException` with the message `"DPI must be between 72 and 600."`.

4. Add a method to validate the `format` parameter. It must accept `"png"`, `"jpeg"`, or `"jpg"` using a case-insensitive comparison. On invalid values, throw `ArgumentException` with the message `"Format must be 'png', 'jpeg', or 'jpg'."`.

5. Add a method to validate the `quality` parameter. It must accept values from 1 to 100 inclusive. On out-of-range values, throw `ArgumentException` with the message `"Quality must be between 1 and 100."`.

6. Add a method to validate `outputPath` (used by `GetPageImages`). It must verify the path is absolute, does not contain `..`, and exists as a directory. On failures, throw `ArgumentException` with the messages specified in FRD-007.

7. Add a method to validate `outputFile` (used by `GetPageText`). It must verify the path is absolute, does not contain `..`, and the parent directory exists. On failures, throw `ArgumentException` with the messages specified in FRD-003/FRD-007.

### Update Tool Methods

8. All four page-level tool methods (`GetPageText`, `GetPageGraphics`, `GetPageImages`, `RenderPagePreview`) must call the page-minimum validation method immediately after file path validation, before calling the service.

9. `GetPageTextTool.GetPageText` must call the granularity validation method at the tool boundary, after file path validation and before calling `PageTextService`. When `outputFile` is provided, `GetPageTextTool` must also call the output file validation method at the boundary.

10. `RenderPagePreviewTool.RenderPagePreview` must call the DPI, format, and quality validation methods at the tool boundary, after file path validation and before calling `RenderPagePreviewService`.

11. `GetPageImagesTool.GetPageImages` must call the output path validation method (when `outputPath` is provided) at the tool boundary, after file path validation and before calling `PageImagesService`.

### Service-Side Validation

12. The existing parameter validations in services (`PageTextService` granularity check, `RenderPagePreviewService` DPI/format/quality checks, `PageImagesService` output path check, `PageTextService` output file check) are retained as defense-in-depth. The tool boundary is the authoritative validation point, but the services keep their own checks as a secondary safety net.

8. The `ValidatePageNumber(page, pageCount)` call in services must remain — it validates `page <= pageCount`, which requires an opened PDF.

9. `RenderPagePreviewService` also retains an internal `ValidateFilePath(pdfPath)` call as defense-in-depth, since it opens the PDF via a separate engine (PDFiumCore) from the PdfPig-based services. The PdfPig-based services (`PdfInfoService`, `PageTextService`, `PageGraphicsService`) do not duplicate this call internally — they rely solely on the tool boundary validation.

## Acceptance Criteria

- [ ] Calling any page-level tool with `page = 0` or `page = -1` returns the error `"Page number must be 1 or greater."` without opening the PDF file.
- [ ] Calling `GetPageText` with an invalid `granularity` value returns `"Granularity must be 'words' or 'letters'."` without opening the PDF file.
- [ ] Calling `RenderPagePreview` with `dpi = 50` or `dpi = 700` returns `"DPI must be between 72 and 600."` without opening the PDF file.
- [ ] Calling `RenderPagePreview` with an invalid format returns `"Format must be 'png', 'jpeg', or 'jpg'."` without opening the PDF file.
- [ ] Calling `RenderPagePreview` with `quality = 0` or `quality = 101` returns `"Quality must be between 1 and 100."` without opening the PDF file.
- [ ] Calling `GetPageImages` with an invalid `outputPath` returns the appropriate validation error without opening the PDF file.
- [ ] Calling `GetPageText` with an invalid `outputFile` returns the appropriate validation error without opening the PDF file.
- [ ] All existing unit tests for `InputValidationService` continue to pass.
- [ ] All existing integration tests for all five tools continue to pass.
- [ ] New unit tests cover the added validation methods in `InputValidationService`.

## Testing Requirements

- Add unit tests for the new validation methods in `InputValidationServiceTests`:
  - Page-minimum validation: valid (1, 100), invalid (0, -1, `int.MinValue`).
  - Granularity validation: valid (`"words"`, `"letters"`, `"Words"`, `"LETTERS"`), invalid (`"sentences"`, `""`, `null`).
  - DPI validation: valid (72, 150, 600), invalid (71, 601, 0, -1).
  - Format validation: valid (`"png"`, `"jpeg"`, `"jpg"`, `"PNG"`, `"Jpeg"`), invalid (`"bmp"`, `"gif"`, `""`, `null`).
  - Quality validation: valid (1, 50, 100), invalid (0, 101, -1).
  - Output path validation: valid (absolute existing directory), invalid (relative path, path with `..`, nonexistent directory).
  - Output file validation: valid (absolute path with existing parent), invalid (relative path, path with `..`, nonexistent parent directory).
