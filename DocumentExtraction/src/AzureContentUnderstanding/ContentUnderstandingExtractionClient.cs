// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Extensions.Documents;

namespace CommunityToolkit.DocumentExtraction.AzureContentUnderstanding;

/// <summary>
/// A <see cref="DocumentExtractionClient"/> backed by Azure AI Content Understanding.
/// </summary>
public sealed class ContentUnderstandingExtractionClient : DocumentExtractionClient
{
    private const string ProviderName = "azure-content-understanding";

    /// <summary>The default Content Understanding analyzer.</summary>
    public const string DefaultAnalyzerId = "prebuilt-document";

    private readonly Azure.AI.ContentUnderstanding.ContentUnderstandingClient _client;
    private readonly string _analyzerId;

    /// <summary>Creates an adapter using the supplied Azure endpoint and credential.</summary>
    public ContentUnderstandingExtractionClient(
        Uri endpoint,
        TokenCredential credential,
        ContentUnderstandingExtractionOptions? options = null,
        ContentUnderstandingClientOptions? clientOptions = null)
        : this(new Azure.AI.ContentUnderstanding.ContentUnderstandingClient(
            endpoint, credential, clientOptions ?? new ContentUnderstandingClientOptions()), options)
    {
    }

    /// <summary>Creates an adapter around a borrowed SDK client.</summary>
    public ContentUnderstandingExtractionClient(
        Azure.AI.ContentUnderstanding.ContentUnderstandingClient client,
        ContentUnderstandingExtractionOptions? options = null)
    {
        Throw.IfNull(client);
        string analyzerId = options is null ? DefaultAnalyzerId : options.AnalyzerId;
        Throw.IfNullOrWhitespace(analyzerId);
        _client = client;
        _analyzerId = analyzerId;
    }

    /// <inheritdoc/>
    public override async Task<Microsoft.Extensions.Documents.ExtractionDocument> ExtractAsync(
        Stream document,
        string identifier,
        string mediaType,
        DocumentExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ProviderAdapterHelpers.ValidateInput(document, identifier, mediaType);
        DocumentExtractionOptions normalizedOptions = ProviderAdapterHelpers.NormalizeOptions(options);
        ProviderAdapterHelpers.RejectUnsupportedProperties(
            normalizedOptions,
            nameof(ContentUnderstandingExtractionClient),
            "processingLocation");
        string analyzerId = ProviderAdapterHelpers.ResolveExtractor(
            normalizedOptions,
            _analyzerId,
            nameof(ContentUnderstandingExtractionClient));
        ProcessingLocation? processingLocation = ParseProcessingLocation(
            ProviderAdapterHelpers.GetProperty<string>(normalizedOptions, "processingLocation"));

        using var buffer = new MemoryStream();
        await document.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        Operation<AnalysisResult> operation = await _client
            .AnalyzeBinaryAsync(
                WaitUntil.Completed,
                analyzerId,
                BinaryData.FromBytes(buffer.ToArray()),
                contentRange: null,
                contentType: mediaType,
                processingLocation,
                cancellationToken)
            .ConfigureAwait(false);
        AnalysisResult result = operation.Value;
        using Response rawResponse = operation.GetRawResponse();
        if (result.Contents.Any(content => content is not DocumentContent))
        {
            throw new NotSupportedException("This Content Understanding adapter supports document results only.");
        }
        var output = new Microsoft.Extensions.Documents.ExtractionDocument(identifier)
        {
            RawRepresentation = result,
        };
            DocumentContent[] documentContents = result.Contents.OfType<DocumentContent>().ToArray();
            var frames = new Dictionary<int, PageFrame>();
            foreach (DocumentContent content in documentContents)
            {
                ExtractionDocumentCoordinateUnit? unit = MapUnit(content.Unit);
                for (int pageIndex = 0; pageIndex < content.Pages.Count; pageIndex++)
                {
                    Azure.AI.ContentUnderstanding.DocumentPage page = content.Pages[pageIndex];
                    if (frames.ContainsKey(page.PageNumber))
                    {
                        throw new InvalidDataException(
                            $"Content Understanding returned duplicate page number {page.PageNumber}.");
                    }

                    (ExtractionDocumentSourcePage sourcePage, _) =
                        ProviderAdapterHelpers.CreatePageFrame(
                            identifier,
                            ProviderName,
                            page.PageNumber,
                            page.Width,
                            page.Height,
                            unit,
                            ExtractionDocumentCoordinateOrigin.TopLeft);
                    var section = new ExtractionDocumentSection();
                    section.Sources.Add(new ExtractionDocumentSourceReference(sourcePage, rawRepresentation: page));
                    frames.Add(page.PageNumber, new PageFrame(sourcePage, section));
                }
            }

            foreach (DocumentContent content in documentContents)
            {
                foreach (Azure.AI.ContentUnderstanding.DocumentPage page in content.Pages)
                {
                    ExtractionDocumentSection section = frames[page.PageNumber].Section;
                    foreach (DocumentBarcode barcode in page.Barcodes ?? [])
                    {
                        foreach (ExtractionDocumentSourceReference source in ParseSources(barcode.Source, frames, barcode))
                        {
                            section.Sources.Add(source);
                        }
                    }

                    foreach (DocumentFormula formula in page.Formulas ?? [])
                    {
                        foreach (ExtractionDocumentSourceReference source in ParseSources(formula.Source, frames, formula))
                        {
                            section.Sources.Add(source);
                        }
                    }
                }
            }

            foreach (PageFrame frame in frames.Values)
            {
                output.SourcePages.Add(frame.SourcePage);
                output.Sections.Add(frame.Section);
            }

            foreach (DocumentContent content in documentContents)
            {
                var elements = new Dictionary<string, ExtractionDocumentElement>(StringComparer.Ordinal);
                MapParagraphs(output, content, frames, elements);
                MapTables(output, content, frames, elements);
                MapFigures(output, content, frames, elements);
                OrderContent(output, content, elements);
                if (content.Paragraphs.Count == 0)
                {
                    foreach (var page in content.Pages)
                    {
                        foreach (DocumentLine line in page.Lines)
                        {
                            IReadOnlyList<ExtractionDocumentSourceReference> sources = ParseSources(line.Source, frames, line);
                            if (sources.Count == 0)
                            {
                                sources = [new ExtractionDocumentSourceReference(frames[page.PageNumber].SourcePage,
                                    rawRepresentation: line)];
                            }

                            ProviderAdapterHelpers.AddElement(frames[page.PageNumber].Section,
                                new ExtractionDocumentTextElement(new TextContent(line.Content)), sources);
                        }
                    }
                }
                MapSections(output, content, frames, elements);
                if (elements.Count == 0 && !content.Pages.Any(page => page.Lines.Count > 0) &&
                    !string.IsNullOrEmpty(content.Markdown))
                {
                    // Whole-document Markdown has no measured page-local text boundaries.
                    var section = new ExtractionDocumentSection();
                    foreach (var page in content.Pages)
                    {
                        section.Sources.Add(new ExtractionDocumentSourceReference(
                            frames[page.PageNumber].SourcePage, rawRepresentation: content));
                    }

                    ProviderAdapterHelpers.AddElement(section,
                        new ExtractionDocumentTextElement(new TextContent(content.Markdown)), section.Sources);
                    output.Sections.Add(section);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return output;
    }

    private static void MapParagraphs(
        ExtractionDocument output,
        DocumentContent content,
        IReadOnlyDictionary<int, PageFrame> frames,
        Dictionary<string, ExtractionDocumentElement> elements)
    {
        foreach (var (paragraph, index) in (content.Paragraphs ?? []).Select((value, index) => (value, index)))
        {
            IReadOnlyList<ExtractionDocumentSourceReference> regions = ParseSources(paragraph.Source, frames, paragraph);
            string text = paragraph.Content ?? string.Empty;
            var element = new ExtractionDocumentTextElement(new TextContent(text), role: ToTextRole(paragraph.Role));
            elements.Add($"/paragraphs/{index}", element);
            ProviderAdapterHelpers.AddElement(ProviderAdapterHelpers.GetSection(output, regions), element, regions);
        }
    }

    private static void MapTables(
        ExtractionDocument output,
        DocumentContent content,
        IReadOnlyDictionary<int, PageFrame> frames,
        Dictionary<string, ExtractionDocumentElement> elements)
    {
        foreach (var (table, index) in (content.Tables ?? []).Select((value, index) => (value, index)))
        {
            IReadOnlyList<ExtractionDocumentSourceReference> tableSources = ParseSources(table.Source, frames, table);
            var mappedTable = new ExtractionDocumentTableElement(null, table.RowCount, table.ColumnCount);
            elements.Add($"/tables/{index}", mappedTable);
            foreach (Azure.AI.ContentUnderstanding.DocumentTableCell cell in table.Cells)
            {
                IReadOnlyList<ExtractionDocumentSourceReference> cellSources = ParseSources(cell.Source, frames, cell);
                ExtractionDocumentElement? cellContent = cell.Elements.Count > 0
                    ? new ExtractionDocumentSection()
                    : cell.Content is { } text ? new ExtractionDocumentTextElement(new TextContent(text)) : null;
                foreach (ExtractionDocumentSourceReference source in cellSources)
                {
                    cellContent?.Sources.Add(source);
                }

                var mappedCell = cellContent is null
                    ? new Microsoft.Extensions.Documents.ExtractionDocumentTableCell(
                        cell.RowSpan ?? 1, cell.ColumnSpan ?? 1, cell.Kind?.ToString())
                    : new Microsoft.Extensions.Documents.ExtractionDocumentTableCell(
                        cellContent, cell.RowSpan ?? 1, cell.ColumnSpan ?? 1, cell.Kind?.ToString());
                foreach (ExtractionDocumentSourceReference source in cellSources)
                {
                    mappedCell.Sources.Add(source);
                }

                mappedTable.Cells[cell.RowIndex, cell.ColumnIndex] = mappedCell;
            }
            ProviderAdapterHelpers.AddElement(ProviderAdapterHelpers.GetSection(output, tableSources), mappedTable, tableSources);
        }
    }

    private static void MapFigures(
        ExtractionDocument output,
        DocumentContent content,
        IReadOnlyDictionary<int, PageFrame> frames,
        Dictionary<string, ExtractionDocumentElement> elements)
    {
        foreach (var (figure, index) in (content.Figures ?? []).Select((value, index) => (value, index)))
        {
            IReadOnlyList<ExtractionDocumentSourceReference> sources = ParseSources(figure.Source, frames, figure);
            var element = new ExtractionDocumentImageElement()
            {
                AlternativeText = figure.Description,
            };
            elements.Add($"/figures/{index}", element);
            ProviderAdapterHelpers.AddElement(ProviderAdapterHelpers.GetSection(output, sources), element, sources);
        }
    }

    private static void MapSections(
        ExtractionDocument output, DocumentContent content, IReadOnlyDictionary<int, PageFrame> frames,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements)
    {
        var sections = new List<(ExtractionDocumentSection, IReadOnlyList<string>)>();
        foreach (Azure.AI.ContentUnderstanding.DocumentSection native in content.Sections ?? [])
        {
            var section = new ExtractionDocumentSection();
            foreach (var page in content.Pages)
            {
                if (native.Span is { } span && page.Spans.Any(pageSpan =>
                    pageSpan.Offset < span.Offset + span.Length && span.Offset < pageSpan.Offset + pageSpan.Length))
                {
                    section.Sources.Add(new ExtractionDocumentSourceReference(frames[page.PageNumber].SourcePage,
                        rawRepresentation: native));
                }
            }

            sections.Add((section, native.Elements.ToArray()));
        }

        var cells = new List<SectionHierarchy.CellContent>();
        for (int index = 0; index < content.Tables.Count; index++)
        {
            var table = (ExtractionDocumentTableElement)elements[$"/tables/{index}"];
            foreach (Azure.AI.ContentUnderstanding.DocumentTableCell cell in content.Tables[index].Cells)
            {
                if (cell.Elements.Count > 0)
                {
                    cells.Add(new SectionHierarchy.CellContent(
                        table, table.Cells[cell.RowIndex, cell.ColumnIndex]!, cell.Elements.ToArray()));
                }
            }
        }

        SectionHierarchy.Apply(output, elements, sections, cells);
    }

    private static void OrderContent(
        ExtractionDocument output, DocumentContent content,
        IReadOnlyDictionary<string, ExtractionDocumentElement> elements)
    {
        var offsets = new Dictionary<ExtractionDocumentElement, int>();
        for (int index = 0; index < content.Paragraphs.Count; index++)
        {
            offsets.Add(elements[$"/paragraphs/{index}"], content.Paragraphs[index].Span?.Offset ?? int.MaxValue);
        }

        for (int index = 0; index < content.Tables.Count; index++)
        {
            offsets.Add(elements[$"/tables/{index}"], content.Tables[index].Span?.Offset ?? int.MaxValue);
        }

        for (int index = 0; index < content.Figures.Count; index++)
        {
            offsets.Add(elements[$"/figures/{index}"], content.Figures[index].Span?.Offset ?? int.MaxValue);
        }

        SectionHierarchy.Order(output, offsets);
    }

    private static IReadOnlyList<ExtractionDocumentSourceReference> ParseSources(
        string? source,
        IReadOnlyDictionary<int, PageFrame> frames,
        object nativeRepresentation)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        var sources = new List<ExtractionDocumentSourceReference>();
        foreach (DocumentSource parsed in DocumentSource.Parse(source!))
        {
            if (!frames.TryGetValue(parsed.PageNumber, out PageFrame? frame))
            {
                throw new InvalidDataException($"Content Understanding referenced unknown page {parsed.PageNumber}.");
            }

            ExtractionDocumentSourceReference? reference = ProviderAdapterHelpers.CreateSource(
                frame.SourcePage,
                parsed.Polygon,
                nativeRepresentation);
            if (reference is not null)
            {
                sources.Add(reference);
            }
        }

        return sources;
    }


    private static ProcessingLocation? ParseProcessingLocation(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? (ProcessingLocation?)null
            : (ProcessingLocation)value;

    private static ExtractionDocumentCoordinateUnit? MapUnit(Azure.AI.ContentUnderstanding.LengthUnit? unit)
        => unit?.ToString() switch
        {
            "pixel" or "Pixel" => ExtractionDocumentCoordinateUnit.Pixel,
            "inch" or "Inch" => ExtractionDocumentCoordinateUnit.Inch,
            null => null,
            string value => throw new NotSupportedException(
                $"Content Understanding coordinate unit '{value}' is not supported."),
        };

    internal static string? ToTextRole(SemanticRole? role)
        => ProviderAdapterHelpers.NormalizeTextRole(role?.ToString());

    private sealed record PageFrame(
        ExtractionDocumentSourcePage SourcePage,
        ExtractionDocumentSection Section);

    /// <inheritdoc/>
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        Throw.IfNull(serviceType);
        return serviceKey is null && serviceType == typeof(Azure.AI.ContentUnderstanding.ContentUnderstandingClient)
            ? _client : base.GetService(serviceType, serviceKey);
    }
}
