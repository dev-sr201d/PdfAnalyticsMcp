# ADR-0005: Serialization and Response Format

## Status

Accepted

## Context

Each MCP tool returns structured data representing PDF content — text elements with positional/stylistic metadata, classified graphics, image metadata, and document info. The data must be:

- Machine-parseable by the AI agent receiving tool responses
- Compact enough to stay within practical LLM context window limits (REQ-6 targets ≤ 30 KB per call for a typical page)
- Self-describing so the agent can interpret it without external schema knowledge

The MCP protocol transmits tool results as text content within JSON-RPC messages. We need to decide on the serialization format for tool return values.

## Decision

Use **System.Text.Json** (built into .NET) to serialize tool responses as JSON. Define lean DTO (Data Transfer Object) types for each tool's response, applying server-side classification and aggregation before serialization to minimize payload size.

### Serialization conventions

- Use `camelCase` property naming (`JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase`).
- Omit null/default properties (`DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`) to reduce payload size.
- Round coordinate values to 1 decimal place (0.1 PDF points ≈ 0.0014 inches — sufficient precision for layout analysis).
- Encode colors as hex strings (`"#RRGGBB"`) rather than separate R/G/B integer properties.
- Return image data as base64-encoded PNG strings only when explicitly requested (`includeData = true`).

## Alternatives Considered

### Newtonsoft.Json (Json.NET)

- **Pros:** Feature-rich; widely used in legacy .NET projects; more flexible serialization options.
- **Cons:** External dependency; slower than System.Text.Json in modern .NET; not recommended for new .NET 9 projects; adds unnecessary package dependency.

### Custom compact text format (TSV or custom DSL)

- **Pros:** Could achieve even smaller payloads than JSON.
- **Cons:** Requires custom parsing logic; not self-describing; agents may struggle with non-standard formats; harder to debug; loses interoperability.

### MessagePack or Protocol Buffers

- **Pros:** Very compact binary formats; fast serialization.
- **Cons:** MCP tool results are text-based; binary formats would require base64 encoding, negating size benefits; not human-readable for debugging; adds complexity.

## Consequences

- System.Text.Json is included in .NET 9 — no additional NuGet dependency.
- Using source generators (`[JsonSerializable]`) enables AOT-compatible, allocation-efficient serialization if needed in the future.
- Server-side classification (e.g., grouping raw PDF operations into rectangles/lines rather than returning raw ops) is done before serialization, keeping payloads focused on what the agent needs.
- Coordinate rounding from ~15 significant digits to 1 decimal place significantly reduces JSON string length for dense pages with thousands of positioned elements.
- The `camelCase` convention matches typical JSON conventions and is immediately familiar to LLMs trained on web data.
- Omitting nulls and using compact color representation (`"#FF0000"` vs `{"r":255,"g":0,"b":0}`) reduces typical per-element overhead.
