# Task 006: GetPdfInfo Service and DTOs

## Description

Implement the PDF metadata extraction service and its associated response DTOs. This service opens a PDF file using PdfPig, extracts document-level metadata (page count, page dimensions, title, author, subject, keywords, creator, producer), and the bookmarks/outline tree. It returns the data as structured DTOs ready for JSON serialization.

## Traces To

- **FRD:** FRD-002 (Document Metadata Retrieval — GetPdfInfo)
- **PRD:** REQ-1 (Document metadata retrieval)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Dependencies

- **Task 001** (Solution & Project Scaffolding) must be completed first.
- **Task 004** (Shared Serialization Configuration) must be completed first — DTOs rely on serialization conventions.

## Technical Requirements

### NuGet Package

- Add the `PdfPig` NuGet package to the main server project. This is a meta-package that bundles all `UglyToad.PdfPig.*` assemblies. This is the first task that uses PdfPig; all subsequent page-level tools will also depend on it.

### Response DTOs

Define immutable DTO types for the GetPdfInfo response. The DTOs must follow AGENTS.md conventions:

- Use `record` types for immutability and conciseness.
- Use nullable properties for optional fields so they serialize as omitted (not `null` literal) per ADR-0005.
- Keep DTOs flat where possible.

The response structure must include:

| Field | Type | Description |
|-------|------|-------------|
| `pageCount` | int | Total number of pages |
| `pages` | array | Per-page info: page number (1-based), width, height in PDF points |
| `title` | string? | Document title (null if absent) |
| `author` | string? | Document author (null if absent) |
| `subject` | string? | Document subject (null if absent) |
| `keywords` | string? | Document keywords (null if absent) |
| `creator` | string? | Creating application (null if absent) |
| `producer` | string? | PDF producer (null if absent) |
| `bookmarks` | array? | Hierarchical bookmark tree (null if none) |

Each page info entry:

| Field | Type | Description |
|-------|------|-------------|
| `number` | int | 1-based page number |
| `width` | double | Page width in PDF points, rounded to 1 decimal |
| `height` | double | Page height in PDF points, rounded to 1 decimal |

Each bookmark entry (recursive):

| Field | Type | Description |
|-------|------|-------------|
| `title` | string | Bookmark title |
| `pageNumber` | int? | Target page number (null if bookmark has no page destination) |
| `children` | array? | Child bookmarks (null if none) |

### Metadata Extraction Service

- The service accepts a file path (already validated by the input validation service for null/empty, traversal, and existence).
- It opens the PDF using PdfPig, extracts all required metadata, and returns the populated DTO.
- If `PdfDocument.Open()` throws (e.g., the file is not a valid PDF or is corrupted), the service must catch the exception and rethrow as `ArgumentException` with message: "The file could not be opened as a PDF." Internal exception details must not be exposed.
- The `PdfDocument` must be disposed after extraction (via `using` declaration).
- The document is opened per call — not cached across calls.
- Page dimensions must be rounded to 1 decimal place using the shared coordinate rounding utility from Task 004.
- Metadata string fields must be null when not present in the PDF — never empty strings. If PdfPig returns an empty string for a metadata field, treat it as null.
- The bookmarks tree must be extracted from PdfPig's document outline/bookmarks API and returned as a nested structure matching the document's hierarchy.
- If the document has no bookmarks, the `bookmarks` field must be null (omitted from JSON).

### DI Registration

- The extraction service must be registered in the DI container (singleton is appropriate since it is stateless — all state is per-call).

### Test Data

- Add three sample PDF files to `tests/TestData/`:
  1. `sample-with-metadata.pdf` — A PDF with metadata fields populated (title, author, subject, keywords, creator, producer) but no bookmarks. Two pages: one US Letter (612×792) and one A4.
  2. `sample-no-metadata.pdf` — A single-page PDF with minimal/no metadata and no bookmarks.
  3. `sample-with-bookmarks.pdf` — A two-page PDF with a hierarchical bookmark tree (e.g., "Chapter 1" with child "Section 1.1", and "Chapter 2" without children).
- These files should be small (1–3 pages). Prefer checking in small static PDF files over programmatic generation — static fixtures are simpler and more reliable for deterministic test assertions.

## Acceptance Criteria

- [ ] The `PdfPig` NuGet package is referenced in the server project.
- [ ] DTO record types exist for the GetPdfInfo response, page info, and bookmark entries.
- [ ] A metadata extraction service extracts page count, page dimensions, and all metadata fields from a PDF.
- [ ] Page dimensions are rounded to 1 decimal place.
- [ ] Metadata string fields are null (not empty strings) when absent from the PDF.
- [ ] Bookmarks are returned as a nested hierarchical structure matching the PDF outline.
- [ ] When a PDF has no bookmarks, the bookmarks field is null (omitted from serialized JSON).
- [ ] The extraction service is registered in the DI container.
- [ ] The extraction service catches PdfPig exceptions on file open and rethrows `ArgumentException` with "The file could not be opened as a PDF."
- [ ] Unit tests verify correct metadata extraction against a sample PDF with known metadata values.
- [ ] Unit tests verify a PDF without bookmarks produces a null bookmarks field.
- [ ] Unit tests verify page dimension rounding.
- [ ] Unit tests verify that empty metadata strings from PdfPig are converted to null.
- [ ] Three sample PDF test files exist in `tests/TestData/`: `sample-with-metadata.pdf`, `sample-no-metadata.pdf`, and `sample-with-bookmarks.pdf`.
