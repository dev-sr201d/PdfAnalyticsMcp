# Task 018: Move Fail-Fast Parameter Validations to Tool Boundary

## Description

FRD-007 requires that input validation happen at the tool method boundary, before any PDF processing begins. Currently, file path validation is correctly performed at the tool boundary, but three other parameter validations occur inside service methods — after the PDF has already been opened or processing has started:

- **Granularity** (`GetPageText`) — validated in `PageTextService`, not in `GetPageTextTool`.
- **DPI** (`RenderPagePreview`) — validated in `RenderPagePreviewService`, not in `RenderPagePreviewTool`.
- **Page number minimum** (all page-level tools) — the `page >= 1` check is only performed inside services via `ValidatePageNumber`, after the PDF has been opened.

These validations do not require an open PDF document and should fail fast at the tool boundary, before any I/O or resource allocation.

## Traces To

- **Feature:** FRD-007 (Error Handling & Input Validation), Functional Requirement 1
- **PRD:** REQ-8 (Robust error handling)

## Dependencies

- Task 005 (Input Validation Service) — the service interface will be extended
- Tasks 007, 009, 011, 013, 016 (all tool implementations) — tool methods will be updated

## Technical Requirements

### Extend `IInputValidationService` / `InputValidationService`

1. Add a method to validate that a page number is at least 1. This check does not require the document's page count and can be called before opening the PDF. The existing `ValidatePageNumber(int page, int pageCount)` method must remain unchanged for services that validate `page <= pageCount` after opening the document.

2. Add a method to validate the `granularity` parameter. It must accept `"words"` or `"letters"` using a **case-insensitive** comparison (matching the existing behavior in `PageTextService`, which uses `StringComparer.OrdinalIgnoreCase`). On invalid values, throw `ArgumentException` with the message `"Granularity must be 'words' or 'letters'."`.

3. Add a method to validate the `dpi` parameter. It must accept values from 72 to 600 inclusive. On out-of-range values, throw `ArgumentException` with the message `"DPI must be between 72 and 600."`.

### Update Tool Methods

4. All four page-level tool methods (`GetPageText`, `GetPageGraphics`, `GetPageImages`, `RenderPagePreview`) must call the page-minimum validation method immediately after file path validation, before calling the service.

5. `GetPageTextTool.GetPageText` must call the granularity validation method at the tool boundary, after file path validation and before calling `PageTextService`.

6. `RenderPagePreviewTool.RenderPagePreview` must call the DPI validation method at the tool boundary, after file path validation and before calling `RenderPagePreviewService`.

### Service-Side Validation

7. The existing parameter validations in services (`PageTextService` granularity check, `RenderPagePreviewService` DPI check) are retained as defense-in-depth. The tool boundary is the authoritative validation point, but the services keep their own checks as a secondary safety net. The `PageTextService` granularity check message was updated to match the FRD-007 message (`"Granularity must be 'words' or 'letters'."`).

8. The `ValidatePageNumber(page, pageCount)` call in services must remain — it validates `page <= pageCount`, which requires an opened PDF.

9. `RenderPagePreviewService` also retains an internal `ValidateFilePath(pdfPath)` call as defense-in-depth, since it opens the PDF via a separate engine (Docnet) from the PdfPig-based services. The PdfPig-based services (`PdfInfoService`, `PageTextService`, `PageGraphicsService`, `PageImagesService`) do not duplicate this call internally — they rely solely on the tool boundary validation.

## Acceptance Criteria

- [ ] Calling any page-level tool with `page = 0` or `page = -1` returns the error `"Page number must be 1 or greater."` without opening the PDF file.
- [ ] Calling `GetPageText` with an invalid `granularity` value returns `"Granularity must be 'words' or 'letters'."` without opening the PDF file.
- [ ] Calling `RenderPagePreview` with `dpi = 50` or `dpi = 700` returns `"DPI must be between 72 and 600."` without opening the PDF file.
- [ ] All existing unit tests for `InputValidationService` continue to pass.
- [ ] All existing integration tests for all five tools continue to pass.
- [ ] New unit tests cover the added validation methods in `InputValidationService`.

## Testing Requirements

- Add unit tests for the new validation methods in `InputValidationServiceTests`:
  - Page-minimum validation: valid (1, 100), invalid (0, -1, `int.MinValue`).
  - Granularity validation: valid (`"words"`, `"letters"`, `"Words"`, `"LETTERS"`), invalid (`"sentences"`, `""`, `null`).
  - DPI validation: valid (72, 150, 600), invalid (71, 601, 0, -1).
- Verify existing integration tests still pass — no new integration tests are required since the error messages and behavior are unchanged from the caller's perspective.
