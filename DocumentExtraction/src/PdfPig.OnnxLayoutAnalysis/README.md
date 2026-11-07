# CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis

Extract **native PDF text with locally detected headings, tables, pictures and
other layout regions**, with independently opt-in local OCR, table structure
and LTR reading order. Heron sees the actual rendered page; by default
PdfPig supplies the searchable text. Results use the provider-neutral
`ExtractionDocument` graph, with original page numbers and measured source
polygons. No document content is sent to a service.

For the pinned graph's correct non-square-page geometry, explicitly enable
`UseCorrectedLayoutCoordinates`. Its default is `false` solely to preserve
historical package output; the legacy height-width input order is not the
graph's correct coordinate contract.

This is an optional alternative to the lightweight
[`CommunityToolkit.DocumentExtraction.PdfPig`](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/src/PdfPig/README.md)
text-only provider. Installing the basic package does not install this package,
Skia rendering, ONNX Runtime or model weights.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis --prerelease
dotnet add package Microsoft.ML.OnnxRuntime --version 1.23.2
```

Targets **.NET 10 and .NET Standard 2.0**, with **.NET 10** as the supported
application/test runtime. Runtime dependencies are brought in transitively:

- `PdfPig` **0.1.15**: native PDF words and document layout analysis.
- `PdfPig.Rendering.Skia` **0.1.15.4**: PdfPig-based page rasterization, with
  `SkiaSharp` / `SkiaSharp.HarfBuzz` **3.119.1** and PDF image filters.
- `SkiaSharp.NativeAssets.Linux.NoDependencies` **3.119.1** and
  `HarfBuzzSharp.NativeAssets.Linux` **8.3.1.2**: Linux native assets; SkiaSharp
  and HarfBuzzSharp also bring Windows/macOS assets.
- `Microsoft.ML.OnnxRuntime.Managed` **1.23.2**: managed inference and tensor APIs,
  **without an ONNX native runtime**.
- `Clipper2` **1.5.4**, Boost-1.0: managed closed-polygon offsetting for optional
  DB text detection; no OpenCV or ImageSharp runtime is introduced.
- `Microsoft.Extensions.DocumentExtraction.Abstractions`,
  `Microsoft.Extensions.Documents.Abstractions` and
  `Microsoft.Extensions.AI.Abstractions`: shared extraction and content types.

Use a process/platform supported by **both**
[SkiaSharp's native assets](https://github.com/mono/SkiaSharp) and
[ONNX Runtime](https://onnxruntime.ai/docs/install/).
Windows x64 and Linux x64 are the acceptance targets. Do not assume that a
managed `netstandard2.0` or `net10.0` asset guarantees native support for every RID,
Alpine/musl, ARM, single-file publishing or Native AOT. Missing native libraries
are errors, not a request to fall back to text-only extraction.
The renderer still uses native Skia and HarfBuzz; it is not pure managed code.
The Linux Skia asset avoids a Fontconfig dependency, not libc/libm/libdl or all
system prerequisites. Install suitable fonts in minimal images. Missing embedded
glyph outlines can require system-font substitution, so glyph appearance may
differ between operating systems. PDF feature/image-decoder support is bounded
by the pinned renderer and filters, not a promise of universal PDF fidelity.

The .NET Standard asset broadens **managed API compatibility**, not the native
runtime support commitment. The companion gate runs the same deterministic
contracts against that actual assembly on .NET 10, separately from the modern
asset, and runs an actual packed-asset synthetic extraction on Windows and
Linux. This does not
certify .NET 8, .NET Framework, every .NET Standard implementation, other architectures,
GPU execution or recognition quality. The modern asset remains available;
NuGet normally selects the closest compatible one. There is no dedicated
`net8.0` asset or .NET 8 support profile; possible consumption of the portable
asset on an older runtime is not a support commitment.

### Select the application runtime

The application, not this library, selects one compatible native distribution.
For CPU inference, use the `Microsoft.ML.OnnxRuntime` **1.23.2** installation
above. Options-based construction creates CPU-default sessions unless explicit
`SessionOptions` are supplied; it does not enable CUDA merely because a GPU
is present.

For CUDA, use **`Microsoft.ML.OnnxRuntime.Gpu` 1.23.2 instead of the CPU package**:

```shell
dotnet add package Microsoft.ML.OnnxRuntime.Gpu --version 1.23.2
```

Select one distribution in the application and its executable dependencies, not
both CPU and GPU native packages. Install a supported NVIDIA driver, CUDA 12.x
and cuDNN 9.x shared libraries, and use an OS/process architecture supported by
the GPU package and SkiaSharp/HarfBuzz. Check the
[CUDA execution-provider compatibility requirements](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html#requirements)
for the exact runtime release. Package restore is not a GPU capability check.
CUDA session/provider setup errors propagate; the Toolkit does not catch them
and retry a CPU session. ONNX Runtime can itself assign unsupported operators
to CPU; selecting CUDA does not promise every operator runs on the GPU.

PDF parsing, Skia rendering, pixel preprocessing and document mapping remain
CPU operations. Only model execution can use the configured execution provider.

All Toolkit-owned APIs in this assembly are experimental under **`CTDE0001`**
on every target framework. This is separate from upstream `MEDE0001` and
`EXTEXP0019`; prerelease package versions alone do not provide that diagnostic.
Explicitly opt in only where the application accepts these preview contracts.
For scoped compiler opt-in, see
[experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Acquire a model explicitly

The package does **not** contain or automatically download model weights.
Use the Apache-2.0
[Docling Heron ONNX export](https://huggingface.co/docling-project/docling-layout-heron-onnx/tree/40bde044036bb181c130ddf6c51792187268748f),
revision `40bde044036bb181c130ddf6c51792187268748f`.
The checked-in PowerShell 7 helper and [`models.json`](models.json) describe
the four pinned artifacts for the full companion (308,482,462 bytes total).
From the repository root, explicitly provision them into an application-owned
directory, then verify without network or writes:

```powershell
./DocumentExtraction/src/PdfPig.OnnxLayoutAnalysis/Get-Models.ps1 -ModelsDirectory ./models
./DocumentExtraction/src/PdfPig.OnnxLayoutAnalysis/Get-Models.ps1 -ModelsDirectory ./models -VerifyOnly
$env:HERON_MODEL_PATH = (Resolve-Path ./models/heron.onnx).Path
```

The same helper/manifest are packed under `tools/`; they are not invoked by
construction, Build or extraction. It validates the complete manifest before
destination writes, reuses verified files, rejects invalid existing files,
downloads missing files to unique same-directory temporaries, checks size/hash
and uses non-overwriting atomic rename. Failure cleans only its own partial;
concurrent destinations are preserved. `-VerifyOnly` does not create a directory
or download. The helper provisions all four models; layout-only callers can
instead acquire just the above Heron artifact and verify its manifest identity.
No weights are included in source or NuGet.

Keep the model in an application-controlled local path. Distribute it under its
own [model license](https://huggingface.co/docling-project/docling-layout-heron-onnx/blob/40bde044036bb181c130ddf6c51792187268748f/README.md)
and retain applicable notices when redistributing weights. Model loading can
take appreciable memory beyond the 171 MB file size.

This export requires `images` as RGB **uint8** `[1, 3, 640, 640]` and
`orig_target_sizes` as int64 `[1, 2]` in **width/height order**. Its postprocessor
repeats these values directly as `[width, height, width, height]` to scale XYXY
boxes; there is no internal dimension reversal. It returns `labels`,
`boxes` and `scores` with 300 predictions. The provider composites the real page
onto white and performs exact-size bilinear resizing; the graph itself rescales
pixels by 255. A float `pixel_values` graph, different label set or different
export is not interchangeable. Incompatible tensors fail during construction.

`UseCorrectedLayoutCoordinates=true` supplies those actual raster width/height
values before inference, without post-hoc crop repair. This is recommended for
new use, particularly tables. The compatibility default `false` retains the old
height/width input and can shift or clip regions on non-square pages, omit
source rows from table crops and misassign text. This flag is independent of
OCR, table structure and reading order, and is captured at construction for
both model-path and borrowed-session clients. It changes no image pixels,
model weights, confidence thresholds or text authority. Crop/rotation display
frames still come from the actual rendered page.

## Examples

### Extract text and layout

Complete console example. Supply a searchable `document.pdf` and set
`HERON_MODEL_PATH` to the verified local model.

```csharp
using System;
using System.IO;
using System.Threading;
using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;

string modelPath = Environment.GetEnvironmentVariable("HERON_MODEL_PATH")
    ?? throw new InvalidOperationException("Set HERON_MODEL_PATH to model.onnx.");
using var extractor = new PdfPigOnnxDocumentExtractionClient(
    new OnnxLayoutOptions { LayoutModelPath = modelPath, UseCorrectedLayoutCoordinates = true });
using var input = File.OpenRead("document.pdf");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
ExtractionDocument document = await extractor.ExtractAsync(
    input, "document.pdf", "application/pdf", cancellationToken: timeout.Token);

foreach (var element in document.EnumerateContent())
{
    // Table-local text is included here; this provider does not invent cells.
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
    if (element is ExtractionDocumentImageElement image)
    {
        Console.WriteLine($"Picture text: {image.Text}");
    }
}
```

The caller owns the input stream; extraction leaves it open. Dispose the client
to release its owned ONNX session. The returned document and native word/region
observations are detached data and do not require disposal.

### Tune rendering and inspect native regions

Alternative construction, followed by the same `ExtractAsync` call as above.
All types below come from this package or its transitive dependencies.

```csharp
using System.Collections.Generic;

using var configuredExtractor = new PdfPigOnnxDocumentExtractionClient(
    new OnnxLayoutOptions
    {
        LayoutModelPath = modelPath,
        ConfidenceThreshold = 0.6f,
        RenderDpi = 144,
        MaximumPagePixels = 16_000_000,
        UseCorrectedLayoutCoordinates = true,
    });

using var configuredInput = File.OpenRead("document.pdf");
var configuredDocument = await configuredExtractor.ExtractAsync(
    configuredInput, "document.pdf", "application/pdf");

if (configuredDocument.RawRepresentation is IReadOnlyList<PdfPigLayoutRegion> regions)
{
    foreach (var region in regions)
    {
        Console.WriteLine($"Page {region.PageNumber}: {region.Label} ({region.Confidence:F2})");
        Console.WriteLine($"Bounds in display points: {region.Bounds}");
        foreach (var word in region.Words)
        {
            Console.WriteLine(word.Text);
        }
    }
}
```

Options are validated and copied at construction. Confidence must be within
`[0, 1]`; DPI within `[72, 300]`; the pixel limit must be positive and fit a BGRA
buffer. Defaults are confidence `0.5`, DPI `144` and 32 million pixels per page.
The pixel limit is checked before allocating the rendered bitmap. The whole
input PDF is buffered separately, so this is not a total-process memory limit.
Increasing DPI does not increase the model's fixed 640 x 640 input size.

### Opt in to column-aware reading order

`UseColumnReadingOrder` defaults to `false`, preserving the existing
top-to-bottom, then left-to-right element order. Enable it explicitly for
left-to-right documents with separated columns:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.Documents;

public static class ColumnReadingOrderExtraction
{
    public static async Task<ExtractionDocument> ExtractAsync(
        string modelPath, Stream input, CancellationToken cancellationToken = default)
    {
        using var extractor = new PdfPigOnnxDocumentExtractionClient(
            new OnnxLayoutOptions
            {
                LayoutModelPath = modelPath,
                UseColumnReadingOrder = true,
                UseCorrectedLayoutCoordinates = true,
            });
        return await extractor.ExtractAsync(
            input, "document.pdf", "application/pdf", cancellationToken: cancellationToken);
    }
}
```

This is a deterministic **geometric heuristic, not learned reading order**.
It orders existing page elements atomically, including pictures, tables,
captions, footnotes and unmatched native text. Vertical whitespace separates
columns, read left-to-right and top-to-bottom. Where spanning elements block a
column split, horizontal whitespace next to wider elements is preferred to
inter-line gaps, separating title/table bands before ordering their columns.
Gaps must exceed one PDF point. An isolated smaller bottom-margin element or
observed footer can remain outside the body columns. Where an atomic fragment
bridges columns, non-overlapping left/right rectangles with overlapping vertical
extent retain LTR precedence; vertically separated rectangles sharing horizontal
extent retain top-first precedence. Ambiguous/cyclic overlaps retain a stable
top-to-bottom/left-to-right order, with original assembly order breaking ties.
It does not reorder words inside an element, infer caption relationships,
reconstruct cells, perform OCR or download another model. Native region/word
observations and their order remain unchanged; only page element order changes.
The example separately enables the coordinate correction; that independent
flag changes Heron's input-size contract, not the ordering algorithm.

Opted-in ordering rejects more than **4,096 elements per page** or non-finite,
empty or invalid element bounds before partitioning. This fixed safety bound
limits ordering work, not total PDF parsing/rendering memory. Interleaved or
overlapping layouts, narrow/no gutters, non-rectangular text flow and text whose
intended order differs from display geometry can remain incorrectly ordered.
Right-to-left and vertical writing are not supported by this heuristic; enabling
the option does not detect a document's language or writing direction. Native
or model regions can already combine unrelated columns or fragment a phrase.
Ordering does not split, repair or recognize those atomic contents, and can
worsen full-document sequence even when individual column anchors improve.

### Opt in to the curated OCR and table pipeline

There is **one pinned optional stack**, not a model registry. Each capability
is independent: `Ocr=null`, `TableStructure=null` and
`UseColumnReadingOrder=false` are the defaults. Disabled capabilities do not
load models or run inference. A non-null group enables its capability and
must be valid; there is no second `Enabled` or `None` switch. Lightweight PdfPig is
unchanged. No additional orientation, formula, chemical-recognition or VLM
model is loaded for complex Heron region labels.

Use the explicit setup helper above; URLs, revisions, sizes, hashes and qualified
license/provenance notes are centralized in the manifest, not runtime download logic.

The optional loaders enforce these exact hashes and tensor/dictionary contracts.
PP-OCRv5 mobile detector/recognizer artifacts total about **21.5 MB**, not their
Paddle-format model-card sizes. The TATR v1.0 FP32 derivative is **115.8 MB**.
These file sizes do not bound session, activation, raster or process memory.
The general dictionary embedded in the external recognizer ONNX artifact's
`character` custom metadata (not the Toolkit assembly) is checked against the exact
18,383-entry official PP-OCRv5 dictionary, with CTC blank 0 and appended space.
The model is not silently replaced by an English-only or server recognizer.
English/public/synthetic diagnostics do not certify its advertised language
coverage. This selection is not a measured universal Pareto frontier.

Complete method using CPU by default, or explicit CUDA with the application
runtime prerequisites above:

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime;

public static class CuratedLocalExtraction
{
    public static async Task<ExtractionDocument> ExtractAsync(
        string layoutModelPath, string detectorPath, string recognizerPath,
        string tablePath, Stream input, bool useCuda = false,
        CancellationToken cancellationToken = default)
    {
        using var sessionOptions = new SessionOptions { IntraOpNumThreads = 8 };
        if (useCuda)
        {
            sessionOptions.AppendExecutionProvider_CUDA(0);
        }

        using var extractor = new PdfPigOnnxDocumentExtractionClient(
            new OnnxLayoutOptions
            {
                LayoutModelPath = layoutModelPath,
                SessionOptions = sessionOptions,
                Ocr = new()
                {
                    Mode = OnnxOcrMode.PagesWithoutNativeText,
                    DetectorModelPath = detectorPath,
                    RecognizerModelPath = recognizerPath,
                },
                TableStructure = new() { ModelPath = tablePath },
                UseColumnReadingOrder = true,
                UseCorrectedLayoutCoordinates = true,
                RenderDpi = 150,
            });
        return await extractor.ExtractAsync(
            input, "document.pdf", "application/pdf", cancellationToken: cancellationToken);
    }
}
```

### Fluent configuration, not a second middleware pipeline

The thin builder captures the same grouped options and constructs the same client:

```csharp
using var extractor = new PdfPigOnnxDocumentExtractionClientBuilder(
    new OnnxLayoutOptions
    {
        LayoutModelPath = Path.Combine(modelsDirectory, "heron.onnx"),
        UseCorrectedLayoutCoordinates = true,
    })
    .WithOcr(ocr =>
    {
        ocr.DetectorModelPath = Path.Combine(modelsDirectory, "ch_PP-OCRv5_det_mobile.onnx");
        ocr.RecognizerModelPath = Path.Combine(modelsDirectory, "ch_PP-OCRv5_rec_mobile.onnx");
        // The default policy; any visible native word suppresses OCR on that page.
        ocr.Mode = OnnxOcrMode.PagesWithoutNativeText;
    })
    .WithTableStructure(table =>
        table.ModelPath = Path.Combine(modelsDirectory, "tatr-v1.0-structure-fp32.onnx"))
    .WithColumnReadingOrder()
    .Build();
```

Callbacks run immediately, but do not validate or load models. `Build()` validates
the whole recipe and eagerly loads all enabled models. Constructor options,
nested groups and callback results are copied; later caller mutations and later
builder configuration cannot change an existing client. Each Build creates
independently owned sessions. The builder is not thread-safe. Registration order
does not reorder dependencies: render the page; select native/OCR authority and
run OCR when selected; detect Heron layout; assemble native regions and substitute
OCR semantic content when selected; infer tables inside Heron table regions;
apply the opted-in geometric reading order to assembled elements.
This is not generic enrichment middleware, a public raster/stage protocol or a
second executor. Runtime settings remain at the root and reading order is a scalar.

**Preview migration:** replace the historical `(heronPath, options)` overload with
`new PdfPigOnnxDocumentExtractionClient(new OnnxLayoutOptions { LayoutModelPath = ..., ... })`.
Replace flat `OcrMode`/detector/recognizer fields with `Ocr`; replace
`UseTableStructure`/table-path fields with `TableStructure`. Null groups disable;
non-null groups require all paths and a defined, nonzero OCR mode.
Missing/invalid layout configuration never selects lightweight PdfPig.
These are intentional experimental API breaks, not compatibility aliases.

The buildable [console sample](../../samples/PdfPigOnnx/Program.cs) uses this API:

```shell
dotnet run --project DocumentExtraction/samples/PdfPigOnnx -f net10.0 -- ./models document.pdf --ocr --tables --columns
```

For a local native-library and extraction-contract check **without curated
weights**, the same sample can create a tiny synthetic ONNX graph and PDF:

```shell
dotnet run --project DocumentExtraction/samples/PdfPigOnnx -f net10.0 -- --synthetic-smoke
```

It verifies the loaded assembly, rendered-page contract and retained native
literal, then deletes its own temporary graph. Its constant synthetic
predictions are not layout, OCR or table-quality acceptance.

`SessionOptions` is **borrowed only while the constructor creates sessions**;
the provider neither mutates nor disposes it. All sessions created by the
client use that explicit configuration. A supplied Heron `InferenceSession`
remains borrowed and unchanged; configure it separately and supply
`OnnxLayoutOptions.SessionOptions` for the newly owned optional-model sessions.
All owned sessions are disposed with the client. Configured options/path/policy
properties are captured at construction; no model is activated by a later
property change. There is no CPU retry after CUDA/setup/backend errors.
Native `SessionOptions` are not cloneable managed recipe data: keep the borrowed
instance alive and unmodified until construction completes, and through each
Build that uses it. No session reads it after construction. A borrowed layout
session must be supplied with `LayoutModelPath=null`; any non-null path, including
an empty one, is rejected rather than giving either source hidden precedence.
Construction failures dispose already-created owned sessions without disposing
borrowed sessions or runtime options.
ONNX Runtime may still assign unsupported nodes to CPU, and rendering,
pre/postprocessing and assembly remain CPU work.

**Text authority:** `PagesWithoutNativeText` recognizes the entire page only
when no non-whitespace native word is visible. A native banner is sufficient
to skip OCR: mixed image regions on otherwise native pages are intentionally
not covered. Use explicit `WholePage` for scanned/garbled-native cases where
OCR should replace native output. There is no quality heuristic or native/OCR
text merge. Original native words, including region misses, remain separately
available in `PdfPigPipelineObservation.NativePageWords`; OCR does not create
PdfPig `Word`, glyph, font or text-sequence objects.

**Recognition:** the full original raster feeds the DB detector, with max-side
960 and independent nearest-32 dimensions, BGR normalization, polygon scoring,
round polygon unclip and perspective rectification from the original raster.
The recognizer uses singleton BGR 48-high crops, normalized-zero right padding
and CTC collapse-before-blank removal, without applying softmax again.
`OcrRegions` retain source quadrilaterals, the raster-to-crop homography,
literal text and diagnostic confidence. CTC time positions are not word boxes.
Empty decoding means no recognized text, not a certified blank. Actual blank
false positives, space loss and recognition errors are possible. There is no
orientation model, automatic rotation/quality repair, curved-text or general
RTL/vertical recognition promise.

The selected CPU graph can return `0x3F800001` (1.0000001192092896), exactly
one float32 ULP above one at sigmoid saturation. Only this upper boundary value
is normalized to one for detector scoring and CTC probabilities. All negative
values, NaN, infinities and values two or more ULP above one still fail.
Original tensors are not mutated. `PdfPigPipelineObservation.OcrProbabilityAdjustments`
explicitly reports each affected page/stage/recognition-region tensor, adjustment
count, dimensions, first flattened index/raw value and original min/max. This
numerical handling does not change model/image thresholds or repair text.

Managed bilinear/cubic sampling, pixel-center contours, filled rectangle masks
and Clipper2 round offsets follow the selected processing contract, but are
**not bit-identical to OpenCV's fixed-point/SIMD operations**. Cubic rectification
uses replicated borders and 1/32-pixel phases. Treat crop/model quality as an
empirical result, not evidence that all preprocessing implementations coincide.

**Tables:** structure inference runs only on actual Heron `table` crops and
works with native words when OCR is off. OCR also works with tables disabled.
The pinned derivative's singleton RGB/ImageNet, shortest-800/longest-1000
processor has no pixel-mask input. Seven-class softmax includes no-object.
Raw queries/logits/normalized boxes remain available even if rejected.
Score-ranked row/column refinement, alignment and observed header/span regions
derive a bounded grid. The implementation deliberately retains rows/columns
without text instead of deleting them as the upstream content-pruning step
does. Cells are model estimates, not guaranteed recognition of the source.
Unobserved cells have null content; actual `"0"` remains text. Raw cell
`TextStatus` distinguishes unobserved, an actual empty decode and recognized
text; **none proves visual blankness**. An OCR line that cannot be uniquely
assigned stays atomic and unassigned, not split into invented words. Unassigned
text remains once outside the table, and structured table parents do not
duplicate their cell text. With no accepted rows/columns the original 0x0
region remains, with `HasStructure=false` and actual proposals attached;
this is an explicit no-structure observation, not an inference error fallback.
Malformed tensors/topology, missing files and backend failures throw.

Safety budgets are explicit, not silent truncation: at most 32 table regions
per page, 125 TATR queries, 16,384 text observations/positions per table and
65,536 combined table positions per response before core grid allocation.
Recognition width is capped at 4,096 normalized pixels; over-budget crops
fail rather than compressing/dropping text. Page raster and 4,096-element
ordering bounds still apply. Processing is sequential, not an unbounded crop
batch, but whole-PDF buffering, models and native allocations consume
additional memory. Measure E2E latency, memory and quality on your documents;
published Paddle/model-only or PubTables benchmarks are not .NET E2E results.
Optional OCR source/crop processing is capped at 32 million pixels even if a
larger native-only rendering limit is configured. DB decoding additionally
rejects more than 1,000 contours, 262,144 boundary edges, 16,384 points per
contour/offset, 2,048 hull vertices, 16 million cumulative hull-squared work
or 64 million cumulative scoring-mask pixels; it does not return a truncated
successful page after exceeding a budget.

Licensing: Paddle/RapidOCR code and published selected PP artifacts declare
Apache-2.0. Official Table Transformer code/base weights declare MIT; the
pinned Xenova derivative identifies that base/conversion but has no separate
license field or reconstructed exact export recipe. This is the accepted
declared v1.0 lineage, **not v1.1-All**, exact official-weight equivalence or
that model's five-pixel crop claim. Preserve applicable upstream grants and
notices; no training-data/legal assurance is implied. See the packed
`CuratedPipeline.NOTICES.txt` for references, limitations and license text.

### Share a caller-owned inference session

Alternative lifetime setup for applications that already configure ONNX Runtime.
The supplied session must have the same tensor/label contract. This CPU example
uses the application-selected `Microsoft.ML.OnnxRuntime` package above.

```csharp
using Microsoft.ML.OnnxRuntime;

using var sessionOptions = new SessionOptions();
sessionOptions.IntraOpNumThreads = 2;
using var session = new InferenceSession(modelPath, sessionOptions);
using var borrowedSessionExtractor = new PdfPigOnnxDocumentExtractionClient(
    session, new() { UseCorrectedLayoutCoordinates = true });
using var borrowedInput = File.OpenRead("document.pdf");
var borrowedResult = await borrowedSessionExtractor.ExtractAsync(
    borrowedInput, "document.pdf", "application/pdf");
Console.WriteLine($"Pages: {borrowedResult.SourcePages.Count}");
```

### Configure a caller-owned CUDA session

Alternative to the CPU session example. Use the GPU package and native
prerequisites above. `modelPath` is the same explicitly acquired Heron model;
no detector contract or preprocessing changes are needed.

```csharp
using Microsoft.ML.OnnxRuntime;

using var cudaOptions = SessionOptions.MakeSessionOptionWithCudaProvider(0);
using var cudaSession = new InferenceSession(modelPath, cudaOptions);
using var cudaExtractor = new PdfPigOnnxDocumentExtractionClient(
    cudaSession, new() { UseCorrectedLayoutCoordinates = true });
using var cudaInput = File.OpenRead("document.pdf");
var cudaDocument = await cudaExtractor.ExtractAsync(
    cudaInput, "document.pdf", "application/pdf");
Console.WriteLine($"Pages: {cudaDocument.SourcePages.Count}");
```

The extractor does not dispose a supplied session. Keep it alive until all
extractors using it are finished. Calls through one client are serialized at
inference/disposal; sharing a session across clients requires application-level
lifetime coordination. `GetService(typeof(InferenceSession))` returns the
unkeyed native session while the client is alive.
Do not dispose an owned session obtained through `GetService`; dispose its
extractor instead.

## Result semantics and limitations

- Accepts only `application/pdf`. **Default mode is native text plus layout**:
  scanned pages can have regions without text. OCR requires explicit policy
  and both local PP-OCRv5 artifacts, as above.
- Each original page has a source frame, even when empty. Region polygons use
  PDF points, bottom-left origin, relative to the effective crop **after** page
  rotation. Coordinates are scaled from actual rendered pixel dimensions, not
  an assumed DPI. Do not apply the crop offset or rotation a second time.
  Non-default or indirect PDF `UserUnit` is unsupported and raises an error.
- A word belongs to at most one region: at least half of its axis-aligned area
  must overlap; the largest overlap wins, then higher confidence. Unassigned
  native words remain text blocks. Words centered outside the visible crop are
  excluded. Text grouping uses PdfPig's nearest-neighbour word extraction and
  Docstrum block segmentation; by default, elements are ordered top-to-bottom,
  then left-to-right. `UseColumnReadingOrder` opts into the column-aware
  geometric heuristic above. Neither mode provides learned multi-column
  reading order; rotated text can contain additional line breaks.
- `title` and `section_header` become text role `heading`; `page_header` and
  `page_footer` become `pageHeader` and `pageFooter`. Plain `text` has no role.
  Other text labels retain their Heron label. Native observations preserve the
  original label and confidence, including regions without native text.
- By default a table region has native text in local `Content` and a **zero-sized
  cell grid**. Only the independent explicit TATR capability derives cells.
  A picture has measured sources and any associated native words in `Text`;
  this package does not return image bytes or invent caption relationships.
- Source references expose `PdfPigLayoutRegion` for model regions and PdfPig
  `TextBlock` for unmatched text. `document.RawRepresentation` is a detached
  `IReadOnlyList<PdfPigLayoutRegion>` with all enrichment disabled. Enabling
  OCR or table structure instead returns `PdfPigPipelineObservation`, containing
  original native regions/words, OCR measurements and table observations.
  Neither result retains an ONNX tensor/session or open PDF.
- `DocumentExtractionOptions.ExtractorName` must be absent or
  `pdfpig-onnx-heron`. Unsupported selectors/additional properties fail
  explicitly. Raw wire capture is unavailable because extraction is local;
  native region observations are independent of raw-content capture.

## Evidence boundary: inherited .7 is not table acceptance

The preserved historical .7 archive has SHA256
`687A55A5546E9745D802C389D227D08C8343B140B2EE46DAE02AD09E380A750B`.
Its bounded evidence is 91 checks plus 2 development warmups, with no
errors/retries and exact legacy-default parity. **Per backend**, it recorded
0/12 complete table shapes, 15/90 numeric bindings, 121/190 topology checks,
6/42 headers and 0 certified blanks. Compared with .6, topology improved but
numeric/header/literal facts regressed; that is not an overall quality
improvement or general table acceptance.

This fast-follow refines configuration and preserves the restored algorithms;
it does not retune recognition, repair proposals or claim new accuracy,
performance or corpus acceptance. New examples explicitly set
`UseCorrectedLayoutCoordinates=true`, while the historical default remains
false. Do not reinterpret legacy-parity evidence as corrected-coordinate
acceptance. Actual unmatched words, native/raw content, unobserved cells and
one-ULP probability diagnostics remain visible.

Deterministic tests use generated PDFs, synthetic tensors and tiny generated
ONNX contract graphs, not curated weights. The retained historical real-Heron
test is explicitly skipped in ordinary CI. The provisioning checks mock all
downloads. Neither compilation nor these checks certify real model quality.

## Troubleshooting

- **Model load/schema errors:** verify the exact file/hash and export above.
  Missing, invalid or incompatible models never trigger a download or a
  text-only fallback.
- **Native-library load errors:** check process architecture and both native
  runtime asset sets, including Linux shared-library prerequisites. The Managed
  dependency alone cannot run inference; add the CPU or GPU package in the
  application. CUDA also requires matching driver/CUDA/cuDNN installation.
- **Memory limits:** lower `RenderDpi` or process smaller PDFs; the model and
  input buffer also consume memory. Native parsing/rendering failures propagate.
- **Cancellation:** tokens are checked during input copy, between pages,
  rendering content operators, preprocessing and mapping. In-flight ONNX runs
  receive a termination request. Individual synchronous parsing, codec and native
  drawing calls cannot always be interrupted, so cancellation is not a hard
  wall-clock deadline.

## Documentation and feedback

- [Docling Heron model card](https://huggingface.co/docling-project/docling-layout-heron)
- [PdfPig document-layout analysis](https://github.com/UglyToad/PdfPig/wiki/Document-Layout-Analysis)
- [PdfPig Skia rendering](https://github.com/BobLd/PdfPig.Rendering.Skia)
- [ONNX Runtime .NET API](https://onnxruntime.ai/docs/api/csharp/api/Microsoft.ML.OnnxRuntime.InferenceSession.html)
- [Provider comparison and shared result model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

The Toolkit package is [MIT licensed](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
PdfPig and PdfPig.Rendering.Skia are Apache-2.0; the DCT/JBIG2 filter packages
are Apache-2.0 and the JPX filter is BSD-2-Clause. Embedded JPEG code also carries
MIT and Apache-2.0 notices. SkiaSharp/HarfBuzzSharp packages are MIT and their
native packages include `THIRD-PARTY-NOTICES.txt` for bundled font/codec libraries.
See `PdfRendering.NOTICES.txt` in this package and retain dependency licenses and
notices when redistributing. Also see `CuratedPipeline.NOTICES.txt` for the
optional OCR/table lineage and Boost-1.0 polygon dependency. ONNX Runtime and model weights retain their own
licenses; model weights are not included.
