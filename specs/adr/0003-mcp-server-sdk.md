# ADR-0003: MCP Server SDK

## Status

Accepted

## Context

The server must implement the Model Context Protocol (MCP) to expose PDF inspection tools to AI agents. Per REQ-9, it must operate over stdio transport as a local child process. We need an SDK that:

- Supports the MCP protocol specification for tool discovery and invocation
- Provides stdio server transport out of the box
- Integrates with .NET dependency injection and hosting (`Microsoft.Extensions.Hosting`)
- Supports attribute-based tool registration for clean, declarative tool definitions

## Decision

Use the **official Model Context Protocol C# SDK** (`ModelContextProtocol` NuGet package from `modelcontextprotocol/csharp-sdk`).

## Alternatives Considered

### Custom MCP implementation over stdin/stdout

- **Pros:** No external dependency; full control over protocol handling.
- **Cons:** Significant effort to implement JSON-RPC message framing, tool schema generation, and protocol compliance; risk of spec drift; maintenance burden.

### Third-party community MCP libraries

- **Pros:** May offer additional convenience features.
- **Cons:** Not officially maintained; risk of abandonment; may lag behind protocol specification changes.

## Consequences

- The SDK provides `WithStdioServerTransport()` for stdio communication, directly satisfying REQ-9.
- Tools are registered via `[McpServerToolType]` and `[McpServerTool]` attributes with `[Description]` for parameter documentation, matching the tool signatures already designed in the concept document.
- Tool discovery uses `WithToolsFromAssembly()` or `WithTools<T>()` for automatic registration.
- The SDK integrates with `Microsoft.Extensions.Hosting`, providing standard .NET application lifecycle management, logging (with console logs routed to stderr to avoid polluting the stdio MCP channel), and dependency injection.
- Server setup is minimal:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
```

- The SDK is officially maintained by the MCP project, reducing risk of protocol incompatibility as the spec evolves.
