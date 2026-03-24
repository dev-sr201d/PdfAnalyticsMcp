# Task 003: Test Project Setup & Server Verification Tests

## Description

Create the xUnit test project and write integration tests that verify the MCP server host starts correctly, responds to tool discovery, and shuts down gracefully. These tests validate the foundational server infrastructure before any feature tools are added.

## Traces To

- **FRD:** FRD-001 (MCP Server Host & Stdio Transport)
- **AGENTS.md:** Section 10 (Testing Guidelines)

## Dependencies

- **Task 002** (MCP Server Host with Stdio Transport) must be completed first.

## Technical Requirements

### Test Project Setup

- Create the test project at `tests/PdfAnalyticsMcp.Tests/PdfAnalyticsMcp.Tests.csproj`.
- The test project must target `net9.0` and reference the main `src/PdfAnalyticsMcp` project.
- Use xUnit as the test framework.
- Add the test project to the solution file (`PdfAnalyticsMcp.sln`).

### Test Data Directory

- Create a `tests/TestData/` directory for sample PDF files used by future tests. Include at least one minimal valid PDF file for smoke testing (can be generated programmatically or checked in as a small binary).

### Integration Tests: Server Lifecycle

Write integration tests that verify the following scenarios by launching the server as a child process and communicating over stdio:

1. **Server starts successfully** — The server process starts without errors and is ready to accept MCP protocol messages.
2. **Tool discovery returns a valid response** — After completing the MCP initialization handshake (see below), sending a `tools/list` request returns a well-formed JSON-RPC response containing the placeholder tool.
3. **Server shuts down on stdin close** — Closing the server's stdin causes the process to exit with a zero exit code within a reasonable timeout.
4. **Stderr receives log output** — When the server starts, log output is written to stderr (not stdout).

### MCP Protocol Initialization Sequence

The MCP protocol requires a handshake before any tool-related requests will succeed. Tests that interact with the protocol must follow this sequence:

1. Send an `initialize` JSON-RPC request (method: `initialize`) with protocol version and client capabilities.
2. Receive the `initialize` response from the server (contains server capabilities).
3. Send an `initialized` JSON-RPC notification.
4. Only then send `tools/list` or other tool requests.

Tests that skip this handshake will fail at the protocol level.

### Test Approach

- Tests should launch the compiled server executable as a `System.Diagnostics.Process`, write MCP JSON-RPC messages to its stdin, and read responses from its stdout.
- The test project must build the server project as a dependency. Use the server's known output path (e.g., `bin/Debug/net9.0/PdfAnalyticsMcp.exe`) or resolve it programmatically.
- Use a reasonable timeout (e.g., 10 seconds) for all operations to prevent tests from hanging.
- Tests must be deterministic and not depend on external state.

## Acceptance Criteria

- [ ] `tests/PdfAnalyticsMcp.Tests/PdfAnalyticsMcp.Tests.csproj` exists, targets `net9.0`, references xUnit, and is included in the solution.
- [ ] The test project has a project reference to `src/PdfAnalyticsMcp`.
- [ ] `tests/TestData/` directory exists.
- [ ] An integration test verifies that the server process starts without errors.
- [ ] An integration test verifies that a `tools/list` request (after the MCP `initialize` handshake) returns a valid response containing the placeholder tool.
- [ ] An integration test verifies that the server exits cleanly when stdin is closed.
- [ ] An integration test verifies that log output appears on stderr, not stdout.
- [ ] `dotnet test` passes with all tests green.
- [ ] All four integration test scenarios pass and no Task 002 acceptance criteria remain untested.
