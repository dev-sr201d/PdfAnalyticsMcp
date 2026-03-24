# Task 007: GetPdfInfo Tool and Integration Tests

## Description

Create the `GetPdfInfo` MCP tool class that wires together the input validation service and the PDF metadata extraction service, exposing document metadata retrieval as an MCP-callable tool. Write integration tests that exercise the tool end-to-end through the MCP protocol. Remove the placeholder PingTool now that a real tool exists.

## Traces To

- **FRD:** FRD-002 (Document Metadata Retrieval — GetPdfInfo)
- **FRD:** FRD-007 (Error Handling & Input Validation)
- **PRD:** REQ-1 (Document metadata retrieval), REQ-8 (Robust error handling)
- **ADRs:** ADR-0003 (MCP SDK), ADR-0005 (Serialization)

## Dependencies

- **Task 004** (Shared Serialization Configuration) must be completed first.
- **Task 005** (Input Validation Service) must be completed first.
- **Task 006** (GetPdfInfo Service and DTOs) must be completed first.

## Technical Requirements

### GetPdfInfo Tool Class

- Create a tool class in `Tools/` following AGENTS.md conventions:
  - Class marked with `[McpServerToolType]`.
  - Tool method marked with `[McpServerTool]` and `[Description]`.
  - Every parameter must have a `[Description]` attribute with a clear, actionable description for the AI agent.
- The tool method must be a thin wrapper:
  1. Call the input validation service to validate `pdfPath` (null/empty, path traversal, file existence).
  2. Call the metadata extraction service to extract document metadata.
  3. Serialize the result DTO to JSON using the shared serializer options.
  4. Return the JSON string.
- The tool receives its dependencies (validation service, extraction service) via constructor injection. The MCP SDK resolves DI for instance tool classes.
- Tool method name must be `GetPdfInfo` (verb-noun, concise). The MCP SDK automatically converts this to the snake_case wire name `get_pdf_info` in the tool registry.

### Tool Parameter

| Parameter | Type | Required | Description (for `[Description]` attribute) |
|-----------|------|----------|----------------------------------------------|
| `pdfPath` | string | Yes | "Absolute path to the PDF file on the local filesystem." |

### Return Value

- A JSON string containing the serialized `PdfInfoDto` (the MCP SDK wraps this in a `TextContentBlock`).
- The JSON must use camelCase, omit nulls, and be compact (no indentation).

### Remove Placeholder Tool

- Delete the `PingTool` class from `Tools/`. It was a placeholder introduced in Task 002 for verification purposes and is no longer needed.
- Remove the tool discovery test from `McpServerIntegrationTests` that asserted the placeholder tool appeared in the `tools/list` response. Tool discovery is now covered by the `GetPdfInfoIntegrationTests` (see Integration Tests section below).
- The "server shuts down" and "stderr logging" tests from Task 003 in `McpServerIntegrationTests` should remain unchanged.

### DI Registration in Program.cs

- Register the input validation service and metadata extraction service in `Program.cs` so they are available for constructor injection into the tool class.

### Integration Tests

Write integration tests that launch the server as a child process and communicate over MCP stdio protocol. These tests must follow the MCP initialization handshake (initialize → initialized notification → tool requests).

Required test scenarios:

1. **Tool discovery** — `tools/list` returns `GetPdfInfo` with its parameter schema and description.
2. **Valid PDF metadata** — Calling `GetPdfInfo` with a valid sample PDF returns correct page count, page dimensions, and metadata fields as JSON.
3. **PDF with bookmarks** — Calling `GetPdfInfo` on a PDF with bookmarks returns the hierarchical bookmark tree.
4. **PDF without bookmarks** — Calling `GetPdfInfo` on a PDF without bookmarks omits the `bookmarks` field from the response.
5. **Missing file** — Calling `GetPdfInfo` with a nonexistent path returns an error result with "File not found" message.
6. **Empty path** — Calling `GetPdfInfo` with an empty `pdfPath` returns an error result with "pdfPath is required."
7. **Path traversal** — Calling `GetPdfInfo` with a path containing `..` returns an error result with "Invalid file path."
8. **Invalid PDF file** — Calling `GetPdfInfo` with a non-PDF file returns "The file could not be opened as a PDF."

### Test Data

- Reuse the sample PDF files created in Task 006.
- Reuse the `not-a-pdf.txt` file already in `tests/TestData/` for the invalid-PDF test case.

## Acceptance Criteria

- [ ] A `GetPdfInfo` tool class exists with proper MCP attributes and descriptions.
- [ ] The tool method validates input, extracts metadata, serializes, and returns JSON.
- [ ] The tool receives dependencies via constructor injection.
- [ ] The PingTool placeholder has been removed.
- [ ] The input validation service and metadata extraction service are registered in `Program.cs`.
- [ ] Integration test: `tools/list` includes `GetPdfInfo` with its parameter schema.
- [ ] Integration test: Valid PDF returns correct metadata as JSON.
- [ ] Integration test: PDF with bookmarks returns hierarchical bookmark tree.
- [ ] Integration test: PDF without bookmarks omits the `bookmarks` field.
- [ ] Integration test: Nonexistent file path returns an error with "File not found."
- [ ] Integration test: Empty `pdfPath` returns an error with "pdfPath is required."
- [ ] Integration test: Path containing `..` returns "Invalid file path."
- [ ] Integration test: Non-PDF file returns "The file could not be opened as a PDF."
- [ ] All existing tests from Task 003 still pass (tool discovery test removed from McpServerIntegrationTests, now covered in GetPdfInfoIntegrationTests).
- [ ] `dotnet test` passes with all tests green.
