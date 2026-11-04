# CommunityToolkit.DocumentExtraction.AzureContentUnderstanding

Preview Azure AI Content Understanding adapter for `DocumentExtractionClient`,
targeting .NET 8 and .NET 10. This package depends on Documents and Extraction,
not DataIngestion, and has not been released to NuGet.org.

Construct `ContentUnderstandingClient` with an Azure endpoint and
`TokenCredential`, or with a borrowed `Azure.AI.ContentUnderstanding.ContentUnderstandingClient`.
The default analyzer is `prebuilt-document`; `ExtractorName` selects an analyzer
for one request. The only additional property is `processingLocation` (string).
Unsupported explicit options fail before sending a request.

SDK paragraphs, tables, cells, captions, and native section pointers are mapped
directly. Table spans, cell roles, source frames, and every ordered polygon vertex
are preserved. Native objects remain available through explicit source inspection.
Sections are structural, without aggregate Markdown. Paragraphs spanning pages
appear once with all reported sources; content without a reported source is not
assigned a fabricated page. The service reports an analyzer identity, not a model
identity; no model identity is inferred from the requested analyzer.

When a native cell supplies `Elements` references, its content is a structural
section of the referenced paragraphs, figures, tables, or sections in supplied
order. The same mapped children are removed from page siblings; redundant native
section membership is reconciled only within the owning table's ancestor chain.
Dangling, repeated, cyclic, or conflicting ownership fails explicitly. Aggregate
cell text remains available on the native cell, not duplicated in the structural
payload. Without child references, the existing nullable text mapping remains.
No child association is inferred from polygons or Markdown. Content Understanding
figure references retain reported descriptions and geometry; this adapter does
not fabricate image bytes or URIs. Offline synthetic schema replay demonstrates
mapping, not live recognition of images inside cells.

The caller owns its input and supplied SDK client. The result requires no
disposal. `document.RawRepresentation` exposes the native `AnalysisResult`;
source references retain native observations after response resources are released.
Figure descriptions map only to `AlternativeText`, not OCR `Text`.
There is no shared raw-response capture policy or retained wire artifact.

Acceptance uses the real SDK with an offline replay transport and fake credential.
No Azure resource or authentication is required to run those tests. See the
[component documentation](../../README.md). Licensed under the repository's
[MIT license](../../../LICENSE).
