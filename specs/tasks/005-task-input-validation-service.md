# Task 005: Input Validation Service

## Description

Create a shared input validation service that all MCP tools will use to validate common parameters before PDF processing begins. This service centralizes file path validation and page number validation, ensuring consistent error messages and security checks across all tools per FRD-007.

## Traces To

- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK error model)

## Dependencies

- **Task 001** (Solution & Project Scaffolding) must be completed first.

## Technical Requirements

### File Path Validation

The service must validate the `pdfPath` parameter used by all tools, applying the following rules in order:

| Condition | Error Message |
|-----------|---------------|
| `pdfPath` is null or empty | "pdfPath is required." |
| `pdfPath` contains path traversal sequences (`..`) | "Invalid file path." |
| File does not exist at `pdfPath` | "File not found: {pdfPath}" |

- Validation failures must throw `ArgumentException` with the specified message. The calling tool method is responsible for catching `ArgumentException` and rethrowing as `McpException` so the MCP SDK preserves the error message in the response (see AGENTS.md Section 5, Error Handling).
- Path traversal detection must reject any path containing `..` to prevent directory traversal attacks (OWASP path traversal).

### Page Number Validation

The service must validate the `page` parameter used by page-level tools:

| Condition | Error Message |
|-----------|---------------|
| `page` is less than 1 | "Page number must be 1 or greater." |
| `page` exceeds `pageCount` | "Page {page} does not exist. The document has {pageCount} pages." |

- Page number validation is separate from file path validation — it requires an already-opened document to know the page count.
- Validation failures must throw `ArgumentException` with the specified message.

### PDF File Open Validation

> **Note:** This requirement describes behavior that is _not_ implemented in the validation service itself. The validation service only handles pre-open checks (null/empty, traversal, file existence). The try-catch around `PdfDocument.Open()` belongs in the extraction service or tool method, since it requires a PdfPig dependency. This section is included here for completeness because FRD-007 groups all validation behaviors together, but the implementation responsibility falls on the caller that opens the PDF.

- When a file exists but cannot be opened as a valid PDF (e.g., a `.txt` file renamed to `.pdf`, or a corrupted file), the error must be caught and rethrown as an `ArgumentException` with message: "The file could not be opened as a PDF."
- Internal exception details (stack traces, library-specific messages) must not be exposed in the error message.
- The extraction service or tool method that opens the PDF must implement this catch — not the validation service.

### Error Message Standards

- Error messages must never expose stack traces, internal file paths beyond what the user provided, or system information.
- Error messages must be clear, actionable, and consistent with FRD-007 specifications.
- The server must remain operational after any validation error — errors are per-call, not fatal.

### Registration

- The validation service must be registered in the dependency injection container so all tools can receive it via constructor injection.

## Acceptance Criteria

- [ ] A validation service exists that all tool classes can use via dependency injection.
- [ ] Passing a null or empty `pdfPath` produces the error "pdfPath is required."
- [ ] Passing a path containing `..` produces the error "Invalid file path."
- [ ] Passing a nonexistent file path produces "File not found: {pdfPath}" with the provided path.
- [ ] The validation service does NOT open the PDF file — it only validates pre-open conditions. PDF-open validation (invalid/corrupt file) is the responsibility of the extraction service or tool method.
- [ ] Passing page number 0 or negative produces "Page number must be 1 or greater."
- [ ] Passing a page number exceeding the document page count produces the out-of-range error with the actual page count.
- [ ] Error messages never contain stack traces or internal system details.
- [ ] Unit tests cover all validation rules including boundary cases.
- [ ] The validation service is registered as a singleton in the DI container.
