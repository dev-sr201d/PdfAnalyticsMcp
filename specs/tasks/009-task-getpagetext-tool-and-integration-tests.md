# Task 009: GetPageText Tool and Integration Tests

## Description

Create the MCP tool class for `GetPageText` and comprehensive integration tests that exercise the tool through the MCP protocol. The tool is a thin wrapper that validates input, delegates to the page text extraction service, and returns serialized JSON. Integration tests verify end-to-end behavior including tool discovery, successful extraction at both granularity levels, default parameter behavior, and error handling.

## Dependencies

- **Task 002** — MCP server host with stdio transport (complete)
- **Task 003** — Test project and server verification (complete)
- **Task 007** — GetPdfInfo tool and integration tests — establishes the MCP integration test pattern (complete)
- **Task 008** — GetPageText service and DTOs (must be complete before this task)

## Technical Requirements

### Tool Class

Define a tool class in `Tools/` that:

1. Is decorated with the `[McpServerToolType]` attribute for automatic discovery.
2. Contains a single tool method decorated with `[McpServerTool]` and `[Description]` attributes.
3. Uses primary constructor to inject `IInputValidationService` and the page text service interface.
4. Accepts three parameters, each with `[Description]` attributes:
   - `pdfPath` (string, required) — Absolute path to the PDF file
   - `page` (int, required) — 1-based page number
   - `granularity` (string, optional) — Level of detail: `"words"` or `"letters"`. Use a C# default parameter value (`string granularity = "words"`) so the MCP SDK advertises the default in the tool schema. Do not handle null granularity in the service.
5. The tool method must:
   - Validate the file path using `IInputValidationService.ValidateFilePath()` — this is the **only** validation the tool performs directly; granularity validation and page number validation are handled inside the service (matching the established pattern where `GetPdfInfoTool` only validates file path)
   - Delegate to the page text service for extraction (passing through all parameters)
   - Serialize the result using `JsonSerializer.Serialize()` with `SerializerConfig.Options`
   - Return the serialized JSON string
   - Catch `ArgumentException` and rethrow as `McpException` to preserve error messages for the agent

### Tool Description

The `[Description]` on the tool method must clearly communicate to AI agents:
- What the tool returns (text elements with position, font, size, color metadata)
- That it operates on a single page
- The purpose of the granularity parameter and its valid values
- That the default granularity is word-level

### Parameter Descriptions

Each parameter's `[Description]` must explain:
- `pdfPath`: That it must be an absolute filesystem path to a PDF file
- `page`: That it is a 1-based page number
- `granularity`: The two valid values (`"words"`, `"letters"`), the default value, and the trade-off (letters produces ~5× more data)

## Acceptance Criteria

- [ ] Tool class is discoverable via MCP `tools/list` request and appears with the correct name, description, and parameter schema.
- [ ] Calling the tool with `granularity = "words"` returns word-level text elements with bounding boxes, font, size, and color metadata.
- [ ] Calling the tool with `granularity = "letters"` returns letter-level text elements with the same metadata fields.
- [ ] Calling the tool without a `granularity` parameter defaults to word-level extraction.
- [ ] Color is represented as `"#RRGGBB"` and omitted for default black text.
- [ ] Bold/italic flags are only present when true.
- [ ] Coordinates in the response are rounded to 1 decimal place.
- [ ] A typical page (~300 words) at word granularity produces a response ≤ 30 KB.
- [ ] Missing or empty `pdfPath` returns an MCP error with a descriptive message.
- [ ] Nonexistent file path returns an MCP error with "File not found" in the message.
- [ ] Path traversal attempt returns an MCP error with "Invalid file path" in the message.
- [ ] Invalid (non-PDF) file returns an MCP error with "could not be opened as a PDF" in the message.
- [ ] Out-of-range page number returns an MCP error with a descriptive message including the valid page range.
- [ ] Invalid granularity value returns an MCP error with a descriptive message listing valid options.

## Testing Requirements

Integration tests must follow the established MCP protocol test pattern from `GetPdfInfoIntegrationTests`:
- Launch the server as a child process communicating over stdio
- Perform MCP handshake (initialize → notifications/initialized) before tool calls
- Send `tools/call` requests with JSON-RPC protocol
- Validate responses with appropriate timeouts

### Required Integration Test Scenarios

1. **Tool discovery** — Send `tools/list` and verify `GetPageText` (or its snake_case equivalent) appears with the expected input schema including `pdfPath`, `page`, and `granularity` parameters.
2. **Word-level extraction** — Call the tool with `granularity = "words"` on a test PDF with known content. Verify the response contains the correct page number, page dimensions, and word elements with `text`, `x`, `y`, `w`, `h`, `font`, `size` fields populated.
3. **Letter-level extraction** — Call the tool with `granularity = "letters"` on the same test PDF. Verify the response contains individual character elements. Verify the element count is greater than word-level (confirming finer granularity).
4. **Default granularity** — Call the tool without specifying `granularity`. Verify the response matches word-level output structurally (same number of elements and same field presence as test 2 — not byte-identical JSON comparison).
5. **Color metadata** — Verify that non-black text elements include a `color` field with a valid `"#RRGGBB"` value, and that black text elements omit the `color` field.
6. **Bold/italic flags** — Verify that elements from bold fonts include `bold: true`, and elements without bold/italic styling omit those fields.
7. **Response size** — Verify that the response for a typical page at word granularity is ≤ 30 KB. This test requires a test PDF with approximately 300 words on a single page (created in Task 008's test data).
8. **Missing file path** — Call with empty `pdfPath`. Verify the MCP error response contains "pdfPath is required".
9. **File not found** — Call with a nonexistent file path. Verify the error contains "File not found".
10. **Path traversal** — Call with a path containing `..`. Verify the error contains "Invalid file path".
11. **Invalid PDF** — Call with a non-PDF file. Verify the error mentions the file could not be opened as a PDF.
12. **Page out of range** — Call with a page number beyond the document's page count. Verify the error includes the valid page range.
13. **Page number zero or negative** — Call with `page = 0`. Verify the error contains "Page number must be 1 or greater."
14. **Invalid granularity** — Call with `granularity = "paragraphs"` (or another invalid value). Verify the error lists valid granularity options.
