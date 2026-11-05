# CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis

Extract **native PDF text with locally detected headings, tables, pictures and
other layout regions**. The Heron ONNX model sees the actual rendered page;
PdfPig supplies the searchable text. Results use the provider-neutral
`ExtractionDocument` graph, with original page numbers and measured source
polygons. No document content is sent to a service.

This is an optional alternative to the lightweight
[`CommunityToolkit.DocumentExtraction.PdfPig`](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/src/PdfPig/README.md)
text-only provider. Installing the basic package does not install this package,
PDFium, ONNX Runtime or model weights.

## Install

```shell
dotnet add package CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis --prerelease
dotnet add package Microsoft.ML.OnnxRuntime --version 1.23.2
```

Targets **.NET 8 and .NET 10**. Runtime dependencies are brought in transitively:

- `PdfPig` **0.1.15**: native PDF words and document layout analysis.
- `Docnet.Core` **2.6.0**: PDFium page rasterization.
- `Microsoft.ML.OnnxRuntime.Managed` **1.23.2**: managed inference and tensor APIs,
  **without an ONNX native runtime**.
- `Microsoft.Extensions.DocumentExtraction.Abstractions`,
  `Microsoft.Extensions.Documents.Abstractions` and
  `Microsoft.Extensions.AI.Abstractions`: shared extraction and content types.

Use a process/platform supported by **both** PDFium's
[Docnet native assets](https://github.com/GowenGit/docnet/tree/v2.6.0) and
[ONNX Runtime](https://onnxruntime.ai/docs/install/).
Windows x64 and Linux x64 are the acceptance targets. Do not assume that a
managed `net8.0` or `net10.0` target guarantees native support for every RID,
Alpine/musl, ARM, single-file publishing or Native AOT. Missing native libraries
are errors, not a request to fall back to text-only extraction.

### Select the application runtime

The application, not this library, selects one compatible native distribution.
For CPU inference, use the `Microsoft.ML.OnnxRuntime` **1.23.2** installation
above. The model-path constructor creates a CPU-default session; it does not
enable CUDA merely because a GPU is present.

For CUDA, use **`Microsoft.ML.OnnxRuntime.Gpu` 1.23.2 instead of the CPU package**:

```shell
dotnet add package Microsoft.ML.OnnxRuntime.Gpu --version 1.23.2
```

Select one distribution in the application and its executable dependencies, not
both CPU and GPU native packages. Install a supported NVIDIA driver, CUDA 12.x
and cuDNN 9.x shared libraries, and use an OS/process architecture supported by
the GPU package and PDFium. Check the
[CUDA execution-provider compatibility requirements](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html#requirements)
for the exact runtime release. Package restore is not a GPU capability check.
CUDA session/provider setup errors propagate; the Toolkit does not catch them
and retry a CPU session. ONNX Runtime can itself assign unsupported operators
to CPU; selecting CUDA does not promise every operator runs on the GPU.

PDF parsing, PDFium rendering, pixel preprocessing and document mapping remain
CPU operations. Only model execution can use the configured execution provider.

For `MEDE0001` or `EXTEXP0019` compiler diagnostics, see
[experimental API opt-in](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md#experimental-api-diagnostics).

## Acquire a model explicitly

The package does **not** contain or automatically download model weights.
Use the Apache-2.0
[Docling Heron ONNX export](https://huggingface.co/docling-project/docling-layout-heron-onnx/tree/40bde044036bb181c130ddf6c51792187268748f),
revision `40bde044036bb181c130ddf6c51792187268748f`.
The following PowerShell commands explicitly download and verify that artifact:

```powershell
Invoke-WebRequest -Uri 'https://huggingface.co/docling-project/docling-layout-heron-onnx/resolve/40bde044036bb181c130ddf6c51792187268748f/model.onnx' -OutFile model.onnx
$expected = '59C81A3A2923042D85034FFC487F8F47E4854117E879AEF89B2B9F728FB4922A'
if ((Get-Item model.onnx).Length -ne 171220471 -or
    (Get-FileHash model.onnx -Algorithm SHA256).Hash -ne $expected) {
    throw 'Heron model size or SHA256 mismatch.'
}
$env:HERON_MODEL_PATH = (Resolve-Path model.onnx).Path
```

Keep the model in an application-controlled local path. Distribute it under its
own [model license](https://huggingface.co/docling-project/docling-layout-heron-onnx/blob/40bde044036bb181c130ddf6c51792187268748f/README.md)
and retain applicable notices when redistributing weights. Model loading can
take appreciable memory beyond the 171 MB file size.

This export requires `images` as RGB **uint8** `[1, 3, 640, 640]` and
`orig_target_sizes` as int64 `[1, 2]` in height/width order. It returns `labels`,
`boxes` and `scores` with 300 predictions. The provider composites the real page
onto white and performs exact-size bilinear resizing; the graph itself rescales
pixels by 255. A float `pixel_values` graph, different label set or different
export is not interchangeable. Incompatible tensors fail during construction.

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
using var extractor = new PdfPigOnnxDocumentExtractionClient(modelPath);
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
    modelPath,
    new OnnxLayoutOptions
    {
        ConfidenceThreshold = 0.6f,
        RenderDpi = 144,
        MaximumPagePixels = 16_000_000,
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

### Share a caller-owned inference session

Alternative lifetime setup for applications that already configure ONNX Runtime.
The supplied session must have the same tensor/label contract. This CPU example
uses the application-selected `Microsoft.ML.OnnxRuntime` package above.

```csharp
using Microsoft.ML.OnnxRuntime;

using var sessionOptions = new SessionOptions();
sessionOptions.IntraOpNumThreads = 2;
using var session = new InferenceSession(modelPath, sessionOptions);
using var borrowedSessionExtractor = new PdfPigOnnxDocumentExtractionClient(session);
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
using var cudaExtractor = new PdfPigOnnxDocumentExtractionClient(cudaSession);
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

- Accepts only `application/pdf`. This is **layout detection, not OCR**:
  scanned pages can have picture/table regions but no recognized text. Use an
  OCR-capable provider when text must be recognized from pixels.
- Each original page has a source frame, even when empty. Region polygons use
  PDF points, bottom-left origin, relative to the effective crop **after** page
  rotation. Coordinates are scaled from actual rendered pixel dimensions, not
  an assumed DPI. Do not apply the crop offset or rotation a second time.
  Non-default or indirect PDF `UserUnit` is unsupported and raises an error.
- A word belongs to at most one region: at least half of its axis-aligned area
  must overlap; the largest overlap wins, then higher confidence. Unassigned
  native words remain text blocks. Words centered outside the visible crop are
  excluded. Text grouping uses PdfPig's nearest-neighbour word extraction and
  Docstrum block segmentation; regions are ordered top-to-bottom, then
  left-to-right. This is a geometric heuristic, not learned multi-column
  reading order; rotated text can contain additional line breaks.
- `title` and `section_header` become text role `heading`; `page_header` and
  `page_footer` become `pageHeader` and `pageFooter`. Plain `text` has no role.
  Other text labels retain their Heron label. Native observations preserve the
  original label and confidence, including regions without native text.
- A table region has native text in its local `Content` and a **zero-sized cell
  grid**: the model does not reconstruct cells, spans or table structure.
  A picture has measured sources and any associated native words in `Text`;
  this package does not return a cropped image, image bytes, captions or OCR.
- Source references expose `PdfPigLayoutRegion` for model regions and PdfPig
  `TextBlock` for unmatched text. `document.RawRepresentation` is a detached
  `IReadOnlyList<PdfPigLayoutRegion>`, not an ONNX tensor/session or open PDF.
- `DocumentExtractionOptions.ExtractorName` must be absent or
  `pdfpig-onnx-heron`. Unsupported selectors/additional properties fail
  explicitly. Raw wire capture is unavailable because extraction is local;
  native region observations are independent of raw-content capture.

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
  preprocessing and mapping. In-flight ONNX runs receive a termination request.
  Synchronous native PDF parsing/rendering cannot be interrupted mid-call, so
  cancellation is not a hard wall-clock deadline.

## Documentation and feedback

- [Docling Heron model card](https://huggingface.co/docling-project/docling-layout-heron)
- [PdfPig document-layout analysis](https://github.com/UglyToad/PdfPig/wiki/Document-Layout-Analysis)
- [ONNX Runtime .NET API](https://onnxruntime.ai/docs/api/csharp/api/Microsoft.ML.OnnxRuntime.InferenceSession.html)
- [Provider comparison and shared result model](https://github.com/CommunityToolkit/AI/blob/main/DocumentExtraction/README.md)
- [Report a Toolkit issue](https://github.com/CommunityToolkit/AI/issues)

The Toolkit package is [MIT licensed](https://github.com/CommunityToolkit/AI/blob/main/LICENSE).
PdfPig, Docnet/PDFium, ONNX Runtime and model weights retain their respective
licenses and notices; model weights are not included in this package.
