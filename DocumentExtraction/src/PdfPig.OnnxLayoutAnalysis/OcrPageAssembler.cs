// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal static class OcrPageAssembler
{
    internal static void ReplaceNativeContent(
        ExtractionDocumentSection section, ExtractionDocumentSourcePage frame,
        IReadOnlyList<PdfPigLayoutRegion> nativeRegions, IReadOnlyList<PdfPigOcrRegion> observations,
        bool useColumnReadingOrder, CancellationToken token)
    {
        var groups = nativeRegions.Select(_ => new List<PdfPigOcrRegion>()).ToArray();
        var unassigned = new List<PdfPigOcrRegion>();
        foreach (var observation in observations)
        {
            token.ThrowIfCancellationRequested();
            int best = Match(observation.Bounds, nativeRegions);
            (best < 0 ? unassigned : groups[best]).Add(observation);
        }

        var nodes = new List<(PdfRectangle Bounds, ExtractionDocumentElement Element)>();
        for (int index = 0; index < nativeRegions.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var region = nativeRegions[index];
            var lines = groups[index].Where(line => line.Text.Length != 0)
                .OrderByDescending(line => line.Bounds.Top).ThenBy(line => line.Bounds.Left).ToArray();
            string text = string.Join(Environment.NewLine, lines.Select(line => line.Text));
            ExtractionDocumentElement? element = region.Label switch
            {
                "picture" => new ExtractionDocumentImageElement { Text = text.Length == 0 ? null : text },
                "table" => new ExtractionDocumentTableElement(text.Length == 0 ? null : new TextContent(text), 0, 0),
                _ when text.Length != 0 => new ExtractionDocumentTextElement(new TextContent(text), role: LayoutAssembler.Role(region.Label)),
                _ => null,
            };
            if (element is null)
            {
                continue;
            }

            var bounds = region.Bounds;
            element.Sources.Add(new(frame,
                [new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom), new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Top)], region));
            foreach (var line in lines)
            {
                element.Sources.Add(Source(frame, line));
            }

            nodes.Add((bounds, element));
        }

        foreach (var line in unassigned.Where(line => line.Text.Length != 0))
        {
            token.ThrowIfCancellationRequested();
            var element = new ExtractionDocumentTextElement(new TextContent(line.Text));
            element.Sources.Add(Source(frame, line));
            nodes.Add((line.Bounds, element));
        }

        var ordered = useColumnReadingOrder ? PageReadingOrder.Order(nodes, token) :
            nodes.OrderByDescending(node => node.Bounds.Top).ThenBy(node => node.Bounds.Left).Select(node => node.Element);
        section.Elements.Clear();
        foreach (var element in ordered)
        {
            section.Elements.Add(element);
        }
    }

    internal static ExtractionDocumentSourceReference Source(ExtractionDocumentSourcePage frame, PdfPigOcrRegion region)
        => new(frame, region.Polygon.Select(point => new ExtractionDocumentPoint(point.X, point.Y)).ToArray(), region);

    internal static int Match(PdfRectangle bounds, IReadOnlyList<PdfPigLayoutRegion> regions)
    {
        int best = -1;
        double bestOverlap = 0.5;
        for (int index = 0; index < regions.Count; index++)
        {
            double overlap = Intersection(bounds, regions[index].Bounds) / (bounds.Width * bounds.Height);
            if (overlap >= 0.5 && (best < 0 || overlap > bestOverlap ||
                (overlap == bestOverlap && regions[index].Confidence > regions[best].Confidence)))
            {
                best = index;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    internal static double Intersection(PdfRectangle first, PdfRectangle second)
        => Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left)) *
           Math.Max(0, Math.Min(first.Top, second.Top) - Math.Max(first.Bottom, second.Bottom));
}
