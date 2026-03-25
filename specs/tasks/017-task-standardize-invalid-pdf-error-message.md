# Task 017: Standardize Invalid-PDF Error Message Across Engines

## Description

The server uses two independent engines to open PDF files: PdfPig (text, graphics, images extraction) and Docnet (page rendering). When a non-PDF file is provided, both engines must return the same user-facing error message. Currently, the extraction services return `"The file could not be opened as a PDF."` while the rendering service returns `"The file could not be rendered as a PDF."` This inconsistency violates FRD-007's requirement that the invalid-PDF error "must apply consistently regardless of which underlying engine is used to open the file."

## Traces To

- **Feature:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-8 (Robust error handling)

## Dependencies

- Task 015 (RenderPagePreview Service and DTO) — must be implemented first
- Task 016 (RenderPagePreview Tool and Integration Tests) — existing tests will need updating

## Technical Requirements

1. The `RenderPagePreviewService` must catch exceptions thrown when Docnet fails to open a file and throw an `ArgumentException` with the message `"The file could not be opened as a PDF."` — the same message used by all extraction services (`PdfInfoService`, `PageTextService`, `PageGraphicsService`, `PageImagesService`).

2. The change is limited to the error message string in the rendering service's PDF-open exception handler. The existing `when` guard (`catch (Exception ex) when (ex is not ArgumentException)`) must be preserved — it ensures that `ArgumentException` from validation (e.g., page out of range) is not caught by the PDF-open handler. Only the message string changes.

3. Existing unit tests and integration tests for `RenderPagePreview` that assert the invalid-PDF error message must be updated to expect the standardized message.

## Acceptance Criteria

- [ ] Calling `RenderPagePreview` with a non-PDF file (e.g., `tests/TestData/not-a-pdf.txt`) returns the error message `"The file could not be opened as a PDF."`.
- [ ] The error message is identical to what `GetPdfInfo`, `GetPageText`, `GetPageGraphics`, and `GetPageImages` return for the same non-PDF file.
- [ ] All existing unit tests for `RenderPagePreviewService` pass with the updated message.
- [ ] All existing integration tests for `RenderPagePreview` pass with the updated message.

## Testing Requirements

- Update any existing `RenderPagePreviewService` unit test that asserts the invalid-PDF error message string.
- Update any existing `RenderPagePreviewIntegrationTests` test that asserts the invalid-PDF error message string.
- No new test classes are required — this is a message correction in existing tests.
