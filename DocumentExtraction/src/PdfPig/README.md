# CommunityToolkit.DocumentExtraction.PdfPig

Extract **text, tables and page geometry from searchable PDFs without sending
documents to a service**. Results are returned as a provider-neutral
`ExtractionDocument` graph with text blocks, tables, optional roles and source
references.
Use it for PDFs that already contain text; scanned images require an OCR provider.

Local model-driven layout, rendering, OCR and image-based table recognition are
planned as a separate advanced companion, not part of this package. This basic
provider has no ONNX Runtime, model weights or native PDF-rendering dependency;
it detects tables from the PDF's native lines and text positions.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.PdfPig --prerelease
```

For `MEDE0001` or `EXTEXP0019` compiler diagnostics, see
[experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Requirements and dependencies

Experimental APIs emit `CTDE0001`; opt in explicitly as described in the
[component guide](../../README.md#experimental-api-diagnostics).

Targets **.NET Standard 2.0 and .NET 10**. See the [runtime support policy](../../README.md#framework-support).
The package brings these runtime dependencies
transitively; consumers do not need to install them separately:

- `PdfPig` **0.1.15** for PDF parsing, word extraction and segmentation.
- `Tabula` **1.0.1** (tabula-sharp) for table detection.
- `Microsoft.Extensions.DocumentExtraction.Abstractions`,
  `Microsoft.Extensions.Documents.Abstractions` and
  `Microsoft.Extensions.AI.Abstractions` for extraction options, document
  structure and content payloads.

There are no Azure credentials, model deployments, native PDF renderer or model
downloads to configure. The examples below need only this provider package.

## Examples

### Extract text from a PDF

Complete console example. Supply an existing searchable `document.pdf`.
The timeout token covers reading and extraction; the caller owns the stream.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using CommunityToolkit.DocumentExtraction.PdfPig;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
using var extractor = new PdfPigDocumentExtractionClient();
using var input = File.OpenRead("document.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "document.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var text in document.EnumerateContent().OfType<ExtractionDocumentTextElement>())
{
    Console.WriteLine(((TextContent)text.Content!).Text);
}
```

`TextContent.Text` is the text payload. Sections and other structural ancestors
do not contain copies of descendant text. Words inside a detected table appear
in its cells rather than in text blocks, so this loop also prints cell text.
Images are not extracted.

### Read tables

Continuation of the first example. Ruled tables are detected by default.

```csharp
foreach (var table in document.EnumerateContent().OfType<ExtractionDocumentTableElement>())
{
    Console.WriteLine($"Table: {table.Cells.RowCount} x {table.Cells.ColumnCount}");
    for (int row = 0; row < table.Cells.RowCount; row++)
    {
        for (int column = 0; column < table.Cells.ColumnCount; column++)
        {
            // Positions covered by a merged cell return null; the merged cell is at its top-left position.
            if (table.Cells[row, column] is { } cell)
            {
                string? text = ((cell.Content as ExtractionDocumentTextElement)?.Content as TextContent)?.Text;
                Console.WriteLine($"[{row}, {column}] span {cell.RowSpan} x {cell.ColumnSpan}: {text}");
            }
        }
    }
}
```

Each page's tables and text blocks are returned top to bottom, and a text block
never spans a table. Cell text is built from the same PdfPig words as the text
blocks, so a custom `IWordExtractor` applies to both and every word appears
once: in a cell or in a text block. Cell and table sources have rectangular
polygons, and a table source's `GetRawRepresentation()` returns the native
`Tabula.Table`.

Choose a mode with `PdfPigDocumentExtractionClientOptions.TableExtraction`:

| Mode | Detects | Merged cells |
|---|---|---|
| `Lattice` (default) | Tables drawn with ruling lines or cell borders | Inferred from the ruled cell edges |
| `LatticeThenStream` | Ruled tables, then whitespace-aligned tables where no ruled table was found | Ruled tables only |
| `None` | Nothing; all words are returned as text blocks, as in earlier versions | Not applicable |

Detected regions are rejected when they cannot form a valid grid or when their
text occupies only one row or one column, such as a boxed label or stacked form
panels.

### Read page geometry and native text blocks

Continuation of the first example; no additional package is needed.

```csharp
using UglyToad.PdfPig.DocumentLayoutAnalysis;

foreach (var text in document.EnumerateContent().OfType<ExtractionDocumentTextElement>())
{
    foreach (var source in text.Sources)
    {
        Console.WriteLine($"Original page: {source.Page.PageNumber}");
        foreach (var point in source.Polygon)
        {
            Console.WriteLine($"({point.X}, {point.Y})");
        }

        if (source.GetRawRepresentation() is TextBlock block)
        {
            Console.WriteLine($"Native block: {block.Text}");
        }
    }
}
```

Every PDF page has a source frame, including blank pages. Block bounding boxes
have four ordered vertices, in PDF points with a bottom-left origin. Native
`TextBlock` objects are available on source references; there is no native PDF
parser exposed through `document.RawRepresentation`.

### Configure tables, segmentation and roles

Alternative client construction; use this client in place of `extractor` above.
PdfPig's types are available through the transitive `PdfPig` dependency.

```csharp
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

using var configuredExtractor = new PdfPigDocumentExtractionClient(new PdfPigDocumentExtractionClientOptions
{
    TableExtraction = PdfPigTableExtractionMode.LatticeThenStream,
    PageSegmenter = DocstrumBoundingBoxes.Instance,
    RoleResolver = block => block.Text.StartsWith("Invoice", StringComparison.Ordinal)
        ? "heading"
        : null,
});
```

Option values are copied when the client is created. The role callback is an
application heuristic, not heading detection performed by the provider. You can
also supply an `IWordExtractor`. Injected helpers remain borrowed; adapter
disposal does not dispose them. The constructor with `pageSegmenter`,
`wordExtractor` and `roleResolver` parameters remains available and uses the
default `Lattice` mode.

## Limitations and troubleshooting

- Only `application/pdf` is accepted. An image-only PDF can yield page frames
  without text. Use an OCR-capable provider when text must be recognized.
- `DocumentExtractionOptions.ExtractorName` must be absent or `pdfpig`.
  Additional properties and other selectors throw `NotSupportedException`.
- Parsing/layout errors and cancellation propagate to the caller. The provider
  buffers the input in memory; account for file size and PDF complexity.
- Ruled tables are detected by default, which changes the output for PDFs that
  contain them: their words move from text blocks to table cells. Set
  `TableExtraction` to `None` to keep the earlier text-only output. Table
  detection adds per-page work; use `None` when only text is needed.
- Tables are detected per page; a table that continues onto the next page is
  returned as two tables. Headings and captions are not attached to tables.
- On partially ruled tables, `Lattice` may detect only the ruled part, such as
  the header band, and leave the unruled rows as text.
- `LatticeThenStream` can report charts, forms and other aligned text as
  tables, can split multi-line cells into separate rows and does not infer
  merged cells. Validate its output before relying on it.
- Text inside a cell, such as watermark letters, is included in the cell text.
- Dispose the client and input stream when finished. The provider leaves the
  caller's stream open; returned documents require no disposal.

## Documentation and feedback

- [PdfPig documentation and supported PDF features](https://github.com/UglyToad/PdfPig)
- [Provider comparison and shared document model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
