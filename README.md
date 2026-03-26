# PdfAnalyticsMcp

A local [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that gives AI agents deep insight into PDF documents. Agents can inspect text with font/color metadata, classified vector graphics, embedded images, and full-page visual previews — everything needed to faithfully convert complex PDFs into other formats like Markdown.

## Why

LLMs can read text, but PDFs are more than text. Table borders are drawn with vector graphics. Sidebars are shaded rectangles. Headings are distinguished by font name and size, not by semantic tags. PdfAnalyticsMcp exposes all of this structural information through MCP tools so agents can understand *how* a PDF page is laid out, not just *what* it says.

## Tools

The server exposes five tools, each operating on a single page (except `GetPdfInfo` which is document-level):

| Tool | Purpose |
|------|---------|
| **GetPdfInfo** | Page count, dimensions, title, author, subject, keywords, creator, producer, and the full bookmark/outline tree. |
| **GetPageText** | Text with bounding boxes, font name, font size, color, bold/italic flags. Supports `words` (default) or `letters` granularity. Optional `outputFile` writes CSV to disk for large pages. |
| **GetPageGraphics** | Classified vector shapes — rectangles, lines, and complex paths with fill/stroke colors, stroke width, and dash patterns. Useful for identifying table gridlines, sidebars, and dividers. |
| **GetPageImages** | Embedded image bounding boxes and pixel dimensions. Optionally includes base64-encoded PNG data. |
| **RenderPagePreview** | Renders a page as a PNG image at configurable DPI (72–600). Returns the image directly for multimodal models to inspect visually. |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (9.0.312 or later patch)

## Build

```shell
dotnet build
```

## Run Tests

```shell
dotnet test
```

## Configuration

### VS Code / GitHub Copilot

Add to your `mcp.json` (user or workspace):

```json
{
  "servers": {
    "pdf-analytics": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "src/PdfAnalyticsMcp"]
    }
  }
}
```

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "pdf-analytics": {
      "command": "dotnet",
      "args": ["run", "--project", "/absolute/path/to/src/PdfAnalyticsMcp"]
    }
  }
}
```

### Other MCP Clients

The server communicates over **stdio** (stdin/stdout). Point any MCP-compatible client at `dotnet run --project src/PdfAnalyticsMcp`. All logging goes to stderr so it never interferes with protocol messages.

### Using a Compiled Executable

If you prefer not to use `dotnet run`, you can publish a self-contained executable:

```shell
dotnet publish src/PdfAnalyticsMcp -c Release -o ./publish
```

Then reference the binary directly in your MCP configuration:

```json
{
  "servers": {
    "pdf-analytics": {
      "type": "stdio",
      "command": "./publish/PdfAnalyticsMcp"
    }
  }
}
```

This eliminates the SDK dependency at runtime and reduces startup time.

## Example Usage

Once connected, an agent can:

1. **Get document structure** — call `GetPdfInfo` to learn page count, dimensions, and bookmarks.
2. **Extract text with metadata** — call `GetPageText` on a page to get every word with its position, font, size, and color. Use font patterns to identify headings, body text, and table headers.
3. **Understand page layout** — call `GetPageGraphics` to find table borders, shaded regions, and dividers that define the visual structure.
4. **Inspect images** — call `GetPageImages` to locate embedded images and understand text flow around them.
5. **Visually verify** — call `RenderPagePreview` to get a PNG of the page and confirm structural understanding.

## PDF Converter Agents

The repository includes example [GitHub Copilot custom agents](https://code.visualstudio.com/docs/copilot/copilot-customization) in `.github/agents/` that demonstrate how to use these tools to convert complex PDF files to Markdown:

- **pdf-converter** — Orchestrator agent that converts single pages or page ranges. It analyzes font patterns to identify heading levels, delegates each page to a sub-agent, then combines and fixes heading hierarchies.
- **pdf-page-converter** — Worker agent that converts a single PDF page. Receives the PDF path, page number, chapter font pattern, and output path.

Invoke them in VS Code by typing `@pdf-converter` or `@pdf-page-converter` in Copilot Chat.

## Project Structure

```
src/PdfAnalyticsMcp/         Main server project
  Program.cs                 Host setup, MCP server registration
  Tools/                     One tool class per MCP tool
  Services/                  PDF extraction logic (PdfPig, Docnet)
  Models/                    DTOs for tool responses
tests/PdfAnalyticsMcp.Tests/ Unit and integration tests
  TestData/                  Sample PDFs for tests
specs/                       PRD, ADRs, feature specs, task breakdowns
```

## Technology

| Component | Library |
|-----------|---------|
| PDF text & graphics parsing | [PdfPig](https://github.com/UglyToad/PdfPig) |
| PDF page rendering | [Docnet](https://github.com/GowenGit/docnet) (PDFium) |
| MCP server SDK | [ModelContextProtocol](https://github.com/modelcontextprotocol/csharp-sdk) |
| Serialization | System.Text.Json |
| Hosting | Microsoft.Extensions.Hosting |

## Origin

This project — including all source code, specs, architecture decisions, tests, and this README — was created using customized GitHub Copilot agents from the [spec2cloud](https://github.com/EmeaAppGbb/spec2cloud) agentic development approach. No human-written code.

The underlying model was Claude Opus 4.6 (copilot).

## License

This project is licensed under the [MIT License](LICENSE). Third-party dependency licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
