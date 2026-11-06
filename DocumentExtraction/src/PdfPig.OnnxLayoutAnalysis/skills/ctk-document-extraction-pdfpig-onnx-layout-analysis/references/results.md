# Check real output, then triage

## Acceptance is content, not completion

Compare representative approved PDFs with their actual `ExtractionDocument`:
source-page count/numbers, required literal text, reading sequence, table shape,
individual row/value/header/unit associations and overlay alignment. Use native,
scan and mixed-page cases relevant to the customer, not only a blank/generated
graph smoke test. Keep sensitive content out of receipts; record package/model
identities, selected asset/runtime/backend, settings, counts, errors and redacted
assertions. Ask before any costly, network or GPU execution.

This is an experimental bounded model pipeline, not a universally accurate
complete OCR/document engine. Historical 24-PDF curated checks recorded **0/12
complete table shapes, 15/90 numeric bindings, 121/190 topology checks, 6/42
headers and zero certified blanks per backend**. Better topology alone did not
establish improved overall recognition. Compilation, runtime success and numeric
literal presence do not certify tables or customer-document accuracy. Do not
present these development measurements as a blind benchmark, a language-coverage
guarantee or performance promise.

## Geometry and raw observations

Source geometry uses **PDF points, bottom-left origin**, in the effective crop
frame **after rotation**, scaled from the actual rendered pixel dimensions.
Keep original page numbers; do not apply crop offsets/rotation twice or treat
points as pixels. Non-default or indirect PDF `UserUnit` is unsupported and
throws. Check finite in-frame polygons against the PDF, including cropped,
rotated and non-square pages.

Explicit `UseCorrectedLayoutCoordinates=true` supplies width/height to the
pinned Heron graph. The compatibility default is false and retains historical
height/width input order; it can shift/clip non-square-page crops. Correction
does not repair recognition, alter thresholds or imply correct table cells.

With OCR/table groups null, `RawRepresentation` is an
`IReadOnlyList<PdfPigLayoutRegion>`. With either enabled it is
`PdfPigPipelineObservation`: `NativeRegions`, `NativePageWords`, `OcrRegions`,
`TableStructures` and `OcrProbabilityAdjustments`. Reading order alone does not
change the raw representation type. Native raw words remain observations, not
additional semantic text to merge into authoritative OCR.

`PagesWithoutNativeText` requires no visible non-whitespace native word on the
page; a mixed-page banner suppresses OCR for the entire page. `WholePage`
replaces semantic native text while retaining raw observations. There is no
automatic quality detector, region-by-region merge or garbled-text repair.
Empty OCR decoding is not a certified blank; CTC positions are not word boxes.
Raw OCR quads and raster-to-crop transforms support inspection, not proof of
recognition accuracy.

## Tables and order

Without table structure enabled, a detected table has local text and a
zero-sized grid. TATR runs only inside actual Heron table regions, independently
of OCR. Inspect `TableStructures`, proposals and `HasStructure`, not only cells.
No accepted rows/columns leaves an explicit 0x0 no-structure region with raw
proposals, not an inference-error fallback.

Null/unobserved cell content, actual empty decode and literal `"0"` differ.
`TextStatus` on raw cell observations does not prove a visually blank cell.
Unassignable OCR lines remain atomic/unassigned rather than fabricated words;
check whether text sits outside the structured table. Do not duplicate raw or
table-parent text when collecting semantic cell content.

Column ordering is an optional LTR geometric heuristic over atomic elements;
it cannot repair text regions spanning columns, infer captions, split words or
support RTL/vertical writing. Inspect the actual sequence on ambiguous layouts.

## Fail explicitly and diagnose the boundary

| Symptom | Check/action |
|---|---|
| CTDE0001 or upstream experimental diagnostic | Confirm preview acceptance and scope explicit opt-in to integration code; do not blanket-disable warnings. |
| Missing model, hash/schema/dictionary error | Check installed manifest, exact files/lengths/SHA256, complete enabled groups and OCR enum. Never substitute arbitrary graphs or silently disable a capability. |
| Native-library/provider load error | Check one CPU/GPU distribution, process architecture, Skia/HarfBuzz dependencies/fonts; for CUDA check driver/CUDA/cuDNN and explicit provider selection. Preserve the error, not a hidden CPU/text-only retry. |
| Scan yields regions but no text | Check whether OCR is null, page authority policy and actual decoding. Default native layout does not perform OCR. |
| Mixed page has missing scanned content | A visible native word intentionally suppresses missing-native OCR; discuss explicit WholePage replacement, not automatic merging. |
| Misaligned regions/table crops | Check corrected coordinates, actual crop/rotation/units, exact Heron contract and raw polygons before proposing model tuning. |
| Missing/incorrect cells or wrong reading order | Inspect detection/topology/text assignment and raw proposals; report quality failure separately from execution. No silent table repair or fabricated values. |
| Resource/cancellation failure | Respect reported budgets; reduce page DPI/input size with approval. Whole-PDF buffering, models and native allocations are additional to the page pixel cap. Cancellation is not a hard wall-clock deadline for every synchronous native call. |

Preserve bounded OCR probability adjustment observations: exactly one float32
ULP above one may be normalized, with raw-value diagnostics retained. This
numerical saturation policy is not text repair. Invalid tensors, malformed
topology and safety-budget violations must remain errors, not truncated
success-shaped results.
