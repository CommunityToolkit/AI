# CommunityToolkit.DocumentExtraction.AzureContentUnderstanding

Extract **text, tables and layout from documents using Azure AI Content
Understanding**. Results are returned as a provider-neutral `ExtractionDocument`
graph of sections, text, tables and figures, with source pages and geometry.
Use it to process documents with your chosen Content Understanding analyzer while
keeping downstream document handling independent of the Azure SDK.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.AzureContentUnderstanding --prerelease
dotnet add package Azure.Identity
```

`Azure.Identity` is needed for the `DefaultAzureCredential` examples, not by the
provider itself. For `MEDE0001` or `EXTEXP0019` compiler diagnostics, see
[experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Requirements and setup

Targets **.NET 8 and .NET 10**. Runtime dependencies are installed transitively:

- `Azure.AI.ContentUnderstanding` **1.1.0**.
- `Microsoft.Extensions.DocumentExtraction.Abstractions`,
  `Microsoft.Extensions.Documents.Abstractions` and
  `Microsoft.Extensions.AI.Abstractions` for extraction options, document
  structure and content payloads.

You also need a Microsoft Foundry resource in a region supporting Content
Understanding, a document analyzer available to that resource, and permission to
invoke it. Complete any model deployment and default-model mapping required by
your chosen analyzer. This adapter does not provision resources, deploy models or
configure those mappings. Follow the
[SDK resource setup](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.ContentUnderstanding_1.1.0/sdk/contentunderstanding/Azure.AI.ContentUnderstanding/README.md#prerequisites).

The examples use Microsoft Entra ID via `DefaultAzureCredential`.
Configure a supported credential (for example a signed-in Azure CLI
for development, or managed identity in Azure) and the resource's required access
roles, including **Cognitive Services User** on the resource.
Set `AZURE_CONTENT_UNDERSTANDING_ENDPOINT` and
`AZURE_CONTENT_UNDERSTANDING_ANALYZER` to your resource endpoint and chosen
document analyzer ID. No credential secrets belong in source code.

## Examples

### Analyze a document

Complete console example. Provide `invoice.pdf` and the environment variables
above. This uploads the file to Azure and can incur service charges.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Azure.AI.ContentUnderstanding;
using Azure.Identity;
using CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.Extensions.DocumentExtraction;

string endpoint = Environment.GetEnvironmentVariable("AZURE_CONTENT_UNDERSTANDING_ENDPOINT")
    ?? throw new InvalidOperationException("Set AZURE_CONTENT_UNDERSTANDING_ENDPOINT.");
string analyzer = Environment.GetEnvironmentVariable("AZURE_CONTENT_UNDERSTANDING_ANALYZER")
    ?? throw new InvalidOperationException("Set AZURE_CONTENT_UNDERSTANDING_ANALYZER.");

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var sdkClient = new ContentUnderstandingClient(new Uri(endpoint), new DefaultAzureCredential());
using DocumentExtractionClient extractor = sdkClient.AsDocumentExtractionClient(
    new() { AnalyzerId = analyzer });
using var input = File.OpenRead("invoice.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "invoice.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var text in document.EnumerateContent().OfType<ExtractionDocumentTextElement>())
{
    Console.WriteLine(((TextContent)text.Content!).Text);
    foreach (var source in text.Sources)
    {
        Console.WriteLine($"Page {source.Page.PageNumber}: {source.Polygon.Count} vertices");
    }
}
```

Use a service-supported document/media-type combination. The adapter defaults
to the `prebuilt-document` base analyzer; select a document analyzer with the
capabilities you need rather than relying on that default.
Toolkit options are copied at construction; null or omitted options select the
defaults. For native transport/retry settings, pass the separate Azure SDK
`ContentUnderstandingClientOptions` as `clientOptions`, or supply a configured
borrowed SDK client alongside the Toolkit options.
`AsDocumentExtractionClient` borrows that exact native SDK client, preserving its
credentials, transport and retry configuration. Calling it with null or omitted
options selects the defaults. The adapter constructors remain supported for
endpoint/credential construction or direct native-client wrapping.
The traversal above lists text outside table cells; see
[reading table cell content](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#read-table-cell-content)
to include text and mixed content inside cells.

### Select an analyzer for one request

Continuation of the first example. `ExtractorName` is an analyzer ID, not a model
deployment name. Use an ID supported by your configured resource.

```csharp
using Microsoft.Extensions.DocumentExtraction;

var options = new DocumentExtractionOptions { ExtractorName = analyzer };
options.AdditionalProperties["processingLocation"] = "DataZone";
using var nextInput = File.OpenRead("invoice.pdf");
var nextDocument = await extractor.ExtractAsync(
    nextInput, "invoice.pdf", "application/pdf", options, timeout.Token);
```

`processingLocation` is the only supported additional property and must be a
string. The service must support the requested location for your region/analyzer;
omitting it uses the SDK/service default. Other explicit properties are rejected
before a request is sent.

### Inspect the native result

Continuation; uses the transitive SDK dependency, not an extra package.

```csharp
if (document.RawRepresentation is Azure.AI.ContentUnderstanding.AnalysisResult native)
{
    Console.WriteLine($"Native content items: {native.Contents.Count}");
}

var sdk = extractor.GetService(
    typeof(Azure.AI.ContentUnderstanding.ContentUnderstandingClient))
    as Azure.AI.ContentUnderstanding.ContentUnderstandingClient;
Console.WriteLine($"Native SDK available: {sdk is not null}");
```

The `ContentUnderstandingExtractionClient` constructor also accepts an
existing `ContentUnderstandingClient` and optional
`ContentUnderstandingExtractionOptions`, as an alternative to
the extension. Both paths capture the settings and leave the native client borrowed.

## Working with results

Text is stored in `TextContent`; tables retain cell spans/roles, and source
references retain reported pages and ordered polygons. Cells can contain nested
sections when the service supplies explicit child references.
`EnumerateContent()` walks section elements but does not enter table cells;
inspect `table.Cells` and recursively read each anchor's `Content` for those
elements. Fields not projected into this graph remain available on `AnalysisResult`.

When an analyzer returns whole-document Markdown without paragraphs, tables,
figures or page lines (for example `prebuilt-document` without detailed output),
the adapter preserves it literally as one text element in a document-wide section.
That element references all reported pages without polygons; it does not assign
Markdown fragments to individual pages or reconstruct tables from Markdown.
When detailed children are present, those remain authoritative and aggregate
Markdown is not appended again. Choose an analyzer such as `prebuilt-layout`
when you need the service's structured layout details; this adapter does not
change the analyzer configuration or request detailed output automatically.

Figure descriptions are `ExtractionDocumentImageElement.AlternativeText`.
This provider does not download figure image bytes. Only document results are
supported; use the native SDK for audio/video analysis.

## Limitations and troubleshooting

- Document bytes leave the application for your Azure endpoint. Review service
  region, privacy, pricing and file limits before uploading sensitive documents.
- Input is buffered in memory. Pass a cancellation token to bound input copying
  and the SDK operation; cancellation cannot undo remote work already accepted.
- The caller owns the input stream and any supplied SDK client. Disposing the
  provider does not dispose either. Returned documents require no disposal.
- For `RequestFailedException`, check the endpoint, Entra roles, analyzer
  availability and required model/default mappings. Unsupported options,
  non-document results and invalid result relationships raise errors rather
  than silently omitting content.
- Treat native results as inspection objects that may contain sensitive data;
  they are not a durable serialization format.

## Documentation and feedback

- [SDK setup, authentication and examples](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.ContentUnderstanding_1.1.0/sdk/contentunderstanding/Azure.AI.ContentUnderstanding/README.md)
- [Content Understanding documentation](https://learn.microsoft.com/azure/ai-services/content-understanding/)
- [Provider comparison and shared model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
