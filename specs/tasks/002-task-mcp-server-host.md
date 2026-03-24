# Task 002: MCP Server Host with Stdio Transport

## Description

Implement the MCP server host configuration in `Program.cs` so that the application starts as a fully functional MCP server communicating over stdio. This includes host builder setup, MCP server registration with stdio transport, attribute-based tool discovery, logging routed to stderr, and dependency injection wiring.

## Traces To

- **FRD:** FRD-001 (MCP Server Host & Stdio Transport)
- **PRD:** REQ-9 (Local stdio transport)
- **ADRs:** ADR-0003 (MCP SDK)

## Dependencies

- **Task 001** (Solution & Project Scaffolding) must be completed first.

## Technical Requirements

### Host Builder Configuration

- Use `Host.CreateApplicationBuilder(args)` for standard .NET hosting.
- Configure console logging so that **all** log output goes to stderr, not stdout. Use `LogToStandardErrorThreshold = LogLevel.Trace` to route every log level to stderr.
- Stdout must be reserved exclusively for MCP JSON-RPC protocol messages.

### MCP Server Registration

- Register the MCP server via `builder.Services.AddMcpServer()`.
- Configure stdio transport via `.WithStdioServerTransport()`.
- Enable attribute-based tool discovery via `.WithToolsFromAssembly()` so that any class in the assembly marked with `[McpServerToolType]` and methods marked with `[McpServerTool]` are automatically registered.

### Dependency Injection Preparation

- The `IServiceCollection` must be accessible for future tasks to register application services (PDF extraction services, etc.).
- No application services need to be registered in this task — the DI container setup is sufficient.

### Graceful Shutdown

- The server must shut down cleanly when stdin closes, which is the standard MCP stdio lifecycle. The hosting framework handles this by default when using stdio transport.

### Placeholder Tool

- Include a minimal placeholder tool class (e.g., a `PingTool` in `Tools/PingTool.cs` that returns a static string) to verify that tool discovery and invocation work end-to-end. This placeholder will be removed once the first real tool is added.
- The placeholder must use `[McpServerToolType]` on the class and `[McpServerTool]` with `[Description]` on the method, following AGENTS.md conventions.

## Acceptance Criteria

- [ ] `Program.cs` configures the host with `AddMcpServer()`, `WithStdioServerTransport()`, and `WithToolsFromAssembly()`.
- [ ] All console logging is routed to stderr — no log output appears on stdout.
- [ ] The server starts and waits for MCP protocol messages on stdin.
- [ ] An MCP client can connect to the server as a child process and receive a valid tool list (even if the list contains only a placeholder tool or is empty).
- [ ] The server shuts down cleanly when stdin is closed.
- [ ] The code follows AGENTS.md conventions: file-scoped namespaces, nullable reference types, no suppressed nullable warnings.
