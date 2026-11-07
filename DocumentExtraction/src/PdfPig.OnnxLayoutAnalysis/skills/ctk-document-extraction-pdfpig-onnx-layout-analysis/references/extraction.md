# Configure one pipeline and extract

This complete console entry point uses only the installed package and its
transitive managed dependencies. Add the approved CPU **or** GPU runtime first;
verify local models using [setup](setup.md). Arguments:
`<models-directory> <document.pdf> [--ocr-missing|--ocr-whole] [--tables] [--columns] [--cuda]`.
No flags means native text plus layout on CPU. The opt-in pragmas are local to
this integration file; remove them if preview use has not been approved.

```csharp
#pragma warning disable CTDE0001, MEDE0001, EXTEXP0019
using System;
using System.IO;
using System.Linq;
using System.Threading;
using CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using Microsoft.ML.OnnxRuntime;

if (args.Length < 2)
{
    throw new ArgumentException("Expected a models directory and a PDF path.");
}

string models = Path.GetFullPath(args[0]);
string pdfPath = Path.GetFullPath(args[1]);
string[] flags = args.Skip(2).ToArray();
string[] allowed = { "--ocr-missing", "--ocr-whole", "--tables", "--columns", "--cuda" };
if (flags.Any(flag => !allowed.Contains(flag)) ||
    (flags.Contains("--ocr-missing") && flags.Contains("--ocr-whole")))
{
    throw new ArgumentException("Unknown flag or conflicting OCR authority policies.");
}

using var runtime = new SessionOptions();
if (flags.Contains("--cuda"))
{
    runtime.AppendExecutionProvider_CUDA(0);
}

var builder = new PdfPigOnnxDocumentExtractionClientBuilder(new OnnxLayoutOptions
{
    LayoutModelPath = Path.Combine(models, "heron.onnx"),
    UseCorrectedLayoutCoordinates = true,
    RenderDpi = 144,
    MaximumPagePixels = 16_000_000,
    SessionOptions = runtime,
});
if (flags.Contains("--ocr-missing") || flags.Contains("--ocr-whole"))
{
    builder.WithOcr(ocr =>
    {
        ocr.DetectorModelPath = Path.Combine(models, "ch_PP-OCRv5_det_mobile.onnx");
        ocr.RecognizerModelPath = Path.Combine(models, "ch_PP-OCRv5_rec_mobile.onnx");
        ocr.Mode = flags.Contains("--ocr-whole")
            ? OnnxOcrMode.WholePage : OnnxOcrMode.PagesWithoutNativeText;
    });
}
if (flags.Contains("--tables"))
{
    builder.WithTableStructure(table =>
        table.ModelPath = Path.Combine(models, "tatr-v1.0-structure-fp32.onnx"));
}
if (flags.Contains("--columns"))
{
    builder.WithColumnReadingOrder();
}

using var client = builder.Build();
using var input = File.OpenRead(pdfPath);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
ExtractionDocument document = await client.ExtractAsync(
    input, Path.GetFileName(pdfPath), "application/pdf", cancellationToken: timeout.Token);

Console.WriteLine($"Source pages: {document.SourcePages.Count}");
foreach (var element in document.EnumerateContent())
{
    if (element.Content is TextContent text)
    {
        Console.WriteLine(text.Text);
    }
    if (element is ExtractionDocumentTableElement table)
    {
        foreach (var cell in table.Cells)
        {
            if (cell?.Content?.Content is TextContent cellText)
            {
                Console.WriteLine($"Cell [{cell.RowIndex},{cell.ColumnIndex}]: {cellText.Text}");
            }
        }
    }
}
if (document.RawRepresentation is PdfPigPipelineObservation observation)
{
    Console.WriteLine($"OCR regions: {observation.OcrRegions.Count}; " +
        $"table observations: {observation.TableStructures.Count}");
}
#pragma warning restore CTDE0001, MEDE0001, EXTEXP0019
```

Only print extracted content when authorized; use counts/redacted observations
instead for private PDFs. This example does not validate accuracy by printing.

## Direct options, snapshotting and ownership

Direct `new PdfPigOnnxDocumentExtractionClient(new OnnxLayoutOptions { ... })`
uses the same validated recipe. Place OCR paths/policy in `Ocr = new
OnnxOcrOptions { ... }` and table path in `TableStructure = new
OnnxTableStructureOptions { ... }`; null disables model loading/inference.
There are no redundant `Enabled` flags or OCR `None` mode. Missing layout path,
invalid paths/contracts or a non-null incomplete group fail at construction.

Builder callbacks run immediately without model loading; `Build()` validates
and creates owned sessions. Root/nested options are snapshotted. Subsequent
caller mutations or builder changes do not change existing clients; each build
owns independent sessions. Builder is not thread-safe. Registration order does
not reorder rendering, OCR authority, Heron layout, assembly, optional table
structure and final optional column ordering. This is not arbitrary middleware.

Dispose the client to release owned sessions, including optional OCR/table
sessions. The caller owns the input stream, which extraction leaves open.
Returned document/raw observations are detached and do not need disposal.
`SessionOptions` is borrowed, not cloned or disposed; keep it alive/unmodified
through construction and every build using it. It is not read afterward.

Advanced `new PdfPigOnnxDocumentExtractionClient(session, options)` borrows a
caller-created Heron `InferenceSession`. In that overload `LayoutModelPath`
must be **null** (even an empty string is contradictory). Keep the session alive
until all clients using it finish, and dispose it yourself. Root `SessionOptions`
only configures newly owned optional-model sessions; it cannot reconfigure the
borrowed layout session. Coordinate cross-client sharing/lifetime yourself.
Do not dispose an owned session returned by `GetService`; dispose its client.
