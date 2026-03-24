# FRD-002: Document Metadata Retrieval (GetPdfInfo)

## Traces To

- **PRD:** REQ-1 (Document metadata retrieval)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Summary

Provide a tool that returns document-level metadata from a PDF file. This is the first tool an agent calls to understand the document's structure and plan page-by-page traversal. It is also the simplest tool, making it ideal for validating the end-to-end MCP tool invocation pipeline.

## Inputs

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `pdfPath` | string | Yes | Absolute path to the PDF file on the local filesystem |

## Outputs

A JSON object containing:

| Field | Type | Description |
|-------|------|-------------|
| `pageCount` | int | Total number of pages in the document |
| `pages` | array | Per-page info: page number, width, height (in PDF points) |
| `title` | string? | Document title from metadata (null if absent) |
| `author` | string? | Document author from metadata (null if absent) |
| `subject` | string? | Document subject from metadata (null if absent) |
| `keywords` | string? | Document keywords from metadata (null if absent) |
| `creator` | string? | Creating application from metadata (null if absent) |
| `producer` | string? | PDF producer from metadata (null if absent) |
| `bookmarks` | array? | Hierarchical bookmarks/outline tree (null if none). Each entry: title, page number, children |

## Functional Requirements

1. The tool must open the PDF using PdfPig, extract metadata, and close/dispose the document within the single tool call.
2. Page dimensions must be reported in PDF points (1 point = 1/72 inch).
3. The bookmarks tree must be returned as a nested structure reflecting the document outline hierarchy.
4. If the document has no bookmarks, the `bookmarks` field must be null (omitted from JSON).
5. All metadata string fields must be null (omitted) when not present in the document, not empty strings.
6. Coordinates and dimensions must be rounded to 1 decimal place.

## Dependencies

- Feature 001 (MCP Server Host) must be complete.
- `PdfPig` NuGet package (meta-package bundling all `UglyToad.PdfPig.*` assemblies).

> **Note:** As the first tool implementation, Feature 002 will implicitly establish shared infrastructure components that all subsequent tools depend on: the centralized JSON serialization configuration (ADR-0005), the coordinate rounding and color formatting utilities, and the input validation service (FRD-007). These shared components are created alongside this feature and reused by Features 003–006.

## Acceptance Criteria

- [ ] Calling `GetPdfInfo` with a valid PDF path returns page count, page dimensions, and available metadata.
- [ ] Calling `GetPdfInfo` on a PDF with bookmarks returns the full outline tree with titles and page numbers.
- [ ] Calling `GetPdfInfo` on a PDF without bookmarks omits the `bookmarks` field from the response.
- [ ] Metadata fields not present in the PDF are omitted from the JSON response (not empty strings).
- [ ] The response is valid JSON using camelCase property names.
- [ ] The tool properly disposes the PdfDocument after use.
