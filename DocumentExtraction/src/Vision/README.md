# CommunityToolkit.DocumentExtraction.Vision

Preview document extraction using a vision-capable `IChatClient`, targeting
.NET 8 and .NET 10. This package depends on Documents, Extraction, and
Microsoft.Extensions.AI abstractions, not DataIngestion. It has not been released
to NuGet.org.

```csharp
using CommunityToolkit.DocumentExtraction.Vision;

using var extractor = new VisionLlmOcrClient(chatClient, useStructuredOutput: true);
var document = await extractor.ExtractAsync(input, "scan", "image/png");
```

Free-form mode produces a text element containing the model's response.
Structured mode requests JSON with a `pages` array, optional explicitly known
zero-based `index`, `markdown`, tables (`rowCount`, `columnCount`, `markdown` or
addressed `cells`), and figures (`caption`). Cells preserve their spans, roles,
and nullable text. Invalid shape, duplicate pages or cell anchors, contradictory
PDF indices, invalid confidence, and unsupported root properties fail explicitly.
Optional arrays may be omitted, but cannot be null; language must be a string or
null, matching the requested schema.
**Structured mode never falls back to free-form or sends a second inference
request on failure.**

Image source frames remain unnumbered unless the structured response explicitly
reports an original page index. PDF pages are rasterized internally with PDFium
and SkiaSharp, then sent individually as PNG; original PDF page numbers and
point-coordinate frames remain distinct from assembly order. There is no public
renderer or required model download.

`ExtractorName` becomes `ChatOptions.ModelId` for that request. Genuine
`ChatResponse.ModelId` is retained independently. Additional options are rejected.
The chat client and caller stream remain borrowed, including after adapter
disposal. Output documents are data; structural ancestors have no aggregate text.

Capture defaults to `Disabled`; when enabled it is `Unavailable`, because
`IChatClient` does not expose exact original response bytes. Native chat responses
remain available for explicit inspection; JSON/SDK serialization is not wire
capture. Offline acceptance uses fake chat clients and native PDF fixtures and
does not call a model, service, or token provider. See the
[component documentation](../../README.md). Licensed under the repository's
[MIT license](../../../LICENSE).
