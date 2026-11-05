# CommunityToolkit.DocumentExtraction.Vision

Extract **text and structured tables from images or PDF pages with a
vision-capable language model**. Results are returned as a provider-neutral
`ExtractionDocument` graph. Use your application's `IChatClient` to choose a
local or hosted backend, and select free-form transcription or structured output.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.Vision --prerelease
```

Add your chosen backend's `IChatClient` adapter package separately. For
`MEDE0001` or `EXTEXP0019` compiler diagnostics, see
[experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Requirements and setup

Targets **.NET 8 and .NET 10**. Runtime dependencies installed transitively:

- `Microsoft.Extensions.AI.Abstractions`,
  `Microsoft.Extensions.DocumentExtraction.Abstractions` and
  `Microsoft.Extensions.Documents.Abstractions` for chat, extraction and the
  document graph.
- `PdfPig` **0.1.15** for PDF page metadata.
- `Docnet.Core` **2.6.0** (PDFium), `SkiaSharp` **3.119.0** and
  `SkiaSharp.NativeAssets.Linux.NoDependencies` **3.119.0** for PDF rasterization.

PDF input requires compatible PDFium and SkiaSharp native assets for the
application's OS/architecture. Check native loading in your actual deployment;
framework support alone does not establish support for every runtime identifier.

Supply a concrete **vision-capable `IChatClient`**, configured with a supported
model/deployment, credentials and image handling. Add your chosen backend's
adapter package separately; none is included here. Structured mode additionally
requires schema-based JSON response support. Configure backend credentials,
endpoint and model before calling the integration methods below.
See [Microsoft.Extensions.AI concepts and integrations](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai).

Images are sent to this backend. Data egress, retention and inference charges
depend on your choice of service. PDF rendering happens locally, but rendered
pages are also sent to the chat client.

## Examples

### Extract free-form text from an image

Complete integration method. Call it with your configured vision chat client,
an existing PNG and an application cancellation token. No extra package beyond
this provider and your backend adapter is needed.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.DocumentExtraction.Vision;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

public static class ImageExtraction
{
    public static async Task<ExtractionDocument> ExtractAsync(
        IChatClient chatClient, string pngPath, CancellationToken cancellationToken)
    {
        using var extractor = new VisionDocumentExtractionClient(chatClient);
        using var input = File.OpenRead(pngPath);
        ExtractionDocument document = await extractor.ExtractAsync(
            input, Path.GetFileName(pngPath), "image/png",
            cancellationToken: cancellationToken);

        foreach (var text in document.EnumerateContent().OfType<ExtractionDocumentTextElement>())
        {
            Console.WriteLine(((TextContent)text.Content!).Text);
        }

        if (document.RawRepresentation is ChatResponse native)
        {
            Console.WriteLine($"Reported model: {native.ModelId}");
        }

        return document;
    }
}
```

Default free-form mode asks for GitHub-flavored Markdown but returns the model's
literal response in a text element. It does **not** parse Markdown into shared
tables or headings. `VisionDocumentExtractionClientOptions.Prompt` replaces the default
instruction; output remains model-generated and must not be trusted as exact OCR.

### Request structured tables from a PDF

Separate integration method. Native rendering prerequisites apply. The schema
supports page Markdown, tables with addressed cells/spans/roles, figure captions,
language and confidence; the model must actually supply them.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.DocumentExtraction.Vision;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

public static class StructuredExtraction
{
    public static async Task<ExtractionDocument> ExtractAsync(
        IChatClient chatClient, string pdfPath, string modelId,
        CancellationToken cancellationToken)
    {
        using var extractor = new VisionDocumentExtractionClient(
            chatClient, new VisionDocumentExtractionClientOptions { UseStructuredOutput = true });
        using var input = File.OpenRead(pdfPath);
        var options = new DocumentExtractionOptions { ExtractorName = modelId };
        var document = await extractor.ExtractAsync(
            input, Path.GetFileName(pdfPath), "application/pdf", options, cancellationToken);

        foreach (var table in document.EnumerateContent().OfType<ExtractionDocumentTableElement>())
        {
            Console.WriteLine($"Table: {table.Cells.RowCount} rows, {table.Cells.ColumnCount} columns");
        }

        return document;
    }
}
```

PDF pages are rendered locally as PNG and sent individually, with one chat call
per page, in original page order. `ExtractorName` becomes `ChatOptions.ModelId`;
it does not overwrite the genuine `ChatResponse.ModelId`. Additional properties
are rejected.
Client options are copied at construction. Null or omitted options select
`Prompt = null` and `UseStructuredOutput = false`; a null prompt selects the
built-in instruction for the chosen output mode. The supplied chat client
remains borrowed, and both modes return `ExtractionDocument`.

Structured mode requires a JSON object with a `pages` array; tables can contain
addressed cells or table-local Markdown. Invalid output raises an error.
**There is no fallback to free-form or extra inference call to repair it.**
Validate important extracted facts: schema compliance does not establish accuracy.

### Add extraction logging middleware

Separate factory method. Add the middleware package:

```shell
dotnet add package Microsoft.Extensions.DocumentExtraction --prerelease
```

Supply `ILoggerFactory` from your application's logging setup. Avoid logging
sensitive document content.

```csharp
using CommunityToolkit.DocumentExtraction.Vision;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Logging;

public static class ExtractionPipeline
{
    public static DocumentExtractionClient Create(
        IChatClient chatClient, ILoggerFactory loggerFactory)
        => new DocumentExtractionClientBuilder(new VisionDocumentExtractionClient(chatClient))
            .UseLogging(loggerFactory)
            .Build();
}
```

Call `ExtractAsync` on the returned pipeline and dispose it when finished.
The supplied chat client stays borrowed. `GetService` returns the adapter itself
when applicable and otherwise forwards native/keyed lookups to that client;
calling native services directly bypasses extraction middleware.

## Limitations and troubleshooting

- Accepts `image/*` and `application/pdf`. Your backend must support the actual
  image format. A text-only model cannot process these requests.
- Input is buffered in memory. PDFs require local native rendering plus one
  inference call per page; plan for memory, latency and inference cost.
- Sources identify pages, not measured paragraph polygons. PDF frames retain
  known original numbers and point dimensions. Image page numbers remain absent
  unless explicitly reported by structured output.
- Figure captions are `AlternativeText`; figure image bytes are not generated.
  Output is model-generated and should not be treated as exact OCR.
- Caller streams and chat clients remain open after provider disposal.
  Returned documents require no disposal. `RawRepresentation` is a
  `ChatResponse` for one call or an ordered `ChatResponse[]` for multiple calls.
- Pass cancellation tokens to input copying and chat calls. Cancellation cannot
  undo accepted inference or interrupt every synchronous native rendering step.
- For schema failures, check backend response-format support. For native loading
  failures, check PDFium/SkiaSharp assets for your RID. Backend errors and
  cancellation propagate; failed model requests are not retried by this provider.

## Documentation and feedback

- [Docnet native PDF rendering](https://github.com/GowenGit/docnet)
- [SkiaSharp native deployments](https://github.com/mono/SkiaSharp)
- [Provider comparison and shared model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
