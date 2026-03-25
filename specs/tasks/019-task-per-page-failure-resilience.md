# Task 019: Per-Page Extraction and Rendering Failure Resilience

## Description

FRD-007 Functional Requirement 5 states: "When a PDF file can be opened but a specific extraction or rendering operation fails on a page (e.g., a corrupted content stream or an unsupported PDF feature), the error must be reported as a tool error for that specific call — it must not crash the server."

Currently, if a PDF opens successfully but a per-page operation fails (e.g., `page.GetWords()` throws on a corrupt content stream, `page.Paths` throws on malformed path data, or Docnet's `GetImage()` fails on a specific page), the resulting exception is **not** an `ArgumentException`. Because the tool methods only catch `ArgumentException` (to rethrow as `McpException`), these unexpected exceptions fall through to the MCP SDK's generic handler, which returns a generic error message that provides no useful information to the caller.

This task adds targeted exception handling around per-page operations in each service to convert unexpected failures into `ArgumentException` with a clear, caller-facing message.

**Design note:** Using `ArgumentException` for data-level failures (e.g., a corrupt content stream) is semantically imprecise — it normally indicates an invalid argument from the caller. However, this is an intentional trade-off: the established tool-level pattern catches `ArgumentException` and rethrows as `McpException` to surface messages to the caller. The alternative — broadening the tool catch to `Exception` — would risk leaking internal exception messages (PdfPig/Docnet internals) in violation of FRD-007 Functional Requirement 4. Using `ArgumentException` with a sanitized message at the service layer, then converting to `McpException` at the tool layer, keeps error messages clean while reusing the existing pattern.

## Traces To

- **Feature:** FRD-007 (Error Handling & Input Validation), Functional Requirements 5 and 6
- **PRD:** REQ-8 (Robust error handling)

## Dependencies

- Task 008 (GetPageText Service)
- Task 010 (GetPageGraphics Service)
- Task 012 (GetPageImages Service) — already has per-image handling; verify it covers page-level failures too
- Task 015 (RenderPagePreview Service)

## Technical Requirements

### Text Extraction (`PageTextService`)

1. After successfully opening the PDF and retrieving the page, wrap the call to `page.GetWords()` or `page.Letters` access in a try-catch. If an exception occurs, throw `ArgumentException` with the message `"An error occurred extracting text from page {page}."`.

2. The catch must not catch `ArgumentException` itself (to avoid masking validation errors from `ValidatePageNumber`).

### Graphics Extraction (`PageGraphicsService`)

3. After successfully opening the PDF and retrieving the page, wrap the access to `page.Paths` and subsequent classification logic in a try-catch. If an exception occurs, throw `ArgumentException` with the message `"An error occurred extracting graphics from page {page}."`.

4. The catch must not catch `ArgumentException` itself.

### Image Extraction (`PageImagesService`)

5. The service already handles per-image failures via a try-catch _inside_ the `foreach` loop that iterates individual images. However, the `page.GetImages()` call itself (which is the iteration source of the `foreach`) is _outside_ that per-image try-catch. If `page.GetImages()` throws before iteration begins, the exception is unhandled. Add a try-catch _around_ the entire `foreach` loop (not just inside it) that catches exceptions from `page.GetImages()` and reports them as `ArgumentException` with `"An error occurred extracting images from page {page}."`.

6. The catch must not catch `ArgumentException` itself.

### Page Rendering (`RenderPagePreviewService`)

7. After successfully opening the PDF with Docnet and retrieving the page reader, wrap the page reader operations — `pageReader.GetPageWidth()`, `pageReader.GetPageHeight()`, and `pageReader.GetImage()` — together in a single try-catch. All three calls operate on the same native page reader and can fail on corrupt pages, so they share one error boundary. If an exception occurs, throw `ArgumentException` with the message `"An error occurred rendering page {page}."`. Additionally, if `GetImage()` returns null or empty bytes, throw the same `ArgumentException` (this covers cases where PDFium silently fails without throwing).

8. The catch must not catch `ArgumentException` itself.

### General Rules

9. All error messages must include the page number for diagnostic context but must not include exception details, stack traces, or internal library information.

10. The server must remain fully operational after any per-page failure — subsequent tool calls must succeed normally.

## Acceptance Criteria

- [ ] If text extraction fails on a specific page of an otherwise valid PDF, the tool returns `"An error occurred extracting text from page {page}."` and does not crash.
- [ ] If graphics extraction fails on a specific page, the tool returns `"An error occurred extracting graphics from page {page}."` and does not crash.
- [ ] If image extraction fails at the page level (not per-image), the tool returns `"An error occurred extracting images from page {page}."` and does not crash.
- [ ] If page rendering fails after the PDF is opened, the tool returns `"An error occurred rendering page {page}."` and does not crash.
- [ ] `ArgumentException` from validation (e.g., page out of range) is NOT caught by the per-page error handlers — validation errors pass through unchanged.
- [ ] The server continues to handle new tool calls after any per-page failure.

## Testing Requirements

- Add unit tests for each service that verify the per-page error handling:
  - Use a mock or test scenario where the per-page operation would fail (if feasible with real PDFs or through controlled exceptions).
  - Verify the correct error message format including the page number.
  - Verify that `ArgumentException` from validation is not caught (e.g., out-of-range page still returns the validation message, not the generic per-page message).
- No new integration tests are strictly required unless a test PDF with a known corrupt page is available. If such a PDF is impractical to create, unit-level testing of the exception handling logic is sufficient.
