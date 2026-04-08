# Task 014: Shared PDFiumCore Service

## Description

Create a shared service that encapsulates all cross-cutting concerns for PDFiumCore interactions: library lifecycle management (`FPDF_InitLibrary` / `FPDF_DestroyLibrary`), thread serialization via a static semaphore, document and page loading with consistent error handling, and native resource cleanup. Both the page rendering service (Task 015 / FRD-005) and the image extraction service (Task 017 / FRD-006) consume this shared service rather than interacting with PDFiumCore directly.

PDFium's native library is **not thread-safe** — concurrent calls from multiple threads cause `AccessViolationException` and memory corruption. This service serializes all PDFiumCore operations through a single `SemaphoreSlim(1, 1)`, accepting `CancellationToken` so that callers queued behind the semaphore can be cancelled by the MCP client.

## Traces To

- **FRD:** FRD-005 (Page Rendering), Functional Requirements 5, 7, 9; FRD-006 (Page Image Extraction), Functional Requirements 13, 18
- **PRD:** REQ-5 (Page rendering), REQ-4 (Image extraction), REQ-7 (Robust error handling), REQ-9 (Concurrent tool safety)
- **ADRs:** ADR-0004 (PDFiumCore — Shared PDFiumCore Service, Thread Safety, Resource Management sections)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)

No dependency on Tasks 012, 013, 015, or 017. This is a foundational service that those tasks consume.

## Technical Requirements

### NuGet Dependency

Add `PDFiumCore` to the main project's `.csproj`:
```xml
<PackageReference Include="PDFiumCore" Version="*-*" />
```

This introduces native PDFium binaries bundled per platform (Windows x64/x86, Linux x64, macOS x64) via the NuGet package. No manual configuration is required.

### Library Lifecycle — Hosted Service

Implement a hosted service (e.g., `PdfiumLifecycleService`) that:
- Calls `fpdfview.FPDF_InitLibrary()` once during application startup (`StartAsync`)
- Calls `fpdfview.FPDF_DestroyLibrary()` once during application shutdown (`StopAsync`)
- Is registered as a hosted service via `builder.Services.AddHostedService<PdfiumLifecycleService>()` in `Program.cs`

This ensures the PDFium native library is initialized before any tool calls arrive and cleaned up on process exit.

### Service Interface

Define a service interface `IPdfiumService` in `Services/` with methods that provide serialized access to PDFium operations. The service accepts high-level parameters and handles all native interop, resource management, and error handling internally.

**`ExecuteAsync<T>`** — General-purpose method for serialized PDFium operations:
- Accepts a file path (`string`), a 1-based page number (`int`), a callback delegate that receives the loaded document handle and page handle and returns `T`, and a `CancellationToken`
- Acquires the semaphore, loads the document and page, invokes the callback, disposes native resources, and releases the semaphore
- Returns `Task<T>`
- Throws `ArgumentException` for validation and file access failures (see Error Handling below)
- Throws `OperationCanceledException` if the cancellation token is triggered while waiting for the semaphore


**`GetPageCount`** — Convenience method:
- Accepts a document handle (`FpdfDocumentT`)
- Returns the page count via `fpdfview.FPDF_GetPageCount(document)`
- This is called from within a callback, so it is already serialized

**`GetPageSize`** — Convenience method:
- Accepts a document handle and a 0-based page index
- Returns page width and height in PDF points via `fpdfview.FPDF_GetPageSizeByIndex()`
- This is called from within a callback, so it is already serialized

### Service Implementation — `PdfiumService`

The service must:

1. **Own the serialization semaphore** — `private static readonly SemaphoreSlim _semaphore = new(1, 1)`. The semaphore is `static` because the PDFium thread-safety constraint is process-wide (there is only one native PDFium instance).

2. **Acquire the semaphore with cancellation** — Call `await _semaphore.WaitAsync(cancellationToken)` before any PDFiumCore API calls. Release in a `finally` block to ensure cleanup even on exceptions.

3. **Load the document** — Call `fpdfview.FPDF_LoadDocument(pdfPath, null)`. The second parameter (password) is always `null` — password-protected PDFs are out of scope.

4. **Validate the document handle** — If `FPDF_LoadDocument` returns `null`, determine the error type:
   - Call `fpdfview.FPDF_GetLastError()` to get the error code
   - Error code `2` (`FPDF_ERR_FILE`) or `3` (`FPDF_ERR_FORMAT`): throw `ArgumentException` with `"The file could not be opened as a PDF."` (consistent with FRD-007)
   - Other error codes: throw `ArgumentException` with `"The file could not be opened as a PDF."`

5. **Validate the page number** — Use `IInputValidationService.ValidatePageNumber(page, pageCount)` where `pageCount = fpdfview.FPDF_GetPageCount(document)`. This reuses the same validation service as PdfPig-based tools, ensuring consistent error messages across both engines (FRD-007).

6. **Load the page** — Call `fpdfview.FPDF_LoadPage(document, pageNumber - 1)` (PDFium uses 0-based page indexing).

7. **Validate the page handle** — If `FPDF_LoadPage` returns `null`, throw `InvalidOperationException` with `"The page could not be loaded."`.

8. **Invoke the callback** — Pass the document and page handles to the caller's delegate. The caller performs feature-specific operations (rendering, image enumeration, etc.) using these handles.

9. **Clean up native resources** — In `finally` blocks, always:
   - Close the page: `fpdfview.FPDF_ClosePage(page)`
   - Close the document: `fpdfview.FPDF_CloseDocument(document)`
   - Release the semaphore: `_semaphore.Release()`

   Resource cleanup must occur in reverse order of acquisition (page → document → semaphore).

### File Access Error Handling

Before calling `FPDF_LoadDocument`, the service should detect file access errors early to produce the correct FRD-007 error messages:

- **File does not exist** — Detected by `IInputValidationService` at the tool layer (before the service is called). Not the service's responsibility.
- **File I/O errors** (locked, permission denied, sharing violation) — If `FPDF_LoadDocument` fails and `FPDF_GetLastError()` returns error code `2` (`FPDF_ERR_FILE`), throw `ArgumentException` with `"The file could not be accessed: {pdfPath}. It may be in use by another process."` This distinguishes file access errors from format errors per FRD-007.
- **Invalid PDF format** — If `FPDF_GetLastError()` returns error code `3` (`FPDF_ERR_FORMAT`) or `4` (`FPDF_ERR_PASSWORD`), throw `ArgumentException` with `"The file could not be opened as a PDF."`

### PDFiumCore Error Code Reference

| Code | Constant | Meaning |
|------|----------|---------|
| 0 | `FPDF_ERR_SUCCESS` | No error |
| 1 | `FPDF_ERR_UNKNOWN` | Unknown error |
| 2 | `FPDF_ERR_FILE` | File access error (not found, locked, permission denied) |
| 3 | `FPDF_ERR_FORMAT` | Not a valid PDF or corrupted |
| 4 | `FPDF_ERR_PASSWORD` | Password required (out of scope) |
| 5 | `FPDF_ERR_SECURITY` | Security scheme not supported |
| 6 | `FPDF_ERR_PAGE` | Page not found or content error |

### DI Registration

Register in `Program.cs`:
- `PdfiumLifecycleService` as a hosted service via `AddHostedService<PdfiumLifecycleService>()`
- `IPdfiumService` → `PdfiumService` as `AddSingleton` (the service holds no mutable state; the semaphore is static)

### Concurrency Note

The static semaphore ensures that only one thread at a time can use PDFium's native library, preventing crashes and memory corruption. The semaphore is `static` because the constraint is process-wide — there is only one PDFium native library instance in the process. Fast validations (file path checks, parameter range checks) should be performed by the tool layer before calling the service to avoid unnecessary queuing behind the semaphore.

## Acceptance Criteria

### Library Lifecycle
- [ ] `FPDF_InitLibrary()` is called once during application startup via a hosted service.
- [ ] `FPDF_DestroyLibrary()` is called once during application shutdown.
- [ ] The hosted service is registered in `Program.cs`.

### Semaphore Serialization
- [ ] All PDFiumCore operations are serialized through a static `SemaphoreSlim(1, 1)`.
- [ ] The semaphore is acquired before any `FPDF_*` call and released in a `finally` block.
- [ ] The `CancellationToken` is passed to `SemaphoreSlim.WaitAsync()`, allowing queued callers to be cancelled.
- [ ] A cancelled request waiting for the semaphore throws `OperationCanceledException` promptly.

### Document and Page Loading
- [ ] The service loads a valid PDF and provides document and page handles to the callback.
- [ ] PDFium 0-based page indexing is handled internally — callers pass 1-based page numbers.
- [ ] Page count is available to callbacks via `GetPageCount`.
- [ ] Page dimensions are available to callbacks via `GetPageSize`.

### Error Handling
- [ ] A file I/O error (locked/inaccessible file) produces `ArgumentException` with message: `"The file could not be accessed: {pdfPath}. It may be in use by another process."`
- [ ] An invalid PDF file produces `ArgumentException` with message: `"The file could not be opened as a PDF."`
- [ ] A page number less than 1 produces the standard validation error from `IInputValidationService`.
- [ ] A page number exceeding the document's page count produces the standard validation error with the valid range.
- [ ] A page that cannot be loaded produces `InvalidOperationException` with message: `"The page could not be loaded."`

### Resource Cleanup
- [ ] The page handle is closed via `FPDF_ClosePage()` after the callback completes, even on exceptions.
- [ ] The document handle is closed via `FPDF_CloseDocument()` after the callback completes, even on exceptions.
- [ ] The semaphore is released after native resources are cleaned up, even on exceptions.

### Service Registration
- [ ] `IPdfiumService` is registered as a singleton in `Program.cs`.
- [ ] `PdfiumLifecycleService` is registered as a hosted service in `Program.cs`.

## Testing Requirements

Unit tests must validate the service's lifecycle, serialization, error handling, and resource cleanup.

### Required Unit Test Scenarios

1. **Load valid PDF** — Call `ExecuteAsync` with a valid test PDF and a callback that returns the page count. Verify the callback is invoked and returns the correct page count.
2. **Page number translation** — Call `ExecuteAsync` with page 1. Verify the callback receives a valid page handle (not null). Repeat with page 2 on a multi-page PDF.
3. **Page size retrieval** — Call `ExecuteAsync` with a known US Letter PDF and use `GetPageSize` inside the callback. Verify width ≈ 612 and height ≈ 792 (PDF points).
4. **Invalid PDF file** — Call `ExecuteAsync` with `not-a-pdf.txt`. Verify `ArgumentException` is thrown with message containing `"could not be opened as a PDF"`.
5. **Page number zero** — Call `ExecuteAsync` with page 0. Verify the standard validation error is thrown.
6. **Page beyond count** — Call `ExecuteAsync` with a page number exceeding the document's page count. Verify the appropriate validation error is thrown.
7. **Cancellation while waiting** — Acquire the semaphore externally (to simulate a busy state), then call `ExecuteAsync` with a pre-cancelled `CancellationToken`. Verify `OperationCanceledException` is thrown promptly.
8. **Callback exception propagation** — Call `ExecuteAsync` with a callback that throws. Verify the exception propagates to the caller and native resources are still cleaned up (no deadlock on subsequent calls).
9. **Sequential calls succeed** — Call `ExecuteAsync` twice in sequence. Verify both calls succeed, confirming the semaphore is properly released after each call.

### Test Data

Reuse existing test PDFs from `tests/TestData/`:
- `sample-with-metadata.pdf` — Multi-page PDF with known dimensions
- `sample-text.pdf` — Single-page PDF with content
- `not-a-pdf.txt` — Invalid file for error handling tests

No new test data required.
