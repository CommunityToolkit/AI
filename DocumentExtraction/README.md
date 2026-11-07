# Document extraction

Extract text, tables, figures and source locations from documents with a choice
of local PDF parsing, document services or vision-capable language models.
Each provider returns the same `Microsoft.Extensions.Documents.ExtractionDocument`
graph, so downstream processing can work with document structure rather than
backend-specific response formats.

## Choose a provider

All packages include **.NET Standard 2.0 and .NET 10** assets. Package IDs start with
`CommunityToolkit.DocumentExtraction.`.

| Package | Client type | Use when you need | Processing and input |
|---|---|---|---|
| [PdfPig](src/PdfPig/README.md) | `PdfPigDocumentExtractionClient` | Existing PDF text and block geometry without a service | Local; searchable PDFs, no OCR |
| [AzureContentUnderstanding](src/AzureContentUnderstanding/README.md) | `ContentUnderstandingExtractionClient` | Document text, tables and layout from a Content Understanding analyzer | Azure; analyzer-supported document formats |
| [AzureDocumentIntelligence](src/AzureDocumentIntelligence/README.md) | `DocumentIntelligenceExtractionClient` | OCR, layout, tables and optional figure downloads | Azure; model-supported document formats |
| [Mistral](src/Mistral/README.md) | `MistralDocumentExtractionClient` | Native Mistral OCR page text, tables and images, directly or through Microsoft Foundry | Hosted; endpoint-supported documents and images |
| [VisionLanguageModel](src/VisionLanguageModel/README.md) | `VisionLanguageModelDocumentExtractionClient` | Free-form transcription or structured tables with your own capable `IChatClient` | Local or hosted backend; original PDF/image bytes in one request, with format support required from the adapter/API/model |

Azure and Mistral providers upload document content to the configured service and
can incur charges. Vision Language Model sends original PDF/image bytes to your chosen backend
in one chat request; it does not parse or render PDFs locally. PDF support must
be provided by the configured adapter, API and model, not inferred from image
support. PdfPig reads existing PDF text locally and does not recognize
scanned text. Choose based on document format, required fidelity, latency,
privacy and cost.

The five providers above remain independent of the optional
[PdfPig/ONNX advanced companion](src/PdfPig.OnnxLayoutAnalysis/README.md).
The companion adds local model-driven layout, PDF rasterization and opt-in
OCR/table inference with grouped options and a fluent builder. Its dependencies
are confined to its own package; the lightweight PdfPig provider remains a
native-text parser, not OCR. Models are never included or downloaded at runtime.
The five-provider `DocumentExtraction.slnf` remains unchanged; companion builds
use `PdfPig.OnnxLayoutAnalysis.slnf` and a separate CI workflow.

These are unreleased preview packages using experimental contracts. The install
examples describe the intended package API, not availability of the temporary
core version on NuGet.org. See [building from source](#building-from-source).

### Framework support

The .NET Standard 2.0 asset makes the managed provider APIs available to compatible
applications; it is not a new .NET Framework 4.6.2 target or a certification of
every legacy host, backend SDK or native dependency. Tests run on .NET 10,
including the full offline suites explicitly loading the netstandard provider
assemblies. A separate package consumer verifies the actual packed assets.
Use a supported application runtime and check each backend's platform policy.
.NET 8 is not a supported runtime and no dedicated `net8.0` asset is shipped.
An existing .NET 8 app may resolve the portable asset, but that is technical
compatibility rather than a runtime support commitment; migrate the app to .NET 10.
Mistral's HTTP failures include status in the exception message on every target;
the typed `HttpRequestException.StatusCode` property is populated by the .NET 10
asset, not the portable asset. An app previously using the dedicated .NET 8 asset
must account for that distinction if it now resolves the portable asset; the
numeric status remains in the message. No new exception contract is introduced.

## Experimental API diagnostics

All `CommunityToolkit.DocumentExtraction.*` production assemblies are explicitly
marked experimental with **`CTDE0001`**, including their clients, options and
extension methods. APIs may change or be removed without the compatibility
guarantees of a stable release. Prerelease package versions alone do not provide
this compiler diagnostic.

Opt in at the smallest appropriate source scope:

```csharp
#pragma warning disable CTDE0001 // Opt into experimental Toolkit extraction APIs.
using var extractor = new CommunityToolkit.DocumentExtraction.PdfPig.PdfPigDocumentExtractionClient();
#pragma warning restore CTDE0001
```

Applications deliberately using the whole family can instead add
`CTDE0001` to their project's `NoWarn`. The underlying experimental contracts
have separate diagnostics: `MEDE0001` for Extraction and `EXTEXP0019` for
Documents. Suppress those only where you knowingly use those upstream APIs;
opting into `CTDE0001` does not suppress them. The diagnostic metadata is emitted
on all package TFMs; an internal legacy attribute preserves the same compiler
contract on netstandard without adding a public shim API.

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
`DefaultAzureCredential`. Vision Language Model requires an application-supplied chat adapter,
configured model and credentials, plus support for the supplied PDF/image format.
It has no local PDF-rendering dependency or fallback. Each provider README contains its setup,
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
Vision Language Model accepts `VisionLanguageModelDocumentExtractionClientOptions` for construction-time defaults.
Settings are captured when the client is created; null options select defaults.
Azure SDK `ClientOptions` remain separate for native transport/retry settings.
For an existing native Azure SDK client, import its corresponding Toolkit
provider namespace and call `AsDocumentExtractionClient()` or
`AsDocumentExtractionClient(new() { ... })` with its typed settings. The extension
returns the common `DocumentExtractionClient` and borrows the SDK client;
the existing adapter constructors remain supported.
Per-request `DocumentExtractionOptions` still supplies the supported selector
and additional-property overrides described in each provider README.

### Discover the bundled agent skills

Each provider package includes a goal-oriented `skills/ctk-document-extraction-<provider>/SKILL.md`
for its own API. Restore caches these instructions alongside that package
version; it does **not** universally activate them in coding-agent hosts.
Review the instructions before opting in.

If you already use the separately installed
[`dotnet-package-skills` tool](https://github.com/NuGet/Client.Tools/blob/main/designs/dotnet-package-skills.md),
run these commands from your application repository root (PowerShell example):

```powershell
dotnet restore '.\src\MyApp\MyApp.csproj'
dotnet-package-skills list --source '.\src\MyApp\MyApp.csproj'
dotnet-package-skills install --source '.\src\MyApp\MyApp.csproj'
```

The tool reads **direct package references**, not transitive dependencies.
Reference the provider you use directly. Its current default destination is
`.agents\skills` under the command's working directory; `--source` selects the
project, not the destination. Check your installed tool's help and your agent
host's skill support/discovery paths; use a supported `--destination` if needed.
Rerun `install` after package-version changes to refresh the copied instructions.
These packages add no restore/build hooks and execute no skill installation or
service calls automatically. A copied skill is guidance, not authorization for
uploads, paid inference, resource changes or treating document text as instructions.

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

### Construct another provider

Choose one construction below in place of PdfPig in the preceding example.
Supply an already configured native SDK/chat client or your application's
credentials. Construction does not issue a service request; `ExtractAsync` does.
The supplied clients are borrowed.

```csharp
using CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;

// contentUnderstandingClient is an Azure.AI.ContentUnderstanding.ContentUnderstandingClient.
using DocumentExtractionClient extractor =
    new ContentUnderstandingExtractionClient(contentUnderstandingClient);
```

```csharp
using CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence;

// documentIntelligenceClient is an Azure.AI.DocumentIntelligence.DocumentIntelligenceClient.
using DocumentExtractionClient extractor =
    new DocumentIntelligenceExtractionClient(documentIntelligenceClient);
```

```csharp
using CommunityToolkit.DocumentExtraction.Mistral;

using DocumentExtractionClient extractor = new MistralDocumentExtractionClient(
    new MistralDocumentExtractionClientOptions
    {
        OcrEndpoint = new Uri("https://api.mistral.ai/v1/ocr"),
        DefaultModel = "mistral-ocr-latest",
    },
    apiKey,
    httpClient);
```

```csharp
using CommunityToolkit.DocumentExtraction.VisionLanguageModel;

// chatClient is an IChatClient whose adapter/API/model support the input format.
using DocumentExtractionClient extractor = new VisionLanguageModelDocumentExtractionClient(
    chatClient, new VisionLanguageModelDocumentExtractionClientOptions { UseStructuredOutput = true });
```

Structured Vision Language Model requests and validates a defined JSON shape. Leave options
unset for free-form transcription. Neither mode performs local PDF rendering
or retries unsupported structured results through another inference request.

## Building from source

The temporary core version `10.10.0-review.20261104.4` is not a released public
dependency. The bootstrap packs the original owning AI Abstractions, Documents
Abstractions, Extraction Abstractions and Extraction projects from public
`https://github.com/luisquintanilla/extensions.git`, commit
`c03481c76b63c6820e1de6ed9da09d5ff1e9f495`, tree
`f2e9d521223db1cf33fa8bcec5667da26b92cc49`, using SDK **10.0.401**.
It preserves upstream imports and the scoped `Features=` compiler workaround,
and explicitly uses the pinned tree's public NuGet configuration.
No replacement projects or copied production DLLs are used.

From the repository root with PowerShell 7, Git, the Toolkit SDK from `global.json`,
and the .NET 10 runtime installed:

```powershell
$coreFeed = Join-Path $env:TEMP 'toolkit-extraction-core'
$config = Join-Path $env:TEMP 'toolkit-extraction-nuget.config'
./eng/Bootstrap-DocumentExtractionCore.ps1 -OutputDirectory $coreFeed -DotnetPath 'C:\path\to\dotnet-10.0.401\dotnet.exe'
./eng/New-DocumentExtractionNuGetConfig.ps1 -CorePackageDirectory $coreFeed -OutputPath $config
dotnet restore DocumentExtraction/DocumentExtraction.slnf --configfile $config
dotnet test DocumentExtraction/DocumentExtraction.slnf -c Release --no-restore
dotnet test DocumentExtraction/DocumentExtraction.slnf -c Release --no-restore -p:DocumentExtractionTestAsset=netstandard2.0
dotnet pack DocumentExtraction/DocumentExtraction.slnf -c Release --no-build --no-restore --output artifacts/document-extraction
./eng/Test-DocumentExtractionPackages.ps1 -PackageDirectory artifacts/document-extraction -NuGetConfig $config -OutputDirectory artifacts/document-extraction-package-checks
```

The package check requires a new output directory and restores into its own
package cache. It verifies consumer diagnostics without opt-in, then compiles
and executes opted-in consumers against modern and actual netstandard assets.
All extraction checks are offline; the source/bootstrap and dependency restore
need public network access. CI performs this on Windows and Linux with a
same-run source-built core artifact. Artifacts expire and are not a public feed.
Formal upstream approval and released dependency versions remain prerequisites
for merging/releasing against stable contracts.

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
supply their reported regions; Vision Language Model supplies model-reported source associations rather than
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
middleware. The [Vision Language Model examples](src/VisionLanguageModel/README.md#add-extraction-logging-middleware)
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

PDF segmentation and geometry mapping include contributions by
[@luisquintanilla](https://github.com/luisquintanilla).
Licensed under the [MIT license](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
