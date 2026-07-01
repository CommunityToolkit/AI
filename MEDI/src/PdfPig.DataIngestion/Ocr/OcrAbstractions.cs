#pragma warning disable CS1591 // vendored shape, doc comments not required
// VENDORED copy of the unpublished dotnet/extensions #7588 IOcrClient shape; delete and reference Microsoft.Extensions.DataIngestion.Abstractions when #7588 ships.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace CommunityToolkit.DocumentProcessing.PdfPig.DataIngestion;

/// <summary>Provider-neutral OCR / document-extraction capability (the missing MEAI sibling).</summary>
public interface IOcrClient
{
    Task<OcrResult> GetTextAsync(
        Stream document,
        string mediaType,
        OcrOptions? options = null,
        IProgress<OcrProgress>? progress = null,
        CancellationToken cancellationToken = default);

    object? GetService(Type serviceType, object? serviceKey = null);
}

public sealed class OcrResult
{
    public OcrResult(IReadOnlyList<OcrPage> pages) => Pages = pages;

    public IReadOnlyList<OcrPage> Pages { get; }
    public string Markdown => string.Join("\n\n", Pages.Select(p => p.Markdown));
    public string? OcrSource { get; init; }
    public string? ModelId { get; init; }
    public OcrUsage? Usage { get; init; }
    public object? RawRepresentation { get; init; }
    public AdditionalPropertiesDictionary? AdditionalProperties { get; init; }
}

public sealed class OcrPage
{
    public OcrPage(int index, string markdown)
    {
        Index = index;
        Markdown = markdown;
    }

    public int Index { get; }
    public string Markdown { get; }
    public IReadOnlyList<OcrBlock> Blocks { get; init; } = [];
    public IReadOnlyList<OcrTable> Tables { get; init; } = [];
    public double? Confidence { get; init; }
    public AdditionalPropertiesDictionary? AdditionalProperties { get; init; }
}

public sealed class OcrBlock
{
    public OcrBlock(string text) => Text = text;
    public string Text { get; }
    public string? Kind { get; init; }
    public OcrBoundingRegion? BoundingRegion { get; init; }
    public double? Confidence { get; init; }
}

// Structured table on the provider side. Providers span a spectrum: Mistral OCR returns a markdown/HTML
// string (no cells), Azure DI returns a structured cell grid. The type models both, so the consumer can
// map to IngestionDocumentTable(markdown, cells) faithfully (markdown always, cells when the provider has them).
public sealed class OcrTable
{
    public OcrTable(int rowCount, int columnCount, OcrTableCell[,]? cells, string markdownRepresentation)
    {
        RowCount = rowCount;
        ColumnCount = columnCount;
        Cells = cells;
        MarkdownRepresentation = markdownRepresentation;
    }

    public int RowCount { get; }
    public int ColumnCount { get; }
    public OcrTableCell[,]? Cells { get; }
    public string MarkdownRepresentation { get; }
    public OcrBoundingRegion? BoundingRegion { get; init; }
    public double? Confidence { get; init; }

    public string ToMarkdown() => MarkdownRepresentation;
}

public sealed class OcrTableCell
{
    public OcrTableCell(string text) => Text = text;
    public string Text { get; }
    public int RowIndex { get; init; }
    public int ColumnIndex { get; init; }
}

/// <summary>Shared, provider-neutral geometry primitive (clockwise polygon, faithfully carries DI quads).</summary>
public sealed class OcrBoundingRegion
{
    public OcrBoundingRegion(int pageNumber, IReadOnlyList<float> polygon)
    {
        PageNumber = pageNumber;
        Polygon = polygon;
    }

    public int PageNumber { get; }
    public IReadOnlyList<float> Polygon { get; }

    public static OcrBoundingRegion FromRectangle(int pageNumber, double left, double top, double right, double bottom)
        => new(pageNumber,
        [
            (float)left, (float)top,
            (float)right, (float)top,
            (float)right, (float)bottom,
            (float)left, (float)bottom,
        ]);

    /// <summary>Axis-aligned bounds, for the reader <c>BoundingBox.*</c> metadata keys.</summary>
    public (float Left, float Top, float Right, float Bottom) GetBounds()
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i + 1 < Polygon.Count; i += 2)
        {
            minX = Math.Min(minX, Polygon[i]);
            maxX = Math.Max(maxX, Polygon[i]);
            minY = Math.Min(minY, Polygon[i + 1]);
            maxY = Math.Max(maxY, Polygon[i + 1]);
        }
        return (minX, minY, maxX, maxY);
    }
}

public sealed class OcrProgress
{
    public int? PagesProcessed { get; init; }
    public int? TotalPages { get; init; }
    public string? Status { get; init; }
}

public sealed class OcrUsage
{
    public int? PagesProcessed { get; init; }
    public AdditionalPropertiesDictionary? AdditionalProperties { get; init; }
}

public sealed class OcrOptions
{
    public string? ModelId { get; set; }
    public bool IncludeImages { get; set; }
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }
}
