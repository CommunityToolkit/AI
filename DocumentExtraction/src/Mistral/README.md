# CommunityToolkit.DocumentExtraction.Mistral

Extract **page text, tables and images from documents with Mistral OCR on Azure
AI Foundry**. Results are returned as a provider-neutral `ExtractionDocument`
graph, with native JSON available for service-specific fields. Use it when your
application needs Mistral's document transcription through an Azure endpoint.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.Mistral --prerelease
dotnet add package Azure.Identity
```

`Azure.Identity` is the additional application dependency used by the
`DefaultAzureCredential` examples. For `MEDE0001` or `EXTEXP0019` compiler
diagnostics, see [experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Requirements and setup

Targets **.NET 8 and .NET 10**. These packages are runtime dependencies installed
transitively:

- `Azure.Core` **1.53.0**, providing the `TokenCredential` contract.
- `Microsoft.Extensions.DocumentExtraction.Abstractions`,
  `Microsoft.Extensions.Documents.Abstractions` and
  `Microsoft.Extensions.AI.Abstractions` for extraction options, document
  structure and content payloads.

You need an Azure endpoint exposing `/providers/mistral/azure/ocr`, a
model/deployment accepted by that endpoint, and an Entra identity authorized to
call it. Set up the deployment and access before running extraction. The client
defaults to `mistral-ocr-4-0`; use the model selector available in your resource.
Direct Mistral and generic OpenAI-compatible endpoints use different routes and
are not supported by this client.

Set `AZURE_MISTRAL_OCR_ENDPOINT` to the base endpoint (without the route suffix)
and `AZURE_MISTRAL_OCR_MODEL` to its supported model/deployment selector.
Authentication uses `TokenCredential` for
`https://cognitiveservices.azure.com/.default`; direct Mistral API keys are not
accepted. The example uses `DefaultAzureCredential`, requiring
**`Azure.Identity` as an extra package** and a configured development sign-in or
deployed identity. It is not a dependency of the provider.

## Examples

### Extract page text

Complete console example. Provide `invoice.pdf` and the environment variables
above. This uploads the file as a base64 data URL and can incur inference charges.

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Azure.Identity;
using CommunityToolkit.DocumentExtraction.Mistral;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

string endpoint = Environment.GetEnvironmentVariable("AZURE_MISTRAL_OCR_ENDPOINT")
    ?? throw new InvalidOperationException("Set AZURE_MISTRAL_OCR_ENDPOINT.");
string model = Environment.GetEnvironmentVariable("AZURE_MISTRAL_OCR_MODEL")
    ?? throw new InvalidOperationException("Set AZURE_MISTRAL_OCR_MODEL.");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
using var extractor = new MistralDocumentExtractionClient(
    new Uri(endpoint), new DefaultAzureCredential(), defaultModel: model);
using var input = File.OpenRead("invoice.pdf");
ExtractionDocument document = await extractor.ExtractAsync(
    input, "invoice.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var element in document.EnumerateContent())
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
}
```

This traversal includes table-local `TextContent`, not just text elements.
Tables are retained as producer-supplied content, not synthesized into an
addressed cell grid. Sections do not duplicate aggregate page text.

### Request images and bound image output

Continuation of the first example; the endpoint must support these options.

```csharp
using Microsoft.Extensions.DocumentExtraction;

var options = new DocumentExtractionOptions { ExtractorName = model };
options.AdditionalProperties["includeImages"] = true;
options.AdditionalProperties["imageLimit"] = 4;
options.AdditionalProperties["imageMinSize"] = 100;
using var illustratedInput = File.OpenRead("invoice.pdf");
var illustratedDocument = await extractor.ExtractAsync(
    illustratedInput, "invoice.pdf", "application/pdf", options, timeout.Token);

foreach (var image in illustratedDocument.EnumerateContent().OfType<ExtractionDocumentImageElement>())
{
    if (image.Content is DataContent bytes)
    {
        Console.WriteLine($"{bytes.MediaType}: {bytes.Data.Length} image bytes");
    }
}
```

These are the only supported additional properties: `includeImages` is a bool;
`imageLimit` and `imageMinSize` are non-negative ints. `ExtractorName` overrides
the constructor's request selector. The limits govern service image output, not
input buffering or a total request-memory cap.

### Inspect native JSON and control HTTP lifetime

Continuation of the first example. No extra package is required for JSON or HTTP.
The caller owns the supplied `HttpClient`; the adapter borrows it.

```csharp
using System.Net.Http;
using System.Text.Json;

if (document.RawRepresentation is JsonElement native &&
    native.TryGetProperty("model", out var reportedModel))
{
    Console.WriteLine($"Reported model: {reportedModel.GetString()}");
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
using var borrowedHttpExtractor = new MistralDocumentExtractionClient(
    new Uri(endpoint), new DefaultAzureCredential(), defaultModel: model,
    httpClient: http);
```

The native JSON remains usable after extraction finishes. Read response fields
such as `model` only when present; the requested model selector is not a
substitute for a service-reported identity.

## Limitations and troubleshooting

- The adapter buffers the input, base64 request and response in memory. Account
  for file size and service input limits; supported formats depend on your
  endpoint. Only HTTP 503 is retried, at most five total attempts; other HTTP
  failures propagate as `HttpRequestException`.
- Source page numbers are one-based. Table content remains Markdown in
  table-local `TextContent`; it is not an addressed cell grid. Tables that cannot
  be placed unambiguously in page text remain separate elements. Do not treat
  Markdown as measured paragraph geometry.
- Invalid/missing response structure and unsupported options fail explicitly.
  For authentication/404 errors, check identity access, base endpoint, route and
  model availability; using a direct Mistral endpoint is not supported.
- Caller input remains open. A supplied HTTP client is borrowed; one created by
  the provider is disposed with it. Returned documents require no disposal;
  image bytes and JSON remain usable after the request finishes.
- Cancellation covers input copying, token acquisition, requests, retry delays
  and mapping. It does not roll back remote processing already accepted.
  Native JSON and extracted text can contain sensitive data.

## Documentation and feedback

- [Mistral OCR concepts](https://docs.mistral.ai/studio/document-processing/basic_ocr)
  (direct-service examples use different authentication/routes from this adapter)
- [Microsoft Foundry documentation](https://learn.microsoft.com/azure/ai-foundry/)
- [Azure Identity credential configuration](https://learn.microsoft.com/dotnet/azure/sdk/authentication/)
- [Provider comparison and shared model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
