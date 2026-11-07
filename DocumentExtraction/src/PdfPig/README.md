# CommunityToolkit.DocumentExtraction.PdfPig

Extract **text and page geometry from searchable PDFs without sending documents
to a service**. Results are returned as a provider-neutral `ExtractionDocument`
graph with text blocks, optional roles and source references.
Use it for PDFs that already contain text; scanned images require an OCR provider.

Local model-driven layout, rendering and OCR/table inference are planned as a
separate advanced companion, not part of this package. This basic provider has
no ONNX Runtime, model weights or native PDF-rendering dependency.

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
do not contain copies of descendant text; tables and images are not inferred
from these text blocks.

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

### Configure segmentation and roles

Alternative client construction; use this client in place of `extractor` above.
PdfPig's types are available through the transitive `PdfPig` dependency.

```csharp
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;

using var configuredExtractor = new PdfPigDocumentExtractionClient(
    pageSegmenter: DefaultPageSegmenter.Instance,
    roleResolver: block => block.Text.StartsWith("Invoice", StringComparison.Ordinal)
        ? "heading"
        : null);
```

The role callback is an application heuristic, not heading detection performed
by the provider. You can also supply an `IWordExtractor`. Injected helpers remain
borrowed; adapter disposal does not dispose them.

## Limitations and troubleshooting

- Only `application/pdf` is accepted. An image-only PDF can yield page frames
  without text. Use an OCR-capable provider when text must be recognized.
- `DocumentExtractionOptions.ExtractorName` must be absent or `pdfpig`.
  Additional properties and other selectors throw `NotSupportedException`.
- Parsing/layout errors and cancellation propagate to the caller. The provider
  buffers the input in memory; account for file size and PDF complexity.
- Dispose the client and input stream when finished. The provider leaves the
  caller's stream open; returned documents require no disposal.

## Documentation and feedback

- [PdfPig documentation and supported PDF features](https://github.com/UglyToad/PdfPig)
- [Provider comparison and shared document model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
