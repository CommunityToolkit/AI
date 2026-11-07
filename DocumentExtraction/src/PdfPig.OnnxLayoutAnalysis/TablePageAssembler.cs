// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal static class TablePageAssembler
{
    internal static int ReserveGrid(PdfPigTableStructure structure, int remainingGridPositions)
    {
        long positions = (long)structure.RowCount * structure.ColumnCount;
        if (positions > remainingGridPositions)
        {
            throw new InvalidDataException($"Page {structure.HeronRegion.PageNumber}: table exceeds the 65536-position response grid budget.");
        }

        return remainingGridPositions - (int)positions;
    }

    internal static void Replace(
        ExtractionDocumentSection section, ExtractionDocumentSourcePage frame,
        PdfPigTableStructure observation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int index = -1;
        for (int i = 0; i < section.Elements.Count; i++)
        {
            if (section.Elements[i] is ExtractionDocumentTableElement &&
                section.Elements[i].Sources.Any(source => ReferenceEquals(source.GetRawRepresentation(), observation.HeronRegion)))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidDataException($"Page {observation.HeronRegion.PageNumber}: the actual Heron table element is missing.");
        }

        var tableSource = Source(frame, observation.HeronRegion.Bounds, observation);
        if (!observation.HasStructure)
        {
            section.Elements[index].Sources.Add(tableSource);
            return;
        }

        var table = new ExtractionDocumentTableElement(null, observation.RowCount, observation.ColumnCount);
        table.Sources.Add(tableSource);
        foreach (var cell in observation.Cells)
        {
            token.ThrowIfCancellationRequested();
            var content = string.IsNullOrEmpty(cell.Text) ? null : new ExtractionDocumentTextElement(new TextContent(cell.Text));
            var mapped = content is null
                ? new ExtractionDocumentTableCell(cell.RowSpan, cell.ColumnSpan, cell.Role)
                : new ExtractionDocumentTableCell(content, cell.RowSpan, cell.ColumnSpan, cell.Role);
            mapped.Sources.Add(Source(frame, cell.Bounds, cell));
            if (content is not null)
            {
                foreach (var actual in cell.Observations)
                {
                    content.Sources.Add(TokenSource(frame, actual));
                }
            }

            table.Cells[cell.RowIndex, cell.ColumnIndex] = mapped;
        }

        section.Elements[index] = table;
        var nonempty = observation.UnassignedText.Select((text, ordinal) => (text, ordinal)).Where(item => item.text.Length != 0).ToArray();
        if (nonempty.Length > 0)
        {
            var unmatched = new ExtractionDocumentTextElement(new TextContent(string.Join(" ", nonempty.Select(item => item.text))));
            foreach (var item in nonempty)
            {
                unmatched.Sources.Add(TokenSource(frame, observation.UnassignedObservations[item.ordinal]));
            }

            section.Elements.Insert(index + 1, unmatched);
        }
    }

    internal static void Order(ExtractionDocumentSection section, bool useColumnReadingOrder, CancellationToken token)
    {
        var nodes = section.Elements.Select(element =>
        {
            var points = element.Sources.SelectMany(source => source.Polygon).ToArray();
            if (points.Length == 0)
            {
                throw new InvalidDataException("An enriched page element has no source geometry.");
            }

            return (new PdfRectangle(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y)), element);
        }).ToArray();
        var ordered = useColumnReadingOrder ? PageReadingOrder.Order(nodes, token) :
            nodes.OrderByDescending(node => node.Item1.Top).ThenBy(node => node.Item1.Left).Select(node => node.element).ToArray();
        section.Elements.Clear();
        foreach (var element in ordered) section.Elements.Add(element);
    }

    private static ExtractionDocumentSourceReference TokenSource(ExtractionDocumentSourcePage frame, object observation)
        => observation switch
        {
            PdfPigOcrRegion ocr => OcrPageAssembler.Source(frame, ocr),
            Word word => new(frame,
                [
                    new(word.BoundingBox.BottomLeft.X, word.BoundingBox.BottomLeft.Y),
                    new(word.BoundingBox.BottomRight.X, word.BoundingBox.BottomRight.Y),
                    new(word.BoundingBox.TopRight.X, word.BoundingBox.TopRight.Y),
                    new(word.BoundingBox.TopLeft.X, word.BoundingBox.TopLeft.Y),
                ], word),
            _ => throw new InvalidDataException("Table text observation is neither a genuine native word nor an actual OCR region."),
        };

    private static ExtractionDocumentSourceReference Source(ExtractionDocumentSourcePage frame, PdfRectangle bounds, object observation)
        => new(frame,
            [new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom), new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Top)], observation);
}
