# 📝 Product Requirements Document (PRD)

## 1. Purpose

AI agents today lack the ability to fully understand complex PDF documents. Standard text extraction misses critical layout information — tables defined by drawn lines, sidebars indicated by colored background rectangles, headings distinguished by font size and color, and images that interrupt text flow. Without this structural understanding, agents produce poor-quality conversions that lose meaning.

**PdfAnalyticsMcp** is an MCP (Model Context Protocol) server that gives AI agents a complete toolkit for inspecting and understanding PDF documents at a deep structural level. By exposing document metadata, richly-attributed text, drawn graphics, embedded images, and optional visual page previews through discrete MCP tools, the server enables agents to accurately reconstruct document structure and produce faithful representations in other formats such as Markdown.

The primary users are **AI agents** (LLM-based systems) that consume MCP tools, and the **developers or end-users** who instruct those agents to convert, summarize, or analyze PDF content.

## 2. Scope

### In Scope

- An MCP server exposing a set of PDF inspection tools over the MCP protocol
- Document-level metadata retrieval (page count, dimensions, title, author, bookmarks/outline)
- Page-level text extraction with position, font, size, color, and configurable granularity
- Page-level graphics extraction — classification of drawn paths into rectangles, lines, and complex shapes with fill/stroke properties
- Page-level embedded image extraction with bounding boxes and optional file-based PNG extraction to disk
- Optional page rendering to PNG for visual layout analysis by multimodal AI models
- Page-by-page processing model to manage data volume
- Server-side classification and summarization of raw PDF data to keep responses within practical size limits
- The server operates as a local MCP server communicable over stdio, runnable directly by MCP-compatible clients without network infrastructure

### Out of Scope

- Direct PDF editing or modification
- PDF generation or creation
- OCR (optical character recognition) for scanned/image-only PDFs
- Form field extraction or interactive PDF element handling
- Digital signature verification
- PDF/A compliance checking
- Built-in Markdown conversion logic (the agent performs the conversion using the tools)
- A user-facing GUI or web interface

## 3. Goals & Success Criteria

### Goals

| # | Goal | Description |
|---|------|-------------|
| G1 | **Structural fidelity** | Enable AI agents to understand the full visual and structural layout of any PDF page, not just its raw text |
| G2 | **Complex layout support** | Support documents with multi-column layouts, tables (including borderless and partially-bordered), sidebars, callout boxes, and inline images |
| G3 | **Practical data volume** | Return richly-attributed data while keeping response sizes manageable for LLM context windows |
| G4 | **Agent autonomy** | Provide enough information in each tool response for the agent to plan its own traversal strategy and make layout decisions without human intervention |
| G5 | **Broad PDF compatibility** | Handle the wide variety of PDF construction methods found in real-world documents |

### Success Criteria

| Metric | Target |
|--------|--------|
| An AI agent using the tools can correctly identify tables, sidebars, multi-column layouts, and heading hierarchy on a set of representative complex PDFs | ≥ 85% structural accuracy |
| Average response size per tool call for a typical page (at `words` granularity) | ≤ 30 KB |
| Agent can produce a Markdown file from a 10-page complex PDF using only the MCP tools, with no manual intervention beyond the initial prompt | End-to-end completion |
| All five tool types return valid, parseable responses for any well-formed PDF without errors | 100% reliability on well-formed input |

## 4. High-Level Requirements

- [REQ-1] **Document metadata retrieval** — The server must provide a tool that returns document-level information including page count, individual page dimensions, document title, author, subject, keywords, creator, producer, and the bookmarks/outline tree, so the agent can plan its page-by-page traversal.

- [REQ-2] **Rich text extraction** — The server must provide a tool that returns text content from a specified page with full positional and stylistic metadata (x, y, width, height, font name, font size, bold/italic flags, and RGB fill color) for each text element. The tool must support configurable granularity (at minimum `words` and `letters` levels). For pages that exceed the inline size target, the tool must support an optional file-based output mode that writes the full result to a caller-specified path and returns a compact summary inline.

- [REQ-3] **Graphics extraction and classification** — The server must provide a tool that returns all drawn graphic elements on a specified page, classified into meaningful shapes: filled/stroked rectangles (with bounds, fill color, stroke color, stroke width), lines (start/end points, stroke color, width, dash pattern), and complex paths (bounding box and vertex count). Vertex count is provided instead of a full vertex list to manage payload size for complex shapes. This is essential for identifying table borders, sidebars, callout boxes, section dividers, and background fills.

- [REQ-4] **Image extraction** — The server must provide a tool that returns embedded images on a specified page, including each image's bounding box (position and size on the page), pixel dimensions, and bits per component. The tool must support an optional output directory parameter; when provided, the tool extracts each image as a PNG file to disk using a deterministic naming convention (based on the PDF filename, page number, and image index) and includes the file paths in the response. Image data is never returned inline — it is always written to disk when requested, keeping MCP responses small. When direct PNG conversion from the PDF image stream is not possible, the tool must use an alternative extraction method (rendering the page and cropping the image region) to maximize the number of images for which files can be provided. The extraction method must be transparent to the agent — the result is a PNG file regardless of how it was produced.

- [REQ-5] **Page rendering** — The server must provide a tool that renders a specified page as a PNG image at a configurable DPI, enabling multimodal AI models to visually inspect the page layout. This capability may rely on an external rendering dependency.

- [REQ-6] **Data volume management** — The server must default to the most practical granularity level (words, not letters) and classify graphics server-side (rather than returning raw operations). Response sizes must remain practical for LLM consumption. Server-side classification into rectangles, lines, and complex paths (with vertex counts rather than full vertex lists) provides sufficient data reduction for most pages. For dense pages that exceed the inline size target, tools may support an optional file-based output mode (see REQ-2) to offload the full payload to disk while returning a compact summary inline.

- [REQ-7] **Page-by-page processing** — All page-content tools must operate on a single specified page at a time. The server must never load or return an entire document's content in one call.

- [REQ-8] **Robust error handling** — The server must return clear, descriptive error messages when a PDF cannot be opened, a page number is out of range, or an extraction operation fails for a specific page.

- [REQ-9] **Local stdio transport** — The server must operate as a local MCP server using stdio as its transport. MCP-compatible clients must be able to launch the server as a child process and communicate with it over stdin/stdout without requiring network setup, HTTP endpoints, or additional infrastructure.
- [REQ-10] **Concurrent tool safety** — The server must remain correct and stable when the MCP client invokes multiple tools in parallel against the same PDF file. Concurrent tool calls must not cause crashes, data corruption, or transient failures due to resource contention.

## 5. User Stories

```gherkin
As an AI agent, I want to retrieve a PDF's page count, dimensions, and outline,
so that I can plan an efficient page-by-page traversal strategy.
```

```gherkin
As an AI agent, I want to get all text on a page with position, font, size, and color metadata,
so that I can determine reading order, identify headings, and distinguish body text from annotations.
```

```gherkin
As an AI agent, I want to get all drawn graphics on a page classified as rectangles, lines, and paths,
so that I can identify table gridlines, sidebar backgrounds, callout box borders, and section dividers.
```

```gherkin
As an AI agent, I want to retrieve embedded images with their bounding boxes,
so that I can understand text flow around images and include image references in my output.
```

```gherkin
As an AI agent, I want to optionally extract images to an output directory as PNG files,
so that I can reference or embed them when converting a PDF to another format.
```

```gherkin
As an AI agent, I want to render a page as a PNG preview,
so that I can visually verify my structural understanding of a complex layout.
```

```gherkin
As an AI agent, I want to choose between word-level and letter-level text granularity,
so that I can balance detail against response size depending on the page complexity.
```

```gherkin
As a developer, I want to point my MCP-compatible AI client at this server,
so that my agent gains PDF understanding capabilities without custom integration work.
```

```gherkin
As an end-user, I want to ask my AI agent to convert a complex PDF to Markdown,
so that I get a readable, editable document that preserves the original structure and meaning.
```

## 6. Assumptions & Constraints

### Assumptions

- The AI agent consuming the tools has sufficient context window size to process detailed per-page responses (typically 30 KB or less per call). For dense pages that exceed this target, file-based output provides an alternative that keeps inline responses small.
- Multimodal capability in the consuming AI model is optional; the text and graphics tools provide enough information for structural understanding without the page preview tool.
- Input PDFs are digitally-authored (not scanned images); OCR support is out of scope.
- The MCP client and server communicate over stdio transport; the server runs as a local child process of the client.
- PDF files are accessible from the local filesystem where the server runs.

### Constraints

- Page rendering (REQ-5) requires an external rendering dependency beyond the core PDF parsing library, which may introduce platform-specific native dependencies.
- Very dense pages (3,000+ words, hundreds of graphic elements) may produce large responses; the server must provide practical defaults and options to manage volume.
- The server operates on one PDF page per tool call; agents processing large documents will need to make many sequential calls.
- The server performs read-only operations; it cannot modify PDF files.
- Letter-level granularity may produce response sizes 5× larger than word-level; agents should only request it when necessary.
