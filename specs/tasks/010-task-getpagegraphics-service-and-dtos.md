# Task 010: GetPageGraphics Service and DTOs

## Description

Create the data transfer objects and extraction service for the `GetPageGraphics` tool (FRD-004). The service uses PdfPig's **`page.Paths`** API (`IReadOnlyList<PdfPath>`), which returns pre-processed path objects with:
- **CTM transforms pre-applied** — all coordinates are in page space
- **Fill/stroke colors** attached as `IColor` objects (convertible via `ToRGBValues()`)
- **Line widths and dash patterns** attached to each path
- **Form XObject recursion** handled automatically — graphics inside Form XObjects are included

The service classifies each path into rectangles, lines, and complex shapes, and formats the output as compact JSON. Raw PDF operations are never returned — the server produces classified, page-space shapes that an AI agent can directly use to identify table gridlines, sidebar backgrounds, callout box borders, and section dividers.

## Traces To

- **FRD:** FRD-004 (Page Graphics Extraction — GetPageGraphics)
- **PRD:** REQ-3 (Graphics extraction and classification), REQ-6 (Data volume management), REQ-7 (Page-by-page processing)
- **ADRs:** ADR-0002 (PdfPig), ADR-0005 (Serialization)

## Dependencies

- **Task 001** — Solution and project scaffolding (complete)
- **Task 004** — Shared serialization configuration: `SerializerConfig`, `FormatUtils` (complete)
- **Task 005** — Input validation service: `IInputValidationService` (complete)

## Technical Requirements

### DTOs

Define response DTOs in the `Models/` directory as immutable `record` types. All coordinates are in PDF points, rounded to 1 decimal place. Colors use `"#RRGGBB"` hex format. Nullable fields are omitted from JSON when null (per `SerializerConfig`).

1. **Rectangle DTO** — Represents a classified rectangle shape:
   - `x` (double) — Left edge X coordinate
   - `y` (double) — Bottom edge Y coordinate
   - `w` (double) — Width
   - `h` (double) — Height
   - `fillColor` (string?, nullable) — Fill color as `"#RRGGBB"` (null if not filled)
   - `strokeColor` (string?, nullable) — Stroke color as `"#RRGGBB"` (null if not stroked)
   - `strokeWidth` (double?, nullable) — Stroke line width (null if not stroked)

2. **Line DTO** — Represents a classified line segment:
   - `x1` (double) — Start X coordinate
   - `y1` (double) — Start Y coordinate
   - `x2` (double) — End X coordinate
   - `y2` (double) — End Y coordinate
   - `strokeColor` (string?, nullable) — Stroke color as `"#RRGGBB"`
   - `strokeWidth` (double?, nullable) — Stroke line width
   - `dashPattern` (string?, nullable) — Dash pattern description (null if solid line)

3. **Path DTO** — Represents a complex path that is not a rectangle or line:
   - `x` (double) — Bounding box left edge X coordinate
   - `y` (double) — Bounding box bottom edge Y coordinate
   - `w` (double) — Bounding box width
   - `h` (double) — Bounding box height
   - `fillColor` (string?, nullable) — Fill color as `"#RRGGBB"` (null if not filled)
   - `strokeColor` (string?, nullable) — Stroke color as `"#RRGGBB"` (null if not stroked)
   - `vertexCount` (int) — Number of vertices in the path

4. **Page graphics response DTO** — Envelope for the full tool response:
   - `page` (int) — The 1-based page number returned
   - `width` (double) — Page width in PDF points, rounded to 1 decimal place
   - `height` (double) — Page height in PDF points, rounded to 1 decimal place
   - `rectangles` (list of rectangle DTOs) — All classified rectangles on the page
   - `lines` (list of line DTOs) — All classified line segments on the page
   - `paths` (list of path DTOs) — All complex paths on the page

### Service Interface

Define a service interface `IPageGraphicsService` in `Services/` with a method that:
- Accepts a file path (string) and a 1-based page number (int)
- Returns the page graphics response DTO
- Uses `IInputValidationService` for page number validation (file path validation is the tool layer's responsibility, matching the established pattern in existing tools)

### Service Implementation — `PageGraphicsService`

The service must:

1. **Open the PDF** using PdfPig's `PdfDocument.Open()` with a `using` declaration. If the file cannot be opened as a PDF, catch the exception and throw `ArgumentException` with a message indicating the file could not be opened as a PDF (matching the pattern in `PdfInfoService` and `PageTextService`).
2. **Validate the page number** against the document's page count via `IInputValidationService.ValidatePageNumber()`.
3. **Access only the requested page** using `document.GetPage(n)` — never iterate all pages.
4. **Read `page.Paths`** to get the pre-processed list of `PdfPath` objects. This is PdfPig's public API (`IReadOnlyList<PdfPath>`) that returns all paths on the page, including those inside Form XObjects, with CTM transforms already applied.
5. **Filter out clipping paths** — Skip any `PdfPath` where `IsClipping == true`. Clipping paths define clipping regions and are not visible graphics.
6. **Skip unfilled and unstroked paths** — Skip any `PdfPath` where both `IsFilled == false` and `IsStroked == false`. These are invisible paths (e.g., used only for internal state) and should not be included in the output.
7. **Classify each remaining `PdfPath`** as a rectangle, line, or complex path based on its subpath commands (see Path Classification below).
8. **Extract colors** from each `PdfPath` using the `FillColor` and `StrokeColor` properties (of type `IColor`). Convert to `"#RRGGBB"` hex strings (see Color Extraction below).
9. **Extract line properties** from each `PdfPath`: `LineWidth` and `LineDashPattern`.
10. **Return the response DTO** with page dimensions and the three classified shape lists.
11. **Dispose PdfDocument** properly — open per call, not cached.

### PdfPig `PdfPath` API Reference

The service consumes PdfPig's public `PdfPath` type. Key properties and methods:

| Property / Method | Type | Description |
|---|---|---|
| `IsFilled` | `bool` | Whether the path is filled |
| `IsStroked` | `bool` | Whether the path is stroked |
| `IsClipping` | `bool` | Whether the path is a clipping path (filter these out) |
| `FillColor` | `IColor?` | Fill color (null if not filled) |
| `StrokeColor` | `IColor?` | Stroke color (null if not stroked) |
| `LineWidth` | `double` | Stroke line width |
| `LineDashPattern` | `LineDashPattern` | Dash pattern (array + phase) |
| `GetBoundingRectangle()` | `PdfRectangle?` | Axis-aligned bounding box |
| (indexer / enumerator) | `PdfSubpath` | The path is a list of `PdfSubpath` objects |

Each `PdfSubpath` contains a `Commands` list of `IPathCommand` objects:

| Command Type | Properties | Description |
|---|---|---|
| `Move` | `Location: PdfPoint` | Move to a new point |
| `Line` | `From: PdfPoint`, `To: PdfPoint` | Straight line segment |
| `CubicBezierCurve` | `StartPoint`, `FirstControlPoint`, `SecondControlPoint`, `EndPoint` (all `PdfPoint`) | Cubic Bézier curve |
| `QuadraticBezierCurve` | `ControlPoint`, `EndPoint` (both `PdfPoint`) | Quadratic Bézier curve |
| `Close` | (none) | Close the subpath |

`PdfPoint` has `X` and `Y` properties (both `double`), already in page-space coordinates (CTM transforms are pre-applied by PdfPig's internal `ContentStreamProcessor`).

### Path Classification

Classify each `PdfPath` based on its subpath commands:

1. **Rectangle** — A path is classified as a rectangle if it has exactly **one subpath** and that subpath matches either:
   - **4 Lines + Close**: Exactly 5 commands — 1 `Move`, 3 `Line`, 1 `Close` — where all 4 edges are strictly horizontal or vertical (axis-aligned). Check by comparing coordinates: for each consecutive pair of points, either the X values match (vertical edge) or the Y values match (horizontal edge). Tolerance of 0.01 for floating-point comparison.
   - **4 Lines (no Close)**: Exactly 5 commands — 1 `Move` + 4 `Line` — where the last `Line.To` coincides with the `Move.Location` (within tolerance 0.01), effectively closing the shape, and all edges are axis-aligned.
   - **Note:** PdfPig's `DrawRectangle` generates `re` operations which appear as 1 `Move` + 3 `Line` + 1 `Close` in `page.Paths`. The rectangle shorthand `re` is expanded by PdfPig's content stream processor into these commands.
   - A path containing any `CubicBezierCurve` or `QuadraticBezierCurve` commands **cannot** be a rectangle.

2. **Line** — A path is classified as a line if it has exactly **one subpath** with exactly **2 commands**: 1 `Move` followed by 1 `Line`. No `Close`, no curves.

3. **Complex path** — Any path that does not match the rectangle or line criteria. Return with the bounding box from `GetBoundingRectangle()` and a vertex count. The vertex count should be the total number of significant points across all commands: count each `Move.Location`, each `Line.To`, each curve endpoint (excluding control points for a simpler count), plus 1 for each `Close`.

### Color Extraction

Extract colors from `PdfPath.FillColor` and `PdfPath.StrokeColor` using the `IColor` interface:

1. **Convert to RGB** — Call `color.ToRGBValues()` which returns `(double r, double g, double b)` with values in the 0.0–1.0 range. This method handles all color spaces (RGB, Grayscale, CMYK) automatically.
2. **Convert to bytes** — `byte R = (byte)Math.Round(r * 255)`, etc.
3. **Format as hex** — Use `FormatUtils.FormatColor(R, G, B)` to produce `"#RRGGBB"`.
4. **Handle PatternColor** — `PatternColor` types (`TilingPatternColor`, `ShadingPatternColor`) throw `InvalidOperationException` from `ToRGBValues()`. Catch this and treat the color as null (omit from output). Pattern colors are decorative fills (e.g., hatching, gradients) that cannot be represented as a single hex color.

For painting mode:
- If `IsFilled`: set `fillColor` in the DTO. Use `FillColor.ToRGBValues()` if `FillColor` is not null; otherwise default to `"#000000"` (the PDF spec's default graphics state fill color is black, but PdfPig returns `null` when no explicit color-setting operation precedes the path).
- If `IsStroked`: set `strokeColor` and `strokeWidth` in the DTO. Use `StrokeColor.ToRGBValues()` if `StrokeColor` is not null; otherwise default to `"#000000"` (same reason).
- If not stroked: leave `strokeColor` and `strokeWidth` null

### Dash Pattern Formatting

Extract from `PdfPath.LineDashPattern`:
- If the dash pattern's array is empty (or the `LineDashPattern` is the default/solid), set `dashPattern` to null in the DTO.
- Otherwise, format as a human-readable string: `"[{array values space-separated}] {phase}"` — e.g., `"[3 2] 0"` for a 3-on-2-off pattern starting at phase 0.
- Access the `Array` and `Phase` properties of `LineDashPattern`.

### Coordinate Rounding

All coordinate values in the output DTOs must be rounded to 1 decimal place using `FormatUtils.RoundCoordinate()`. This applies to:
- Rectangle: x, y, w, h
- Line: x1, y1, x2, y2
- Path: x, y, w, h (bounding box)
- strokeWidth values

### Form XObject Handling

PdfPig's `page.Paths` API uses the internal `ContentStreamProcessor` which automatically:
- Detects `InvokeNamedXObject` (`Do`) operations in the content stream
- Resolves the named XObject from the page's resources dictionary
- Checks the XObject's `/Subtype` — only processes Form XObjects
- Saves graphics state, applies the Form's `/Matrix` transformation, processes the Form's content stream recursively, then restores graphics state
- Handles nested Form XObjects and circular reference detection internally

### Registration

Register the service in `Program.cs` dependency injection as a singleton, following the existing pattern used by `IPdfInfoService` and `IPageTextService`.

### Test Data

Create test PDF files using **programmatic generation** via `PdfDocumentBuilder`'s high-level API in the existing `TestPdfGenerator` class.

**PdfDocumentBuilder high-level API** supports: `DrawRectangle`, `DrawLine`, `SetStrokeColor`, `SetTextAndFillColor`, `DrawCircle`, `DrawEllipsis`, `DrawTriangle`. These methods internally emit content stream operations (`re`, `m`, `l`, `c`, `S`, `f`, etc.), which PdfPig's `ContentStreamProcessor` processes when reading the PDF back via `page.Paths`.

**What each method produces in `page.Paths`:**
- `DrawRectangle` → Rectangle path (1 `Move` + 3 `Line` + 1 `Close` from the internal `re` operation)
- `DrawLine` → Line path (1 `Move` + 1 `Line`)
- `DrawCircle` / `DrawEllipsis` → Complex path with `CubicBezierCurve` commands
- `DrawTriangle` → Complex path (3 `Line` segments — not a rectangle)

**Note on rectangle-from-line-segments:** `PdfDocumentBuilder`'s `DrawRectangle` uses the `re` operator internally, so it always produces the Move+3Line+Close pattern. Testing rectangle classification from 4 explicit `Line` commands (Move+4Line with coincident endpoints) is not achievable via `PdfDocumentBuilder` alone. For this edge case, either include a small static test PDF (hand-crafted with raw content stream) or cover it via unit testing the classification logic directly on mock `PdfPath` / `PdfSubpath` data.

The test data must include PDFs with:
- A page with simple rectangles (both filled and stroked) with known positions and colors (via `DrawRectangle`)
- A page with straight lines with known start/end points and stroke colors (via `DrawLine`)
- A page with overlapping graphics demonstrating state isolation (e.g., different colored shapes at different positions)
- A page with at least one complex path containing curves (via `DrawCircle` or `DrawEllipsis`)
- A page with no drawn graphics (text-only or blank) for the empty-graphics test (can reuse an existing test PDF from prior tasks, such as `sample-no-metadata.pdf`)

**Form XObject test data is not needed** because Form XObject recursion is handled internally by PdfPig's `page.Paths` API. The service does not implement custom Form XObject handling and therefore does not need to test it directly. The empirical verification (328 paths from a real-world PDF using `page.Paths`) confirms this works.

Place generated PDFs in `tests/TestData/`.

## Acceptance Criteria

- [ ] Rectangle, Line, Path, and PageGraphics DTOs are defined as immutable records with all specified fields, using nullable types for optional fields.
- [ ] Service interface `IPageGraphicsService` is defined with a method accepting file path and page number.
- [ ] Service implementation uses `page.Paths` to read pre-processed paths from PdfPig.
- [ ] Clipping paths (`IsClipping == true`) are filtered out.
- [ ] Invisible paths (neither filled nor stroked) are filtered out.
- [ ] Paths with 1 subpath containing 1 `Move` + 3 `Line` + 1 `Close` forming an axis-aligned rectangle are classified as rectangles.
- [ ] Paths with 1 subpath containing 1 `Move` + 4 `Line` (last point coinciding with first) forming an axis-aligned rectangle are classified as rectangles.
- [ ] Paths containing any `CubicBezierCurve` or `QuadraticBezierCurve` commands are never classified as rectangles.
- [ ] Paths with 1 subpath containing exactly 1 `Move` + 1 `Line` are classified as lines.
- [ ] All other paths are classified as complex paths with a bounding box and vertex count.
- [ ] All coordinates are rounded to 1 decimal place via `FormatUtils.RoundCoordinate()`.
- [ ] Colors are extracted via `IColor.ToRGBValues()` and formatted as `"#RRGGBB"` hex strings via `FormatUtils.FormatColor()`.
- [ ] `PatternColor` types are handled gracefully (caught `InvalidOperationException`, color treated as null).
- [ ] Stroked paths have `strokeColor` and `strokeWidth` set; filled paths have `fillColor` set; fill+stroke paths have both.
- [ ] Dash patterns are formatted as human-readable strings (e.g., `"[3 2] 0"`) or null for solid lines.
- [ ] PdfDocument is opened with `using` and disposed after each call.
- [ ] Only the requested page is accessed via `document.GetPage(n)`.
- [ ] Invalid PDF files throw `ArgumentException`.
- [ ] Out-of-range page numbers throw `ArgumentException` (via `IInputValidationService`).
- [ ] Service is registered as a singleton in `Program.cs`.
- [ ] Test data PDF(s) exist in `tests/TestData/` with known graphic elements.

## Testing Requirements

Unit tests must cover:

### Path Classification Tests (can use generated PDFs or mock `PdfPath` data)

1. **Rectangle from `re` operation** — Given a PDF with `DrawRectangle`, verify the service returns a classified rectangle with correct position, dimensions, and colors. The `re` operator produces 1 `Move` + 3 `Line` + 1 `Close` in `page.Paths`.
2. **Rectangle from 4 explicit lines** — Given a path with 1 `Move` + 4 `Line` where the last point coincides with the first and all edges are axis-aligned, verify classification as a rectangle. (Test via mock `PdfPath` data or a static test PDF.)
3. **Non-axis-aligned quadrilateral** — Given a path with 4 line segments where edges are not strictly horizontal/vertical, verify it is classified as a complex path, not a rectangle.
4. **Line extraction** — Given a PDF with `DrawLine`, verify the service returns a classified line with correct start/end points and stroke properties.
5. **Complex path (triangle)** — Given a PDF with `DrawTriangle`, verify it is classified as a complex path with correct bounding box and vertex count.
6. **Complex path (circle/curves)** — Given a PDF with `DrawCircle` or `DrawEllipsis`, verify the path is classified as complex (has Bézier curves) with correct bounding box.
7. **Any path with curves is not a rectangle** — Verify that a path containing `CubicBezierCurve` commands is never classified as a rectangle even if it has 4 sides.

### Color and Style Tests

8. **Fill-only path** — Verify a filled rectangle has `fillColor` set and `strokeColor`/`strokeWidth` null.
9. **Stroke-only path** — Verify a stroked rectangle has `strokeColor` and `strokeWidth` set and `fillColor` null.
10. **Fill+stroke path** — Verify a path with both `IsFilled` and `IsStroked` has both fill and stroke properties populated.
11. **RGB color conversion** — Verify that an `RGBColor` from PdfPig is correctly converted to `"#RRGGBB"` via `ToRGBValues()` → `FormatUtils.FormatColor()`.
12. **Grayscale color conversion** — Verify that a `GrayColor` is correctly converted (e.g., 0.5 gray → `"#808080"`).
13. **PatternColor handling** — Verify that `PatternColor` (which throws `InvalidOperationException` from `ToRGBValues()`) is handled gracefully and treated as null.

### Filtering Tests

14. **Clipping paths filtered** — Verify that paths with `IsClipping == true` are excluded from the output.
15. **Invisible paths filtered** — Verify that paths where `IsFilled == false && IsStroked == false` are excluded.
16. **Empty page** — Verify that a page with no drawn graphics returns empty rectangle, line, and path lists.

### Service-Level Tests

17. **Page dimensions** — Verify the response includes correct page width and height.
18. **Coordinate rounding** — Verify all positional values are rounded to 1 decimal place.
19. **Page number validation** — Verify that out-of-range page numbers throw `ArgumentException`.
20. **Invalid PDF** — Verify that a non-PDF file throws `ArgumentException`.
21. **Serialization** — Verify DTOs serialize to expected JSON structure: camelCase properties, null fields omitted, compact format.
22. **Dash pattern formatting** — Verify that a stroked line with a dash pattern includes a human-readable `dashPattern` string, and that solid lines have `dashPattern` null.
