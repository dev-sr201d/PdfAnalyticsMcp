# Task 001: Solution & Project Scaffolding

## Description

Create the .NET 9 solution structure, main server project, NuGet package references, and directory layout that all subsequent features will build upon. This task establishes the foundational project structure defined in AGENTS.md.

## Traces To

- **FRD:** FRD-001 (MCP Server Host & Stdio Transport)
- **PRD:** REQ-9 (Local stdio transport)
- **ADRs:** ADR-0001 (Language/Runtime), ADR-0003 (MCP SDK)

## Dependencies

- None — this is the first task.

## Technical Requirements

### Solution Structure

- Create `PdfAnalyticsMcp.sln` at the repository root.
- Create the main server project at `src/PdfAnalyticsMcp/PdfAnalyticsMcp.csproj`.
- The project must target `net9.0` with the following properties enabled:
  - Nullable reference types (`enable`)
  - Implicit usings (`enable`)
  - Latest C# language version
  - Output type: `Exe` (console application)

### Repository-Level Files

- Create a `.gitignore` appropriate for .NET projects (excluding `bin/`, `obj/`, `*.user`, etc.).
- Create a `global.json` pinning the .NET 9 SDK version to ensure consistent builds across environments.

### Directory Layout

Under `src/PdfAnalyticsMcp/`, create the following subdirectories with `.gitkeep` placeholder files to preserve them in source control:

- `Tools/` — will contain MCP tool classes (one per file)
- `Models/` — will contain DTO record types for serialization
- `Services/` — will contain PDF extraction business logic

### NuGet Package References

The project must reference the following packages at their latest stable versions:

- `ModelContextProtocol` — official C# MCP SDK
- `Microsoft.Extensions.Hosting` — application lifecycle and DI

> **Note:** `UglyToad.PdfPig` and `Docnet.Core` are deferred to the tasks that first use them (features 002–006). Adding unused package references creates unnecessary build dependencies.

### Minimal Program.cs

Include a minimal `Program.cs` that compiles and runs (can be an empty host builder with `await builder.Build().RunAsync()`). The full MCP server configuration is covered in Task 002.

## Acceptance Criteria

- [ ] `PdfAnalyticsMcp.sln` exists at the repository root and includes the `src/PdfAnalyticsMcp` project.
- [ ] The project targets `net9.0` with nullable reference types enabled, implicit usings enabled, and latest C# language version.
- [ ] The project file references `ModelContextProtocol` and `Microsoft.Extensions.Hosting` NuGet packages.
- [ ] A `.gitignore` for .NET projects exists at the repository root.
- [ ] A `global.json` pinning the .NET 9 SDK version exists at the repository root.
- [ ] The directory structure `src/PdfAnalyticsMcp/Tools/`, `Models/`, `Services/` exists (with `.gitkeep` files).
- [ ] `dotnet build` succeeds with zero errors and zero warnings.
- [ ] `dotnet run --project src/PdfAnalyticsMcp` starts without crash (may exit immediately if no stdin input — that is expected).
