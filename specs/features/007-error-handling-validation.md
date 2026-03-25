# FRD-007: Error Handling & Input Validation

## Traces To

- **PRD:** REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK error model), ADR-0005 (Serialization)

## Summary

Define the cross-cutting error handling and input validation behavior for all tools. This feature is authored after the tool implementations because it standardizes patterns that apply across all five tools. The MCP SDK automatically wraps unhandled exceptions into error tool results, but the server must provide clear, actionable validation messages and avoid leaking internal details.

## Scope

This feature applies to all tools: `GetPdfInfo`, `GetPageText`, `GetPageGraphics`, `GetPageImages`, `RenderPagePreview`.

## Validation Rules

### File path validation (all tools)

| Condition | Behavior |
|-----------|----------|
| `pdfPath` is null or empty | Return error: "pdfPath is required." |
| `pdfPath` contains path traversal sequences (`..`) | Return error: "Invalid file path." |
| File does not exist at `pdfPath` | Return error: "File not found: {pdfPath}" |
| File cannot be accessed due to I/O errors (e.g., file is locked, permission denied, sharing violation) | Return error: "The file could not be accessed: {pdfPath}. It may be in use by another process." This error category must be distinct from the invalid PDF error below, so that callers can identify concurrency-related file access issues. This must apply consistently regardless of which underlying engine (extraction or rendering) is used to open the file. |
| File can be accessed but cannot be opened as a valid PDF (e.g., not a PDF, corrupt header) | Return error: "The file could not be opened as a PDF." This must apply consistently regardless of which underlying engine (extraction or rendering) is used to open the file. |

### Page number validation (page-level tools)

| Condition | Behavior |
|-----------|----------|
| `page` is less than 1 | Return error: "Page number must be 1 or greater." |
| `page` exceeds the document's page count | Return error: "Page {page} does not exist. The document has {pageCount} pages." |

### Parameter-specific validation

| Tool | Parameter | Condition | Behavior |
|------|-----------|-----------|----------|
| `GetPageText` | `granularity` | Value is not `"words"` or `"letters"` | Return error: "Granularity must be 'words' or 'letters'." |
| `RenderPagePreview` | `dpi` | Value less than 72 or greater than 600 | Return error: "DPI must be between 72 and 600." |

## Functional Requirements

1. Input validation must happen at the tool method boundary, before any PDF processing begins.
1. The server must handle concurrent tool invocations safely. When multiple tools are invoked in parallel against the same PDF file, each call must succeed independently without data corruption, crashes, or transient failures caused by resource contention between the underlying PDF parsing and rendering engines.
2. Validation errors must be returned to the caller as error responses with clear, user-facing messages.
3. Protocol-level errors must be distinguished from application validation errors. Application validation errors must never be surfaced as protocol errors.
4. Error messages must **never** expose:
   - Stack traces
   - Internal file paths beyond what the user provided
   - System information (OS, .NET version, library internals)
5. When a PDF file can be opened but a specific extraction or rendering operation fails on a page (e.g., a corrupted content stream or an unsupported PDF feature), the error must be reported as a tool error for that specific call — it must not crash the server. This applies equally to the text/graphics/images extraction engine and the page rendering engine, which are independent components.
6. The server must remain operational after any tool error — errors are per-call, not fatal.
7. File path validation must reject path traversal sequences to prevent directory traversal attacks.
8. File-open errors must be classified into two distinct categories: (a) file access/I/O errors (locked files, permission denied, sharing violations) and (b) invalid PDF format errors (not a PDF, corrupt file structure). Each category must produce a distinct error message so that callers can distinguish transient concurrency-related access failures from permanent file format problems.

## Dependencies

- Features 001–006 (all tool implementations) should be complete or in progress.
- Understanding of the MCP SDK error model (ADR-0003).

## Acceptance Criteria

- [ ] Calling any tool with a null or empty `pdfPath` returns a clear error message, not a NullReferenceException.
- [ ] Calling any tool with a nonexistent file path returns "File not found" with the provided path.
- [ ] Calling any tool with a path containing `..` traversal sequences returns "Invalid file path."
- [ ] Calling a page-level tool with page 0 or a negative number returns a clear error.
- [ ] Calling a page-level tool with a page number exceeding the document's page count returns an error showing the valid range.
- [ ] Calling `GetPageText` with an invalid granularity value returns a clear error.
- [ ] Calling `RenderPagePreview` with DPI outside range returns a clear error.
- [ ] Error responses do not contain stack traces or internal system details.
- [ ] The server continues to accept new tool calls after any error — errors are not fatal.
- [ ] Passing a non-PDF file (e.g., a .txt file) returns a meaningful error, not an unhandled exception.
- [ ] When a file is locked or inaccessible due to I/O contention, the error message clearly indicates a file access problem (distinct from "could not be opened as a PDF"), enabling diagnosis of concurrency issues.
- [ ] File access errors and invalid PDF errors produce different, distinguishable error messages across all tools and underlying engines.
- [ ] Calling `RenderPagePreview` on a page that the rendering engine cannot process returns a clear error, not a corrupt image or crash.
- [ ] When multiple tools are invoked in parallel against the same PDF file, all calls succeed independently without transient failures.
