# Inspect the installation and prepare local inference

## Match the consumer, not a checkout

Inspect the application's `.csproj`, any central package versions and lock file.
Run `dotnet list <project.csproj> package --include-transitive` and inspect the
restored `obj/project.assets.json`. Record the resolved provider version, runtime
TFM, selected `lib` asset, OS/RID and process architecture. These package assets
are `net8.0`, `net10.0` and `netstandard2.0`; ordinary NuGet selection chooses the
closest compatible asset. Standard support is managed compatibility, not a .NET
Framework, all-RID, Native AOT, single-file or Alpine/musl commitment.

To find this resolved package without assuming the current working directory,
use PowerShell 7 **after restore**, supplying the consumer project's actual path:

```powershell
$project = (Resolve-Path './YourApp.csproj').Path
$assetsPath = Join-Path (Split-Path $project) 'obj/project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
$id = 'CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis'
$matches = @($assets.libraries.Keys | Where-Object { $_ -like "$id/*" })
if ($matches.Count -ne 1) { throw 'Expected one resolved provider package.' }
$library = $assets.libraries[$matches[0]]
if ($library.type -ne 'package') { throw 'Expected an installed NuGet package.' }
$candidates = @($assets.packageFolders.Keys | ForEach-Object {
    $candidate = Join-Path $_ $library.path
    if (Test-Path -LiteralPath (Join-Path $candidate 'tools/models.json')) {
        $candidate
    }
})
if ($candidates.Count -ne 1) { throw 'Resolve the actual package folder explicitly.' }
$packageRoot = $candidates[0]
Write-Output "Using $($matches[0]) at $packageRoot"
$helper = Join-Path $packageRoot 'tools/Get-Models.ps1'
$manifestPath = Join-Path $packageRoot 'tools/models.json'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'Missing packed helper.' }
```

For customized `MSBuildProjectExtensionsPath`, use its actual assets-file path
instead of the default `obj` location. Multiple caches are not interchangeable:
resolve ambiguity explicitly. Read skills from the same package version being
consumed, not an arbitrary newest cache directory.

## Native runtime and preview opt-in

This provider brings `Microsoft.ML.OnnxRuntime.Managed` **1.23.2**, not a native
ORT distribution. After approval, choose **one** application runtime:

- CPU: `dotnet add <project.csproj> package Microsoft.ML.OnnxRuntime --version 1.23.2`.
- CUDA: use `Microsoft.ML.OnnxRuntime.Gpu` **1.23.2 instead**, not alongside CPU
  native packages. Verify supported NVIDIA driver, CUDA 12.x/cuDNN 9.x libraries
  and process architecture against the
  [ORT CUDA requirements](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html#requirements).

CPU-default `SessionOptions` do not enable a present GPU. CUDA requires explicit
provider configuration as in the extraction example. Toolkit setup errors
propagate without CPU retry; ORT may place unsupported operators on CPU.
PDF parsing, Skia rendering and pre/postprocessing stay on CPU.

SkiaSharp/HarfBuzz native libraries and fonts must also work in the process.
Linux NoDependencies Skia avoids Fontconfig, not all libc/system prerequisites.
Check actual runtime assets, shared-library dependencies and font availability;
successful restore or .NET Standard compilation alone is insufficient.

Provider-owned APIs carry assembly-level **CTDE0001** on every TFM. Upstream
**MEDE0001** and **EXTEXP0019** remain distinct diagnostics. Obtain explicit
acceptance of preview contracts, then use a narrowly scoped suppression around
the integration code (the complete example does this). Do not globally disable
warnings or treat a prerelease version as compiler opt-in.

## Approve and verify models

Read `$manifestPath` and the package-root `CuratedPipeline.NOTICES.txt` and
`PdfRendering.NOTICES.txt`. The manifest owns role, filename, URL/revision,
byte length, SHA256 and qualified license lineage. There are four artifacts,
308,482,462 bytes total: Heron layout, PP-OCRv5 mobile detector/recognizer and
TATR v1.0 FP32 structure derivative. Session/activation memory is additional.
The recognizer's checked dictionary is embedded in the external ONNX metadata;
there is no separate dictionary download.

**Ask before network acquisition.** The helper downloads all four, even for
layout-only use. After approval choose an application-owned directory:

```powershell
$modelsDirectory = [System.IO.Path]::GetFullPath('./models')
# Network/filesystem writes: run only after approval.
& $helper -ModelsDirectory $modelsDirectory
# Offline verification only: no downloads or directory creation.
& $helper -ModelsDirectory $modelsDirectory -VerifyOnly
```

The helper reuses verified files, rejects invalid existing files without
overwriting them, checks exact length/hash, then atomically installs its own
download. No restore/build/client constructor invokes it. Layout-only users
may instead acquire just the manifest's Layout artifact after approval and
check its exact length/hash themselves; `-VerifyOnly` expects all four files.
Do not silently replace pins, edit the manifest to accept other bytes or use
synthetic graphs as real-quality validation.

Heron must be the supported RGB uint8 `[1,3,640,640]` graph with int64 target
sizes in width/height order, the expected labels/boxes/scores and label set.
Arbitrary ONNX exports are not interchangeable. Optional OCR/table loaders
also enforce pinned identities/contracts. TATR derivative lineage does not
establish exact official export equivalence or a separate declared derivative
license; preserve the packed notices and review redistribution rights.
