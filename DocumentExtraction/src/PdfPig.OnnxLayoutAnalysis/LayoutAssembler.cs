// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal static class LayoutAssembler
{
    internal static IReadOnlyList<PdfPigLayoutRegion> AddPage(
        ExtractionDocument output, Page page, IReadOnlyList<HeronDetection> detections, CancellationToken token)
    {
        var (frame, reference) = ProviderAdapterHelpers.CreatePageFrame(
            output.Identifier, "pdfpig-onnx-heron", page.Number, page.Width, page.Height,
            ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);
        output.SourcePages.Add(frame);
        var section = new ExtractionDocumentSection();
        section.Sources.Add(reference);
        output.Sections.Add(section);
        var assigned = detections.Select(_ => new List<Word>()).ToArray();
        var unassigned = new List<Word>();
        foreach (var word in page.GetWords(NearestNeighbourWordExtractor.Instance))
        {
            token.ThrowIfCancellationRequested();
            var box = AxisAligned(word.BoundingBox);
            if (string.IsNullOrWhiteSpace(word.Text) || box.Width <= 0 || box.Height <= 0 ||
                box.Centroid.X < 0 || box.Centroid.X > page.Width ||
                box.Centroid.Y < 0 || box.Centroid.Y > page.Height)
            {
                continue;
            }

            int best = -1;
            double bestOverlap = 0.5;
            for (int i = 0; i < detections.Count; i++)
            {
                double overlap = Intersection(box, detections[i].Bounds) / (box.Width * box.Height);
                if (overlap >= 0.5 && (best < 0 || overlap > bestOverlap ||
                    (overlap == bestOverlap && detections[i].Confidence > detections[best].Confidence)))
                {
                    best = i;
                    bestOverlap = overlap;
                }
            }

            (best < 0 ? unassigned : assigned[best]).Add(word);
        }

        var regions = new List<PdfPigLayoutRegion>();
        var elements = new List<(double Top, double Left, ExtractionDocumentElement Element)>();
        for (int i = 0; i < detections.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var detection = detections[i];
            var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(assigned[i]);
            Word[] orderedWords = blocks.SelectMany(block => block.TextLines).SelectMany(line => line.Words).ToArray();
            var observation = new PdfPigLayoutRegion(
                page.Number, detection.Label, detection.Confidence, detection.Bounds, Array.AsReadOnly(orderedWords));
            regions.Add(observation);
            string text = string.Join(Environment.NewLine, blocks.Select(block => block.Text));
            ExtractionDocumentElement? element = detection.Label switch
            {
                "picture" => new ExtractionDocumentImageElement { Text = string.IsNullOrEmpty(text) ? null : text },
                "table" => new ExtractionDocumentTableElement(string.IsNullOrEmpty(text) ? null : new TextContent(text), 0, 0),
                _ when !string.IsNullOrEmpty(text) => new ExtractionDocumentTextElement(new TextContent(text), role: Role(detection.Label)),
                _ => null,
            };
            if (element is not null)
            {
                element.Sources.Add(Source(frame, detection.Bounds, observation));
                elements.Add((detection.Bounds.Top, detection.Bounds.Left, element));
            }
        }

        // Model misses must not silently delete native text.
        foreach (var block in DocstrumBoundingBoxes.Instance.GetBlocks(unassigned))
        {
            token.ThrowIfCancellationRequested();
            var text = new ExtractionDocumentTextElement(new TextContent(block.Text));
            var bounds = AxisAligned(block.BoundingBox);
            text.Sources.Add(Source(frame, bounds, block));
            elements.Add((bounds.Top, bounds.Left, text));
        }

        foreach (var item in elements.OrderByDescending(item => item.Top).ThenBy(item => item.Left))
        {
            section.Elements.Add(item.Element);
        }

        return regions;
    }

    internal static string? Role(string label) => label switch
    {
        "title" or "section_header" => "heading",
        "page_header" => "pageHeader",
        "page_footer" => "pageFooter",
        "text" => null,
        _ => label,
    };

    internal static PdfRectangle AxisAligned(PdfRectangle rectangle)
    {
        PdfPoint[] corners = [rectangle.TopLeft, rectangle.TopRight, rectangle.BottomLeft, rectangle.BottomRight];
        return new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
    }

    private static double Intersection(PdfRectangle word, PdfRectangle region)
        => Math.Max(0, Math.Min(word.Right, region.Right) - Math.Max(word.Left, region.Left)) *
           Math.Max(0, Math.Min(word.Top, region.Top) - Math.Max(word.Bottom, region.Bottom));

    private static ExtractionDocumentSourceReference Source(ExtractionDocumentSourcePage page, PdfRectangle bounds, object native)
        => new(page,
            [
                new(bounds.Left, bounds.Bottom), new(bounds.Right, bounds.Bottom),
                new(bounds.Right, bounds.Top), new(bounds.Left, bounds.Top),
            ], native);
}
