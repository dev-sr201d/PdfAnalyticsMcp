# ADR-0001: Language and Runtime Platform

## Status

Accepted

## Context

PdfAnalyticsMcp is an MCP server that exposes PDF inspection tools for AI agents over stdio transport (REQ-9). We need to choose a programming language and runtime that supports:

- Building a local stdio-based MCP server (REQ-9)
- Reading and parsing PDF files with rich content extraction (REQ-1 through REQ-4)
- Maintaining manageable response sizes via server-side processing (REQ-6)
- Cross-platform operation (Windows, Linux, macOS)

The concept document already assumes C# with PdfPig for PDF extraction, and the project stakeholder has specified latest C# as the technology base.

## Decision

Use **C# on .NET 9** (latest LTS-adjacent release as of early 2026) as the language and runtime platform.

The project will target `net9.0` and use C# 13 language features.

## Alternatives Considered

### Python with pypdf / pdfminer

- **Pros:** Large ecosystem of PDF libraries; fast prototyping; many MCP SDK options available.
- **Cons:** Slower execution for heavy PDF processing; weaker typing makes complex data extraction error-prone; GIL limits concurrency; no existing codebase or team expertise assumed.

### TypeScript/Node.js with pdf.js

- **Pros:** Good MCP SDK support; pdf.js is mature and well-maintained.
- **Cons:** pdf.js is primarily a renderer, not a structural extraction tool; weak support for graphics path extraction; less suitable for byte-level PDF operations.

### Rust

- **Pros:** Excellent performance; strong typing; memory safety.
- **Cons:** Steeper learning curve; smaller PDF library ecosystem; less mature MCP SDK support; slower development velocity.

## Consequences

- The official C# MCP SDK (`ModelContextProtocol` NuGet package) is available and supports stdio transport with `Microsoft.Extensions.Hosting`.
- PdfPig, the primary PDF parsing library identified in the concept, is a native C# library with full .NET support.
- The project uses `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.DependencyInjection` for application lifecycle and DI, which is idiomatic for .NET 9.
- Developers working on this project need C# / .NET experience.
- Native dependencies (e.g., for PDF rendering via Docnet) have .NET Standard 2.0+ support and ship platform-specific binaries via NuGet.
