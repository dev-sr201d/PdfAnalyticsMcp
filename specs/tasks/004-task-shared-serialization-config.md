# Task 004: Shared Serialization Configuration

## Description

Establish the shared JSON serialization infrastructure that all MCP tools will use when returning structured responses. This includes a centralized serializer options instance configured per ADR-0005, and a coordinate rounding utility to keep response payloads compact. This is shared infrastructure — it must be in place before any tool returns serialized JSON.

## Traces To

- **FRD:** FRD-002 (GetPdfInfo), and all subsequent tool FRDs
- **PRD:** REQ-6 (Data volume management)
- **ADRs:** ADR-0005 (Serialization and Response Format)

## Dependencies

- **Task 001** (Solution & Project Scaffolding) must be completed first.

## Technical Requirements

### Centralized Serializer Options

- Provide a single, reusable `JsonSerializerOptions` instance as a `static readonly` field on a static class (e.g., `SerializerConfig.Options`), following the pattern shown in AGENTS.md Section 9. This does not require DI registration — static access is appropriate for thread-safe, immutable configuration.
- The options must enforce:
  - **camelCase** property naming
  - **Omit null** properties from output (reduces payload size)
  - **No indentation** (compact output for agent consumption)
- This instance must be reused across all tool serialization calls — creating new options per call forces recomputation of internal reflection caches and degrades performance.

### Coordinate Rounding Utility

- Provide a utility method that rounds `double` coordinate values to 1 decimal place.
- PDF coordinate precision beyond 0.1 points (≈ 0.0014 inches) is unnecessary for layout analysis and inflates JSON payloads significantly on dense pages.
- All tools returning positional data (text, graphics, images) must use this utility consistently.

### Color Formatting Utility

> **Note:** Color data is not needed for GetPdfInfo (Feature 002) but is included here as forward-looking shared infrastructure for Features 003–006 which all return color metadata.

- Provide a utility method that formats RGB color values as hex strings in `"#RRGGBB"` format (e.g., `"#FF0000"` for red).
- Accept individual R, G, B byte values and return the formatted string.
- All tools returning color data must use this utility consistently.

### File Placement

- Place the serialization configuration and formatting utilities in `Services/` — AGENTS.md designates this folder for business logic and shared infrastructure. These are not tools and not DTOs.

## Acceptance Criteria

- [ ] A shared serializer options instance exists as a `static readonly` field on a static class and is accessible to all tool classes without DI.
- [ ] The serializer options use camelCase naming, omit nulls, and produce unindented output.
- [ ] A coordinate rounding utility rounds `double` values to 1 decimal place (e.g., `123.456789` → `123.5`).
- [ ] A color formatting utility converts RGB values to `"#RRGGBB"` hex strings.
- [ ] Unit tests verify the serializer options produce expected JSON (camelCase, nulls omitted, compact).
- [ ] Unit tests verify coordinate rounding for edge cases (exact values, halfway rounding, negative coordinates, zero).
- [ ] Unit tests verify color formatting for boundary values (0, 255) and typical values.
- [ ] The serializer options instance is a `static readonly` field — not recreated per call.
