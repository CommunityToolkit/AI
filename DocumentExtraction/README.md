# Document extraction

Extract text, tables, figures and source locations from documents with a choice
of local PDF parsing, document services or vision-capable language models.
Each provider returns the same `Microsoft.Extensions.Documents.ExtractionDocument`
graph, so downstream processing can work with document structure rather than
backend-specific response formats.

## Choose a provider

All packages target **.NET 8 and .NET 10**. Package IDs start with
`CommunityToolkit.DocumentExtraction.`.

| Package | Client type | Use when you need | Processing and input |
|---|---|---|---|
| [PdfPig](src/PdfPig/README.md) | `PdfPigDocumentExtractionClient` | Existing PDF text and block geometry without a service | Local; searchable PDFs, no OCR |
| [PdfPig.OnnxLayoutAnalysis](src/PdfPig.OnnxLayoutAnalysis/README.md) | `PdfPigOnnxDocumentExtractionClient` | Native PDF text with model-detected headings, tables and pictures | Local; actual page rasterization and an explicitly acquired Heron ONNX model, no OCR |
| [AzureContentUnderstanding](src/AzureContentUnderstanding/README.md) | `ContentUnderstandingExtractionClient` | Document text, tables and layout from a Content Understanding analyzer | Azure; analyzer-supported document formats |
| [AzureDocumentIntelligence](src/AzureDocumentIntelligence/README.md) | `DocumentIntelligenceExtractionClient` | OCR, layout, tables and optional figure downloads | Azure; model-supported document formats |
| [Mistral](src/Mistral/README.md) | `MistralDocumentExtractionClient` | Native Mistral OCR page text, tables and images, directly or through Microsoft Foundry | Hosted; endpoint-supported documents and images |
| [Vision](src/Vision/README.md) | `VisionDocumentExtractionClient` | Free-form transcription or structured tables with your own vision-capable `IChatClient` | Local or hosted model; images and locally rasterized PDF pages |

Azure and Mistral providers upload document content to the configured service and
can incur charges. Vision sends images to your chosen backend; a PDF makes one inference
call per page. PdfPig reads existing PDF text locally and does not recognize
scanned text. Choose based on document format, required fidelity, latency,
privacy and cost.

`PdfPig.OnnxLayoutAnalysis` is optional: the basic PdfPig package does not bring
in Skia rendering, ONNX Runtime or model weights. The companion detects layout regions,
not table cells or scanned text; see its setup and result semantics before use.
Its managed ONNX dependency does not choose a native runtime. Applications add
`Microsoft.ML.OnnxRuntime` 1.23.2 for CPU or
`Microsoft.ML.OnnxRuntime.Gpu` 1.23.2 for CUDA, then configure a caller-owned
session for GPU execution. PDF rendering and preprocessing still run on CPU.
Both the companion and Vision render PDFs with `PdfPig.Rendering.Skia`, including
native SkiaSharp/HarfBuzz assets and managed PDF image filters. This is not a
native-free rendering stack. See the provider READMEs for platform and licensing
requirements; basic PdfPig does not acquire these dependencies.

## Install

Install the provider your application needs:

```shell
dotnet add package CommunityToolkit.DocumentExtraction.PdfPig --prerelease
```

Substitute any package ID from the table. The providers bring in
`Microsoft.Extensions.DocumentExtraction.Abstractions`,
`Microsoft.Extensions.Documents.Abstractions` and
`Microsoft.Extensions.AI.Abstractions` transitively.

Azure examples use the additional `Azure.Identity` package for
`DefaultAzureCredential`. Vision requires an application-supplied chat adapter,
configured model and credentials. Each provider README contains its setup,
supported options and examples.

Mistral uses one package for both hosts. Configure its complete native OCR
operation URI and model/deployment selector with
`MistralDocumentExtractionClientOptions`; authenticate with a bearer API key or,
for an endpoint supporting Entra ID, a `TokenCredential` and the resource's token
scope. Direct Mistral uses `https://api.mistral.ai/v1/ocr`; Foundry operation paths
depend on the deployment. No Chat/Responses adapter or second Mistral package is
required.

Azure Content Understanding and Document Intelligence accept typed
`ContentUnderstandingExtractionOptions` and `DocumentIntelligenceExtractionOptions`;
Vision accepts `VisionDocumentExtractionClientOptions` for construction-time defaults.
Settings are captured when the client is created; null options select defaults.
Azure SDK `ClientOptions` remain separate for native transport/retry settings.
For an existing native Azure SDK client, import its corresponding Toolkit
provider namespace and call `AsDocumentExtractionClient()` or
`AsDocumentExtractionClient(new() { ... })` with its typed settings. The extension
returns the common `DocumentExtractionClient` and borrows the SDK client;
the existing adapter constructors remain supported.
Per-request `DocumentExtractionOptions` still supplies the supported selector
and additional-property overrides described in each provider README.

## Extract a document

This complete console example uses PdfPig. Supply an existing searchable PDF.

```csharp
using System;
using System.IO;
using System.Threading;
using CommunityToolkit.DocumentExtraction.PdfPig;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
using DocumentExtractionClient extractor = new PdfPigDocumentExtractionClient();
using var input = File.OpenRead("document.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "document.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var element in document.EnumerateContent())
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
}
```

All providers use `ExtractAsync(Stream, identifier, mediaType, options,
cancellationToken)`. The identifier labels the result; it is not a URL that the
provider fetches. Supply a readable stream and the actual media type.
`DocumentExtractionOptions.ExtractorName` selects an analyzer/model where the
provider supports it. Provider-specific options belong in `AdditionalProperties`;
unsupported options raise errors.

## Work with the result

`ExtractionDocument` contains sections and a hierarchy of text, images and
tables. Content is local to each element; structural ancestors do not repeat
their children's text. `EnumerateContent()` walks section elements in depth-first
order and yields non-section elements, including tables. It does **not** enter
table cells or return document/section-local payloads. Inspect `document.Content`,
section `Content` and table cells directly when you need those payloads.

| Result | How to use it |
|---|---|
| Text | `ExtractionDocumentTextElement.Content` contains `TextContent`; read its `Text` and the element's optional role. |
| Tables | `ExtractionDocumentTableElement.Cells` exposes `RowCount`, `ColumnCount` and a `[row, column]` indexer. A cell can hold a text element or nested section, with spans and an optional role. Mistral and some vision results instead keep table-local Markdown in the table's `Content`. |
| Images | Read `ExtractionDocumentImageElement.Content` when image data is available. `AlternativeText` is a caption/description; `Text` is recognized image text, when supplied. |
| Sources | `document.SourcePages` describes source frames. Element `Sources` associate content with a page and any reported ordered polygon. Page numbers, dimensions or geometry can be absent. |
| Native data | `document.RawRepresentation` and `source.GetRawRepresentation()` expose backend-specific inspection objects where available. |

### Read table cell content

The cell indexer returns an actual anchor cell or null. Null means either an empty
position or one covered by another anchor's span; it is not an empty text cell.
Read `RowIndex`, `ColumnIndex`, `RowSpan` and `ColumnSpan` from non-null anchors
to understand the occupied range. An anchor can itself have null `Content`.

Continuation of the example above. This walks sections and table anchor cells
explicitly, including section-valued mixed cell content and nested tables:

```csharp
foreach (var page in document.SourcePages)
{
    Console.WriteLine($"Source {page.Identifier}, original page {page.PageNumber}");
}

foreach (var section in document.Sections)
{
    PrintElement(section);
}

void PrintElement(ExtractionDocumentElement element)
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
    if (element is ExtractionDocumentImageElement image &&
        image.Content is DataContent data)
    {
        Console.WriteLine($"{image.AlternativeText}: {data.Data.Length} image bytes");
    }
    if (element is ExtractionDocumentSection section)
    {
        foreach (var child in section.Elements)
        {
            PrintElement(child);
        }
    }
    else if (element is ExtractionDocumentTableElement table)
    {
        foreach (var cell in table.Cells)
        {
            if (cell is null)
            {
                continue;
            }

            Console.WriteLine(
                $"Cell ({cell.RowIndex}, {cell.ColumnIndex}), span {cell.RowSpan} x {cell.ColumnSpan}");
            if (cell.Content is { } content)
            {
                PrintElement(content);
            }
        }
    }
}
```

Source capabilities vary: PdfPig supplies PDF block geometry; Azure services
supply their reported regions; Vision supplies page associations rather than
measured paragraph polygons. Check for data instead of assuming every provider
supplies the same level of detail.

## Lifetimes, cancellation and middleware

Dispose extraction clients when finished. Providers leave caller-supplied input
streams and SDK, HTTP or chat clients open. Returned documents require no
disposal; extracted image bytes and data remain usable after extraction.
Native inspection objects can be mutable and contain sensitive data, so avoid
treating them as a durable interchange format or logging them indiscriminately.

Providers buffer input in memory. Set application file-size limits and pass a
cancellation token; cancellation cannot undo remote processing already accepted.
Service, parsing and validation errors propagate to the caller.

For extraction middleware such as logging, also install
`Microsoft.Extensions.DocumentExtraction`:

```shell
dotnet add package Microsoft.Extensions.DocumentExtraction --prerelease
```

Use `DocumentExtractionClientBuilder` to wrap the provider, and call
`ExtractAsync` on the resulting pipeline. Native `GetService` access is for
backend-specific operations; direct calls to those services bypass extraction
middleware. The [Vision examples](src/Vision/README.md#add-extraction-logging-middleware)
show a logging pipeline.

## Experimental API diagnostics

The shared contracts produce `MEDE0001` and `EXTEXP0019` experimental API
diagnostics. To use these APIs, opt in explicitly in your application project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);MEDE0001;EXTEXP0019</NoWarn>
</PropertyGroup>
```

## Feedback and license

[Report issues or request features](https://github.com/CommunityToolkit/AI/issues).
When reporting a problem, include the provider, package versions, input media type
and a minimal reproduction without credentials or sensitive document content.

PDF segmentation, geometry and rendering integration include contributions by
[@luisquintanilla](https://github.com/luisquintanilla).
Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
