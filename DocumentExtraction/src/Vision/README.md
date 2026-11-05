# CommunityToolkit.DocumentExtraction.Vision

Extract **text and structured tables from original PDFs or images with a
capable chat backend**. Results are returned as a provider-neutral
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

Supply a concrete **`IChatClient` supporting the input format**, configured with
a supported adapter, API, model/deployment and credentials. Image support does
**not** imply PDF support: for `application/pdf`, all of those layers must support
PDF file inputs. Add your chosen backend's adapter package separately; none is
included here. Structured mode additionally
requires schema-based JSON response support. Configure backend credentials,
endpoint and model before calling the integration methods below.
See [Microsoft.Extensions.AI concepts and integrations](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai).

The adapter sends the original document bytes and media type as MEAI
`DataContent` in **one chat request per extraction**, for both PDFs and images.
It does not parse, split or render PDFs locally and requires no PdfPig, SkiaSharp
or other PDF-rendering dependency. Backend implementations may have their own
dependencies and limits. Data egress, retention and inference charges depend on
your choice of service. Unsupported formats or capabilities raise backend errors;
there is no rasterization, alternate-model or retry fallback.

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

Separate integration method. Supply a chat backend that supports **both original
PDF file inputs and schema-based responses**. The schema
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

The complete original PDF is sent once as `application/pdf`, without conversion
to page images. `ExtractorName` becomes `ChatOptions.ModelId`;
it does not overwrite the genuine `ChatResponse.ModelId`. Additional properties
are rejected.
The input is buffered in memory. PDF file-size, page-count, context and format
limits belong to the configured backend. Image bytes also keep their original
supported media type.
Client options are copied at construction. Null or omitted options select
`Prompt = null` and `UseStructuredOutput = false`; a null prompt selects the
built-in instruction for the chosen output mode. The supplied chat client
remains borrowed, and both modes return `ExtractionDocument`.

Structured mode requires a JSON object with a `pages` array; tables can contain
addressed cells or table-local Markdown. Invalid output raises an error.
**There is no fallback to free-form or extra inference call to repair it.**
Validate important extracted facts: schema compliance does not establish accuracy.
The response's `pages` array is model-reported structure, not an independently
verified PDF page count. Reported indices become one-based source page numbers;
omitted indices remain unknown. No page numbers are inferred from array order,
and dimensions, coordinate units/origins and measured polygons remain absent.

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
  format. A vision-capable model or adapter that accepts images may still reject
  PDF file inputs. Check the adapter, API and model documentation together.
- Input is buffered in memory and forwarded in one chat call. Plan for backend
  document/context limits, latency and inference cost; this provider does not
  implement PDF validation, page splitting or automatic chunking.
- Free-form output is literal whole-document text in one section with an
  unnumbered source frame; it does not establish the PDF's page count.
  Structured frames follow the response's reported pages and optional indices,
  not local PDF measurements. No source dimensions or polygons are fabricated.
- Figure captions are `AlternativeText`; figure image bytes are not generated.
  Output is model-generated and should not be treated as exact OCR.
- Caller streams and chat clients remain open after provider disposal.
  Returned documents require no disposal. `RawRepresentation` is a
  single original `ChatResponse`.
- Pass cancellation tokens to input copying and chat calls. Cancellation cannot
  undo accepted backend work.
- For schema failures, check backend response-format support. For PDF failures,
  check native file-input support, model selection and document limits. Backend errors and
  cancellation propagate; failed model requests are not retried by this provider.

## Documentation and feedback

- [Microsoft.Extensions.AI concepts and integrations](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
- [OpenAI PDF file inputs](https://platform.openai.com/docs/guides/pdf-files)
- [Provider comparison and shared model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
