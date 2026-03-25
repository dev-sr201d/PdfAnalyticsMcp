# Task 020: Cross-Tool Error Handling Verification Tests

## Description

FRD-007 defines cross-cutting error handling requirements that apply to all five tools. While each tool's individual integration tests already cover many error scenarios, there is no test suite that **systematically verifies error behavior consistency across all tools** and confirms the server's resilience to sequential failures.

This task creates a dedicated integration test class that verifies the FRD-007 acceptance criteria holistically — ensuring identical error messages across tools for the same invalid input, confirming server continuity after errors, and validating that error responses never leak internal details.

## Traces To

- **Feature:** FRD-007 (Error Handling & Input Validation), all acceptance criteria
- **PRD:** REQ-8 (Robust error handling)

## Dependencies

- Task 017 (Standardize Invalid-PDF Error Message)
- Task 018 (Move Fail-Fast Validations to Tool Boundary)
- Task 019 (Per-Page Extraction and Rendering Failure Resilience)
- All tool implementations (Tasks 007, 009, 011, 013, 016)

## Technical Requirements

### Test Class

1. Create an integration test class (e.g., `ErrorHandlingVerificationTests`) in the test project that uses the same MCP client infrastructure as the existing integration tests.

### Error Message Consistency Tests

2. For each of the following error conditions, call **all applicable tools** with the same invalid input and assert that every tool returns the **same validation error message**:
   - Empty `pdfPath` (`""`) → all 5 tools must return `"pdfPath is required."`. (Note: passing a literal `null` for `pdfPath` may be rejected by the MCP SDK at the protocol deserialization level before the tool method is invoked. Use empty string as the reliable test case. If a null test is included, accept either `"pdfPath is required."` or an SDK-level protocol error as valid behavior.)
   - Nonexistent file path → all 5 tools must return `"File not found: {pdfPath}"`.
   - Path traversal (`..` in path) → all 5 tools must return `"Invalid file path."`.
   - Non-PDF file → all 5 tools must return `"The file could not be opened as a PDF."`.
   - Page number 0 → all 4 page-level tools must return `"Page number must be 1 or greater."`.
   - Page number exceeding document page count → all 4 page-level tools must return `"Page {page} does not exist. The document has {pageCount} pages."`.

   **SDK wrapping note:** The MCP SDK wraps `McpException` messages in a per-tool prefix: `"An error occurred invoking '{toolName}': {message}"`. The consistency tests must extract the validation message portion (after the `': '` separator) and compare those across tools, rather than asserting exact equality on the full response string. This ensures the test verifies that all tools produce the same validation message while accommodating the SDK's per-tool wrapping.

### Server Continuity Tests

3. Execute a sequence of tool calls where some fail and some succeed, verifying the server handles the full sequence without becoming unresponsive:
   - Call a tool with invalid input (expect error).
   - Call the same tool with valid input (expect success).
   - Call a different tool with invalid input (expect error).
   - Call that tool with valid input (expect success).
   - All responses must be received and correct.

### Error Response Sanitization Tests

4. For a representative subset of error scenarios, assert that the error response text:
   - Does not contain common stack trace indicators (e.g., `"   at "`, `"Exception"`, `"StackTrace"`).
   - Does not contain internal file path fragments beyond the user-provided path (e.g., no `C:\Users\...` or `/home/...` unless that was the input path).
   - Does not contain .NET type names (e.g., `"NullReferenceException"`, `"PdfDocument"`).

## Acceptance Criteria

- [ ] A new integration test class exists that verifies cross-tool error message consistency.
- [ ] All 5 tools return identical error messages for null/empty path, nonexistent file, path traversal, and non-PDF file.
- [ ] All 4 page-level tools return identical error messages for page 0 and page out of range.
- [ ] Server continuity is verified: a successful call following a failed call returns correct results.
- [ ] Error responses are verified to not contain stack traces, internal paths, or .NET type names.
- [ ] All tests pass.

## Testing Requirements

- All tests in this task are integration tests.
- Use the same MCP client test infrastructure (stdio-based server process) used by existing integration tests.
- The `ReadResponseAsync` helper must skip JSON-RPC notifications (messages without an `id` field) that the MCP SDK may interleave on stdout between tool responses. Only messages with an `id` field are actual JSON-RPC responses.
- Use the same test PDF files already in `tests/TestData/`.
- The test class should be self-contained — it should not depend on test execution order with other test classes.
