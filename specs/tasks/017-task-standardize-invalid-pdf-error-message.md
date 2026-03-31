# Task 017: Classify File-Open Errors — I/O Access vs. Invalid PDF

## Description

The server uses two independent engines to open PDF files: PdfPig (text, graphics, images extraction) and Docnet (page rendering). FRD-007 Functional Requirement 9 requires that file-open errors be classified into two distinct categories:

1. **File access/I/O errors** (file locked, permission denied, sharing violation) → `"The file could not be accessed: {pdfPath}. It may be in use by another process."`
2. **Invalid PDF format errors** (not a PDF, corrupt header) → `"The file could not be opened as a PDF."`

Currently, all five services catch `Exception` broadly on file open and throw a single `ArgumentException("The file could not be opened as a PDF.")`, making it impossible to distinguish transient concurrency-related file access failures from permanent file format problems. This violates FRD-007 Requirement 9 and prevents diagnosis of concurrency issues when multiple tools access the same PDF in parallel.

Both error messages must be consistent across both engines (PdfPig and Docnet).

## Traces To

- **FRD:** FRD-007 (Error Handling & Input Validation), Functional Requirement 9
- **PRD:** REQ-8 (Robust error handling), REQ-10 (Concurrent tool safety)

## Dependencies

- Task 013 (RenderPagePreview Service and DTO) — must be implemented first
- Task 014 (RenderPagePreview Tool and Integration Tests) — existing tests will need updating

## Technical Requirements

### File-Open Exception Handling (All Services)

1. In each service that opens a PDF file (`PdfInfoService`, `PageTextService`, `PageGraphicsService`, `PageImagesService`, `RenderPagePreviewService`), the file-open catch block must be updated to check the exception type **before** falling through to the generic invalid-PDF error:

   - Catch `IOException` or `UnauthorizedAccessException` first → throw `ArgumentException` with the message `"The file could not be accessed: {pdfPath}. It may be in use by another process."`.
   - For all other exceptions → throw `ArgumentException` with the message `"The file could not be opened as a PDF."`.

2. The existing `when` guard in `RenderPagePreviewService` (`catch (Exception ex) when (ex is not ArgumentException)`) must be preserved — it ensures that `ArgumentException` from validation (e.g., page out of range) is not caught by the file-open handler.

3. For the four PdfPig-based services (`PdfInfoService`, `PageTextService`, `PageGraphicsService`, `PageImagesService`), which currently use an unguarded `catch (Exception)`, the same `when (ex is not ArgumentException)` guard should be added for consistency and safety.

### Consistency Requirement

4. Both error messages must be identical across all five services, regardless of which engine (PdfPig or Docnet) is used to open the file. The I/O access error message includes `{pdfPath}` to help the caller identify which file had the access issue.

### Error Message Inclusion in `{pdfPath}`

5. The `{pdfPath}` in the I/O access error message must be the same path provided by the caller — it must not be resolved, expanded, or otherwise transformed. This aligns with FRD-007 Functional Requirement 5 (never expose internal file paths beyond what the user provided).

## Acceptance Criteria

- [ ] Calling any tool when the PDF file is locked by another process returns the error message `"The file could not be accessed: {pdfPath}. It may be in use by another process."`.
- [ ] Calling any tool with a non-PDF file (e.g., `tests/TestData/not-a-pdf.txt`) returns the error message `"The file could not be opened as a PDF."`.
- [ ] The I/O access error message is identical across all five tools (PdfPig-based and Docnet-based).
- [ ] The invalid-PDF error message is identical across all five tools.
- [ ] The two error messages are clearly distinguishable from each other.
- [ ] All existing unit tests for all services pass (updated where necessary for the new exception handling structure).
- [ ] All existing integration tests for all tools pass.
- [ ] `ArgumentException` from validation (e.g., page out of range) is not caught by the file-open handlers.

## Testing Requirements

- Add unit tests to each service's test class that verify the I/O access error path:
  - Use a file locked with a `FileStream` (e.g., `FileShare.None`) to simulate an `IOException`.
  - Assert the error message matches `"The file could not be accessed: {pdfPath}. It may be in use by another process."`.
- Update any existing unit tests that assert the invalid-PDF error message if the exception handling structure changes (e.g., adding `when` guards).
- Verify existing non-PDF file tests still produce `"The file could not be opened as a PDF."`.
- No new integration test classes are required — cross-tool consistency is verified in Task 020.
