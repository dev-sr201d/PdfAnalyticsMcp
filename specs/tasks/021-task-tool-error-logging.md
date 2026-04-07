# Task 021: Tool-Level Error Logging to Stderr

## Description

FRD-007 Functional Requirement 11 and the final acceptance criterion require that all tool errors be logged server-side to stderr at an appropriate severity level, including diagnostic context not exposed to the caller (such as exception type and message). Currently, tool methods catch `ArgumentException` and rethrow as `McpException` without logging. This means errors are silent on the server side — the only output is the sanitized message sent back to the MCP client.

This task adds structured logging to each tool method's error handling path so that server operators and developers can diagnose issues from stderr logs.

## Traces To

- **FRD:** FRD-007 (Error Handling & Input Validation), Functional Requirement 11
- **PRD:** REQ-7 (Robust error handling)

## Dependencies

- Tasks 007, 009, 011, 014, 016 (all tool implementations) — tools must be complete before adding logging
- Task 002 (MCP Server Host) — logging to stderr must be configured

## Technical Requirements

1. Each tool class must accept an `ILogger<T>` via constructor injection (primary constructor).

2. In each tool method's `catch (ArgumentException ex)` block, log the exception at `Warning` level before rethrowing as `McpException`. The log message should include the tool name, the original exception message, and the exception object (for stack trace in verbose logging).

3. In `RenderPagePreviewTool` and `GetPageImagesTool`, which also catch `InvalidOperationException`, apply the same logging at `Warning` level.

4. Logged messages must include diagnostic context (exception type, message) but the `McpException` rethrown to the caller must continue to contain only the sanitized message — no change to caller-facing behavior.

5. The logging severity must be `Warning` for validation errors (bad input) and `Error` for unexpected operational failures (per-page extraction/rendering errors).

## Acceptance Criteria

- [ ] All five tool classes inject `ILogger<T>` and log errors before rethrowing as `McpException`.
- [ ] Validation errors (e.g., invalid path, bad page number) are logged at `Warning` level.
- [ ] Operational errors (e.g., per-page extraction failure) are logged at `Error` level.
- [ ] Log messages include the original exception type and message for diagnostic purposes.
- [ ] Caller-facing error messages remain unchanged — logging does not alter the `McpException` content.
- [ ] Logs are emitted to stderr (verified by the existing `LogToStandardErrorThreshold` configuration).
