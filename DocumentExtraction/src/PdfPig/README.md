# CommunityToolkit.DocumentExtraction.PdfPig

Preview native PDF text extraction for `IDocumentExtractionClient`, targeting
.NET 8 and .NET 10. This package depends on the experimental
`Microsoft.Extensions.Documents.Abstractions` and
`Microsoft.Extensions.DocumentExtraction.Abstractions` packages, not DataIngestion.
It has not been released to NuGet.org.

```csharp
using CommunityToolkit.DocumentExtraction.PdfPig;

using var extractor = new PdfPigDocumentExtractionClient();
using var input = File.OpenRead("document.pdf");
var document = await extractor.ExtractAsync(input, "document.pdf", "application/pdf");
```

Every actual PDF page appears in `SourcePages`, including empty pages. Text blocks retain
their native observations and four ordered bounding-box vertices in PDF points,
with a bottom-left origin. The supplied segmenter and word extractor are borrowed;
an optional `roleResolver` preserves custom roles and normalizes common headings
and page headers/footers. Structural documents and sections do not contain copies
of descendant text.

Only `application/pdf` is supported. `ExtractorName` can be absent or `pdfpig`;
other selectors and all additional properties are rejected. This provider does
not perform OCR, download models, or invoke ONNX. External segmenters can be
supplied without introducing an inference dependency into this package.

The caller owns its input stream. The returned document is data and requires no
disposal. `document.RawRepresentation` remains null because the parser is disposed;
independent native text blocks remain inspectable through their source references.

See the [component documentation](../../README.md) for dependency bootstrap,
offline acceptance, and source attribution. Licensed under the repository's
[MIT license](../../../LICENSE).
