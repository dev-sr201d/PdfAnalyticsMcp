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
| File cannot be opened as a valid PDF | Return error: "The file could not be opened as a PDF." |

### Page number validation (page-level tools)

| Condition | Behavior |
|-----------|----------|
| `page` is less than 1 | Return error: "Page number must be 1 or greater." |
| `page` exceeds the document's page count | Return error: "Page {page} does not exist. The document has {pageCount} pages." |

### Parameter-specific validation

| Tool | Parameter | Condition | Behavior |
|------|-----------|-----------|----------|
| `GetPageText` | `granularity` | Value is not `"words"` or `"letters"` | Return error: "Granularity must be 'words' or 'letters'." |
| `RenderPagePreview` | `dpi` | Value outside reasonable range (e.g., < 72 or > 600) | Return error: "DPI must be between 72 and 600." |

## Functional Requirements

1. Input validation must happen at the tool method boundary, before any PDF processing begins.
2. Validation errors must throw `ArgumentException` with a clear, user-facing message. The MCP SDK catches these and returns them as error tool results with `IsError = true`.
3. `McpProtocolException` must only be thrown for protocol-level issues, not for application validation errors.
4. Error messages must **never** expose:
   - Stack traces
   - Internal file paths beyond what the user provided
   - System information (OS, .NET version, library internals)
5. When a PDF file can be opened but a specific extraction operation fails on a page (e.g., a corrupted content stream), the error must be reported as a tool error for that specific call — it must not crash the server.
6. The server must remain operational after any tool error — errors are per-call, not fatal.
7. File path validation must reject path traversal sequences to prevent directory traversal attacks.

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
