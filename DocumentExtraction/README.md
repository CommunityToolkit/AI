# Document extraction providers

These five preview provider packages derive from the experimental
`Microsoft.Extensions.DocumentExtraction.DocumentExtractionClient` over the
full `Microsoft.Extensions.Documents.ExtractionDocument` hierarchy. They are
not yet published to NuGet.org. No DataIngestion bridge, enrichment package,
public renderer, companion Documents framework, or core Markdown conversion
is introduced here.

**Preview breaking change:** `DocumentExtractionClient` replaces
`IDocumentExtractionClient` entirely. Implementers derive from the abstract class
and override `ExtractAsync`; callers and DI registrations use the new class.
There is no compatibility shim: previously compiled interface-based consumers
must rebuild. Stream input, completed-document results, middleware ordering and
borrowed-client ownership remain unchanged.

For example, the provider declaration changes from
`PdfPigDocumentExtractionClient : IDocumentExtractionClient` to
`PdfPigDocumentExtractionClient : DocumentExtractionClient`, with
`public override Task<ExtractionDocument> ExtractAsync(...)`. Existing callers
use `DocumentExtractionClient client = new PdfPigDocumentExtractionClient()`.
Replace DI registration and lookup of `IDocumentExtractionClient` with
`DocumentExtractionClient`, including keyed registrations/lookups. Middleware
still invokes the configured extraction pipeline; obtaining a native service
through `GetService` does not replace pipeline invocation.

| Package suffix | Source | Input |
|---|---|---|
| `PdfPig` | Native PdfPig | PDF |
| `AzureContentUnderstanding` | Azure AI Content Understanding SDK | Service-supported documents |
| `AzureDocumentIntelligence` | Azure Document Intelligence SDK | Service-supported documents |
| `Mistral` | Mistral OCR on Azure AI Foundry | Service-supported documents |
| `Vision` | Borrowed Microsoft.Extensions.AI `IChatClient` | Images and rasterized PDF pages |

All package IDs start with `CommunityToolkit.DocumentExtraction.` and target
`net8.0` and `net10.0`. Versions are centrally managed; the experimental core
dependencies currently require `10.10.0-review.20261102.1`. These core
prereleases are not released on a public package feed. Until the upstream
packages publish, the component uses a temporary, reproducible **public-source
prerequisite**, linked to [dotnet/extensions#7588](https://github.com/dotnet/extensions/pull/7588).

**Local migration boundary:** The committed pins and public-source bootstrap
below still provide the older interface-based core, not the abstract root.
The coordinated migration is validated separately with a private candidate
override. Public dependency/bootstrap coordination and clean-checkout CI
acceptance are still pending; the commands below do not yet build this migrated
source without that override.

`eng/Bootstrap-DocumentExtractionCore.ps1` fetches the full, unmodified public
`https://github.com/luisquintanilla/extensions.git` source at immutable commit
`0f69c5cd069e2a8bc53e5bc0021c6825b4d35726`, verifies its tree
`102a40f1bfb67efc0746bad6b64faa08c65fe169`, and packs the four original owning
projects with SDK `10.0.401`. It does not replace upstream projects/imports or
use a moving build DLL. `Features=` disables the upstream debug-determinism
compiler feature which otherwise removes portable-PDB SourceLink. No extra
`ContinuousIntegrationBuild` or `ArtifactsDir` build override is applied.

```powershell
.\eng\Bootstrap-DocumentExtractionCore.ps1 -DotnetPath C:\path\to\sdk-10.0.401\dotnet.exe
.\eng\New-DocumentExtractionNuGetConfig.ps1 `
    -CorePackageDirectory .\nuget\document-extraction-core `
    -OutputPath .\nuget\document-extraction-nuget.config
dotnet restore .\DocumentExtraction\DocumentExtraction.slnf `
    --configfile .\nuget\document-extraction-nuget.config
dotnet build .\DocumentExtraction\DocumentExtraction.slnf --no-restore -c Release
dotnet test .\DocumentExtraction\DocumentExtraction.slnf --no-build --no-restore -c Release
dotnet pack .\DocumentExtraction\DocumentExtraction.slnf --no-build --no-restore `
    -c Release --output .\nuget\document-extraction-providers
```

CI executes that same source bootstrap on Windows, uploads its actual nupkgs as
the same-run `document-extraction-core-source-packages` artifact, and restores
the Windows/Linux provider jobs from that artifact plus NuGet.org using an
isolated configuration. This is not a URL for historical CI binaries and does
not claim canonical cross-build byte equality. SDK installation remains isolated;
the bootstrap refuses dirty source checkouts or mismatched SDK/source identities.
Private review archives/configurations are not CI inputs or committed dependencies.
No package release, signing, or publishing job is enabled for this preview lane.

The component's tests use real Azure SDK HTTP replay, controlled Mistral HTTP
handlers, fake chat clients, and generated native PDFs. They require no cloud
authentication, embedding calls, models, or downloads. PdfPig remains native-text
only; no ONNX inference success is claimed.

## Representation and ownership

Content is local `AIContent` within the full document/section/text/image/table/
cell hierarchy. Documents and structural ancestors do not duplicate child text.
Source frames are ordinary entries in `SourcePages`, native observations retain their identity,
and polygons retain every vertex in order. SDK section pointers and table cell
spans are preserved. A missing original page number stays missing.

Callers own input streams and supplied SDK, HTTP, and chat clients. Returned
documents are data-only, non-disposable values. Temporary resources are disposed
after semantic image bytes and JSON observations have been detached. Optional
`document.RawRepresentation` exposes provider-native inspection: a native Azure
result, cloned Mistral JSON, or one/ordered multiple Vision chat responses.
PdfPig retains independent native blocks, not its disposed parser. These objects
are borrowed mutable inspection objects, not durable serialization or deep-clone
guarantees, and can contain sensitive data. There is no shared raw-response capture
policy, wire artifact, or operation registry. Request extractor selection does not
fabricate a returned model identity.

Text elements store text only in `TextContent.Text`; their optional role is
independent. Image OCR/transcription `Text` is distinct from `AlternativeText`;
captions and descriptions populate only the latter. Vision retains reported page
association without claiming measured paragraph polygons. Other providers retain
their measured source geometry without inventing units, page numbers, or regions.

## Source attribution

Native segmentation, bounding-box mapping, pluggable role resolution, and
rendering-resource/test patterns recover useful work authored by
[@luisquintanilla](https://github.com/luisquintanilla) in the closed, unmerged
[CommunityToolkit/AI#3](https://github.com/CommunityToolkit/AI/pull/3)
(`37b34b1fbf0a6f265257f4b46d75b687e3cf0684`) and
[CommunityToolkit/AI#14](https://github.com/CommunityToolkit/AI/pull/14)
(`f7ec65479ce9afe172579fb884d67f534dcf73e8`).
Provider-specific mapping and offline replay fixtures also adapt the retained
provider source supplied for this implementation. The old `IOcrClient` model,
MEDI policy/enrichment flow, scanned-page placeholders, and text-only Skia page
renderer are not revived: actual PDF rasterization uses PDFium internally.

Licensed under the repository's [MIT license](../LICENSE).
