# CommunityToolkit.DocumentExtraction.AzureContentUnderstanding

Preview Azure AI Content Understanding adapter for `IDocumentExtractionClient`,
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
identity, so `SourceOutput.ModelId` is not filled from the requested analyzer.

The caller owns its input and supplied SDK client. The result requires no
disposal. Available original final response bytes are copied before disposing the
response. Capture defaults to `Disabled`; enabled capture distinguishes exact
retained bytes (including an empty body) from `Unavailable`. SDK serialization
is never relabeled original wire content.

Acceptance uses the real SDK with an offline replay transport and fake credential.
No Azure resource or authentication is required to run those tests. See the
[component documentation](../../README.md). Licensed under the repository's
[MIT license](../../../LICENSE).
