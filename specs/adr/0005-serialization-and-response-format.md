# ADR-0005: Serialization and Response Format

## Status

Accepted — amended 2026-03-26

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
- Disable indentation (`WriteIndented = false`) — saves bytes on large responses; agents don't need pretty-printed JSON.
- When image extraction is requested, write image data as PNG files to a caller-specified output directory rather than encoding inline. See FRD-006 for the `outputPath` parameter design.

### File-based output for large responses

For tools where a typical page can produce responses exceeding the ≤ 30 KB inline target (e.g., dense pages with `GetPageText`), an optional `outputFile` parameter allows callers to redirect the element data to a CSV file on disk. When `outputFile` is provided:

- The tool returns a compact **summary DTO** inline (< 1 KB) containing page metadata (page number, width, height), element count, the output file path, and the file size in bytes. The envelope data lives in the summary — it is not duplicated in the file.
- The file contains only the flat element data in CSV format — one header row followed by one row per element. The CSV columns match the element DTO fields (e.g., `text,x,y,w,h,font,size,color,bold,italic`). Optional fields (`color`, `bold`, `italic`) are empty when not applicable.
- The caller can then read the file independently, outside of the MCP response payload.
- The `outputFile` path is validated: must be absolute, must not contain path traversal sequences (`..`), and the parent directory must exist. If the file already exists it is overwritten.

CSV eliminates the ~40 bytes of repeated JSON key names per element, reducing token consumption by ~50% compared to JSON for the same data. For a 300-word page this saves ~12 KB of tokens; for dense pages the savings are proportionally larger. The envelope data (`page`, `width`, `height`) is already present in the inline summary, so it is not repeated in the CSV file. LLMs are well-trained on CSV and a header row provides sufficient schema clarity.

When `outputFile` is omitted, the full JSON data is returned inline as before. Currently implemented for `GetPageText` (FRD-003). The pattern can be adopted by other tools (e.g., `GetPageGraphics`, `GetPageImages`) if their payloads also risk exceeding the inline size target.

## Alternatives Considered

### Newtonsoft.Json (Json.NET)

- **Pros:** Feature-rich; widely used in legacy .NET projects; more flexible serialization options.
- **Cons:** External dependency; slower than System.Text.Json in modern .NET; not recommended for new .NET 9 projects; adds unnecessary package dependency.

### Custom compact text format (TSV or custom DSL)

- **Pros:** Could achieve even smaller payloads than JSON.
- **Cons:** Requires custom parsing logic; not self-describing; agents may struggle with non-standard formats; harder to debug; loses interoperability.

> **Revisited (2026-03-26):** Standard CSV (with a header row) is sufficiently self-describing for LLMs and does not require custom parsing. File-based output always uses CSV for maximum token efficiency. The inline MCP response remains JSON.

### MessagePack or Protocol Buffers

- **Pros:** Very compact binary formats; fast serialization.
- **Cons:** MCP tool results are text-based; binary formats would require base64 encoding, negating size benefits; not human-readable for debugging; adds complexity.

### Alternatives considered for large-response handling (file-based output)

- **Pagination (offset/limit parameters):** Would require callers to issue multiple round-trips and reassemble results. Adds protocol complexity and makes each individual response less self-contained.
- **Server-side truncation with warning:** Simpler, but the caller loses data with no way to recover it. Unacceptable for faithful document conversion.
- **Chunked streaming responses:** MCP tool results are single-shot text payloads; the protocol has no built-in streaming for tool results.

File-based output was chosen because it keeps each tool call self-contained (one call = one complete result), requires no protocol extensions, and lets the caller decide when to read the file.

## Consequences

- System.Text.Json is included in .NET 9 — no additional NuGet dependency.
- Using source generators (`[JsonSerializable]`) enables AOT-compatible, allocation-efficient serialization if needed in the future.
- Server-side classification (e.g., grouping raw PDF operations into rectangles/lines rather than returning raw ops) is done before serialization, keeping payloads focused on what the agent needs.
- Coordinate rounding from ~15 significant digits to 1 decimal place significantly reduces JSON string length for dense pages with thousands of positioned elements.
- The `camelCase` convention matches typical JSON conventions and is immediately familiar to LLMs trained on web data.
- Omitting nulls and using compact color representation (`"#FF0000"` vs `{"r":255,"g":0,"b":0}`) reduces typical per-element overhead.
- File-based output (`outputFile`) decouples response size from MCP payload limits. Dense pages that exceed ≤ 30 KB inline can write to disk while keeping the inline summary under 1 KB.
- File output uses CSV format, reducing token consumption by ~50% compared to JSON for uniform tabular data (text elements), because repeated key names are replaced by a single header row. The trade-off is that optional/nullable fields appear as empty columns rather than being omitted entirely.
