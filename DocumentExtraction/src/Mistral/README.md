# CommunityToolkit.DocumentExtraction.Mistral

Preview Mistral OCR on Azure AI Foundry for `DocumentExtractionClient`,
targeting .NET 8 and .NET 10. This package depends on Documents and Extraction,
not DataIngestion, and has not been released to NuGet.org.

Construct `FoundryMistralOcrClient` with an endpoint and `TokenCredential`.
The default extractor is `mistral-ocr-4-0`; `ExtractorName` overrides it for a
request. An optional `HttpClient` is borrowed; an internally created client is
disposed with the adapter. The request route is
`/providers/mistral/azure/ocr`. Production requests obtain a token for the Azure
Cognitive Services scope; tests use a fake credential and an in-memory HTTP handler.

Supported additional properties are `includeImages` (bool), `imageLimit` (int),
and `imageMinSize` (int). Limits must be non-negative. Only HTTP 503 is retried,
at most five attempts; cancellation and other failures propagate explicitly.

Zero-based reported page indices become one-based source numbers, without
reordering response pages. Response model identity is preserved only when
actually reported, never inferred from the request. Producer-supplied table
content stays in table-local `TextContent`, not ancestor aggregates. Unambiguous
table placeholders are replaced in reading order; ambiguous/unplaced tables remain
separate rather than deleting source text. Image bytes and JSON observations are
detached before HTTP responses/parsers are disposed.

`document.RawRepresentation` is a cloned root `JsonElement`, independent of the
disposed parser. Source observations are cloned as well. This is native JSON
inspection, not original wire-byte capture or a shared operation envelope.
Results require no disposal; caller input stays open.
See the [component documentation](../../README.md) for offline acceptance and
attribution. Licensed under the repository's [MIT license](../../../LICENSE).
