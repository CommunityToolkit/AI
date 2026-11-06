# Reviewing the document extraction providers

## Scope and entry points

This change adds five independent `CommunityToolkit.DocumentExtraction.*`
packages, targeting .NET 8 and .NET 10. Each implements the shared abstract
`DocumentExtractionClient` and returns `ExtractionDocument`; providers do not
depend on each other or on DataIngestion.

| Review area | Start here | Important boundary |
|---|---|---|
| Shared contract and setup | [Component guide](README.md), `src/Directory.Build.props`, `Directory.Packages.props` | Experimental upstream contracts, not new Toolkit document abstractions |
| Internal mapping helpers | `src/Shared/ProviderAdapterHelpers.cs`, `src/Shared/SectionHierarchy.cs` | Linked implementation reuse, not a public framework/package |
| PdfPig | [Guide](src/PdfPig/README.md), `src/PdfPig/PdfPigDocumentExtractionClient.cs`, `test/PdfPig.UnitTests` | Native text and measured PDF geometry; no OCR or rendering |
| Azure pair | [CU](src/AzureContentUnderstanding/README.md), [DI](src/AzureDocumentIntelligence/README.md), `test/AzureTestSupport` | Real SDKs over strict replay transports; mixed cell content and original observations |
| Mistral | [Guide](src/Mistral/README.md), `test/Mistral.UnitTests` | One native OCR client for direct and Foundry endpoints; detached JSON and borrowed HTTP ownership |
| Vision | [Guide](src/Vision/README.md), `test/Vision.UnitTests` | Original PDF/image bytes in one chat call; separate free-form and strict structured acceptance |
| Build and CI | `eng/Bootstrap-DocumentExtractionCore.ps1`, `eng/New-DocumentExtractionNuGetConfig.ps1`, `.github/workflows/document-extraction.yml` | Public pinned source bootstrap, then Windows/Linux offline acceptance |

Azure CU retains literal document Markdown when no structured items exist; it
does not parse or manufacture tables from that fallback. Vision derives table
dimensions solely from validated anchors/spans, preserves null versus empty
text, and rejects invalid/ambiguous structures without an extra inference call.
Returned data is non-disposable; caller streams and supplied SDK/HTTP/chat
clients stay borrowed. Native inspection is not an original-wire archive.

## Reproduce from a fresh checkout

The temporary core packages are **not claimed available on a public feed**.
The bootstrap builds these four original owning projects from public source:
AI Abstractions, Documents Abstractions, DocumentExtraction Abstractions and
DocumentExtraction. No shadow projects, copied production DLLs or handwritten
NuGet metadata are used.

| Prerequisite | Exact input |
|---|---|
| Repository | `https://github.com/luisquintanilla/extensions.git` |
| Commit | `c03481c76b63c6820e1de6ed9da09d5ff1e9f495` |
| Tree | `f2e9d521223db1cf33fa8bcec5667da26b92cc49` |
| Upstream source SDK | `10.0.401` |
| Core package version | `10.10.0-review.20261104.4` |

Install SDK 10.0.401 separately if needed and supply its executable explicitly;
do not change either repository's SDK pin. The Toolkit checkout uses its own
`global.json` and requires .NET 8 and .NET 10 runtimes for both test targets.
The scripts use PowerShell 7 and Git. Run from the Toolkit repository root:

```powershell
# Use the installed 10.0.401 host, not a copied build DLL.
$sourceDotnet = 'C:\path\to\dotnet-10.0.401\dotnet.exe'
$coreFeed = Join-Path $env:TEMP 'toolkit-extraction-core'
$config = Join-Path $env:TEMP 'toolkit-extraction-nuget.config'

./eng/Bootstrap-DocumentExtractionCore.ps1 -OutputDirectory $coreFeed -DotnetPath $sourceDotnet
./eng/New-DocumentExtractionNuGetConfig.ps1 -CorePackageDirectory $coreFeed -OutputPath $config
dotnet restore DocumentExtraction/DocumentExtraction.slnf --configfile $config
dotnet build DocumentExtraction/DocumentExtraction.slnf -c Release --no-restore
dotnet test DocumentExtraction/DocumentExtraction.slnf -c Release --no-build --no-restore
dotnet pack DocumentExtraction/DocumentExtraction.slnf -c Release --no-build --no-restore --output artifacts/document-extraction
```

The bootstrap verifies commit/tree, preserves normal owning-project imports and
uses the scoped `Features=` workaround for the pinned upstream source compiler.
Restore explicitly uses that source tree's checked-in public `NuGet.config`,
so ancestor/user package-source mappings cannot redirect its dependencies.
It validates package IDs, version, repository commit and all five upstream TFM
assembly assets. That prerequisite needs network access to public Git/NuGet.
The provider tests themselves use local PDF fixtures, strict HTTP/SDK replay
and recording chat clients: no credentials, live service/model calls, model
downloads or ONNX/native-rendering setup are needed.

CI packs the same pinned upstream projects in a Windows job, uploads the four
core archives as `document-extraction-core-source-packages`, then supplies that
same-run artifact to Windows and Linux restore/build/test/pack jobs. Five
provider packages are uploaded as review artifacts, not released to NuGet.org.
Artifacts expire; the pinned bootstrap is the reproducible prerequisite, not a
permanent public feed.

## Deliberately separate fast-follow

The **entire** advanced `PdfPig.OnnxLayoutAnalysis` companion is deferred,
including its initial Heron layout integration, local rendering stack, model
setup, OCR, reading order, table inference, tests and native dependencies.
It is not a sixth package or a hidden dependency in this review.

That future provider-specific options/builder and model provisioning belong
under Toolkit `DocumentExtraction`, not in the shared core. Their API design and
model-quality evaluation are separate work. Offline mapping/schema compliance
here does not establish OCR accuracy or faithful table recognition.

## Remaining review and release boundaries

The pinned public source is a reproducible experimental dependency, not formal
upstream API approval or a released core package. Review can proceed, but
merging/releasing must reconcile the final approved upstream contracts and
public dependency versions. No package publication or upstream merge is
authorized by these builds.

Provider service capability, endpoint policy, credentials, cost and recognition
quality require application-specific validation. Vision needs actual PDF file
support across adapter/API/model, not merely image support. Markdown fallbacks
remain literal; unknown source pages, geometry and cell content stay unknown.
