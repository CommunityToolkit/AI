# CommunityToolkit.DocumentExtraction.AzureDocumentIntelligence

Preview Azure Document Intelligence adapter for `DocumentExtractionClient`,
targeting .NET 8 and .NET 10. This package depends on Documents and Extraction,
not DataIngestion, and has not been released to NuGet.org.

Construct `AzureDocumentIntelligenceClient` with an endpoint and `TokenCredential`,
or a borrowed `DocumentIntelligenceClient`. The default extractor is
`prebuilt-layout`. `ExtractorName` selects the request model; the genuine returned
`AnalyzeResult.ModelId` remains available on the native result through
`document.RawRepresentation`, independently of the request selector.

Supported additional properties are `locale` (string), `pages` (string), and
`features` (`IEnumerable<string>`). Supported feature names are
`ocrHighResolution`, `languages`, `barcodes`, `formulas`, `keyValuePairs`,
`fontStyling`, and `queryFields`, case-insensitively. Other explicit options fail.
Set the constructor's `includeImages` to fetch detached figure bytes; captions
and figure sources are still mapped when byte retrieval is disabled.

SDK paragraphs, native section pointers, tables, cells, roles, and all polygon
vertices map directly. Page units and origins are explicit. Structural ancestors
contain no aggregate text; multi-page elements retain all their sources without
duplicating their semantic content.

When a native cell supplies `Elements` references, its content is a structural
section of the referenced paragraphs, figures, tables, or sections in supplied
order. The same mapped children are removed from page siblings; redundant native
section membership is reconciled only within the owning table's ancestor chain.
Dangling, repeated, cyclic, or conflicting ownership fails explicitly. Aggregate
cell text remains available on the native cell, not duplicated in the structural
payload. Without child references, the existing nullable text mapping remains.
No child association is inferred from polygons or Markdown. Figure bytes are
retrieved only with `includeImages`; no original asset URI is fabricated. Offline
synthetic schema replay demonstrates mapping, not live recognition of images
inside cells.

Input streams and supplied SDK clients are borrowed. Final response and figure
resources are released after semantic figure bytes have been detached. Results
need no disposal. `document.RawRepresentation` exposes the native `AnalyzeResult`,
and source references retain native observations. Captions map only to
`AlternativeText`, not OCR `Text`. There is no shared raw-response capture policy
or retained wire artifact.

Acceptance runs the real SDK against an offline replay transport with fake
credentials, not Azure. See the [component documentation](../../README.md).
Licensed under the repository's [MIT license](../../../LICENSE).
