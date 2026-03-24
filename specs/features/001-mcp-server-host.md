# FRD-001: MCP Server Host & Stdio Transport

## Traces To

- **PRD:** REQ-9 (Local stdio transport)
- **ADRs:** ADR-0001 (Language/Runtime), ADR-0003 (MCP SDK)

## Summary

Set up the foundational MCP server application that hosts tools over stdio transport. This is the prerequisite for all other features — no tool can be exposed without the server shell.

## Inputs

- Command-line arguments (passed by the MCP client when launching the server as a child process)

## Outputs

- A running MCP server process that:
  - Communicates with the client over stdin/stdout using the MCP protocol
  - Responds to tool discovery requests (listing available tools)
  - Routes tool invocations to registered tool methods
  - Sends all log output to stderr (never stdout)

## Functional Requirements

1. The server must start as a console application targeting .NET 9.
2. The server must use `Microsoft.Extensions.Hosting` for application lifecycle management.
3. The server must register the MCP server with stdio transport via `AddMcpServer().WithStdioServerTransport()`.
4. The server must discover and register tools from the assembly via `WithToolsFromAssembly()`.
5. All console logging must be routed to stderr (`LogToStandardErrorThreshold = LogLevel.Trace`) so that stdout is reserved exclusively for MCP protocol messages.
6. The server must support graceful shutdown when stdin closes (standard MCP stdio lifecycle).
7. The project must use dependency injection (`IServiceCollection`) for registering application services that tools depend on.

## Dependencies

- `ModelContextProtocol` NuGet package (official C# MCP SDK)
- `Microsoft.Extensions.Hosting` NuGet package
- .NET 9 SDK

## Acceptance Criteria

- [ ] The server compiles and runs as a .NET 9 console application.
- [ ] An MCP client (e.g., VS Code, Claude Desktop) can launch the server as a child process and receive a valid tool list via the MCP protocol.
- [ ] No log output appears on stdout — only MCP protocol JSON-RPC messages.
- [ ] The server shuts down cleanly when the client disconnects (stdin closes).
- [ ] The project structure follows the conventions in AGENTS.md: `src/PdfAnalyticsMcp/Program.cs` with `Tools/`, `Models/`, `Services/` directories prepared.
