// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal static class PageReadingOrder
{
    internal const int MaximumElements = 4096;
    private const double MinimumGap = 1;

    internal static IReadOnlyList<ExtractionDocumentElement> Order(
        IReadOnlyList<(PdfRectangle Bounds, ExtractionDocumentElement Element)> elements, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (elements.Count > MaximumElements)
        {
            throw new InvalidDataException($"Column reading order supports at most {MaximumElements} elements per page.");
        }

        var nodes = new Node[elements.Count];
        for (int i = 0; i < elements.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var (bounds, element) = elements[i];
            if (!OnnxCompatibility.IsFinite(bounds.Left) || !OnnxCompatibility.IsFinite(bounds.Right) ||
                !OnnxCompatibility.IsFinite(bounds.Bottom) || !OnnxCompatibility.IsFinite(bounds.Top) ||
                !OnnxCompatibility.IsFinite(bounds.Width) || !OnnxCompatibility.IsFinite(bounds.Height) ||
                bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidDataException($"Column reading order element [{i}] has invalid bounds.");
            }

            nodes[i] = new(bounds, element, i);
        }

        var result = new List<ExtractionDocumentElement>(nodes.Length);
        var pending = new Stack<Node[]>();
        if (nodes.Length > 0)
        {
            pending.Push(nodes);
        }

        while (pending.Count > 0)
        {
            Node[] group = pending.Pop();
            token.ThrowIfCancellationRequested();
            if (group.Length == 1)
            {
                result.Add(group[0].Element);
                continue;
            }

            Node[] sorted = group.OrderByDescending(node => node.Bounds.Top).ThenBy(node => node.Ordinal).ToArray();
            int cut = MarginCut(sorted, token);
            if (cut < 0)
            {
                sorted = group.OrderBy(node => node.Bounds.Left).ThenBy(node => node.Ordinal).ToArray();
                cut = VerticalCut(sorted, token);
            }

            if (cut < 0)
            {
                sorted = group.OrderByDescending(node => node.Bounds.Top)
                    .ThenByDescending(node => node.Bounds.Width).ThenBy(node => node.Ordinal).ToArray();
                cut = HorizontalCut(sorted, token);
            }

            if (cut >= 0)
            {
                pending.Push(sorted.Skip(cut).ToArray());
                pending.Push(sorted.Take(cut).ToArray());
            }
            else
            {
                foreach (var node in OverlappingOrder(group, token))
                {
                    result.Add(node.Element);
                }
            }
        }

        token.ThrowIfCancellationRequested();
        return result;
    }

    private static int MarginCut(Node[] nodes, CancellationToken token)
    {
        var last = nodes[^1];
        double bottom = double.MaxValue, top = double.MinValue;
        bool smaller = true;
        for (int i = 0; i < nodes.Length - 1; i++)
        {
            token.ThrowIfCancellationRequested();
            bottom = Math.Min(bottom, nodes[i].Bounds.Bottom);
            top = Math.Max(top, nodes[i].Bounds.Top);
            smaller &= last.Bounds.Height < nodes[i].Bounds.Height;
        }

        bool footer = last.Element is ExtractionDocumentTextElement { Role: "footnote" or "pageFooter" };
        double gap = bottom - last.Bounds.Top;
        // A short, isolated bottom margin is not part of the leftmost body column.
        return gap > MinimumGap && (footer || (smaller && gap > top - bottom)) ? nodes.Length - 1 : -1;
    }

    private static IEnumerable<Node> OverlappingOrder(Node[] nodes, CancellationToken token)
    {
        var incoming = new int[nodes.Length];
        var emitted = new bool[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            for (int j = 0; j < nodes.Length; j++)
            {
                if (Precedes(nodes[j].Bounds, nodes[i].Bounds))
                {
                    incoming[i]++;
                }
            }
        }

        var ready = new SortedSet<(double Top, double Left, int Ordinal, int Index)>();
        for (int i = 0; i < nodes.Length; i++)
        {
            if (incoming[i] == 0)
            {
                ready.Add((-nodes[i].Bounds.Top, nodes[i].Bounds.Left, nodes[i].Ordinal, i));
            }
        }

        while (ready.Count > 0)
        {
            var next = ready.Min;
            ready.Remove(next);
            int index = next.Index;
            token.ThrowIfCancellationRequested();
            emitted[index] = true;
            yield return nodes[index];
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!emitted[i] && Precedes(nodes[index].Bounds, nodes[i].Bounds) && --incoming[i] == 0)
                {
                    ready.Add((-nodes[i].Bounds.Top, nodes[i].Bounds.Left, nodes[i].Ordinal, i));
                }
            }
        }

        // Cyclic/overlapping evidence cannot establish an unambiguous reading order.
        foreach (var node in nodes.Where((_, index) => !emitted[index])
            .OrderByDescending(node => node.Bounds.Top).ThenBy(node => node.Bounds.Left).ThenBy(node => node.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            yield return node;
        }
    }

    private static bool Precedes(PdfRectangle first, PdfRectangle second)
        => (second.Left - first.Right > MinimumGap && Math.Min(first.Top, second.Top) > Math.Max(first.Bottom, second.Bottom)) ||
           (first.Bottom - second.Top > MinimumGap && Math.Min(first.Right, second.Right) > Math.Max(first.Left, second.Left));

    private static int VerticalCut(Node[] nodes, CancellationToken token)
    {
        double right = nodes[0].Bounds.Right;
        double largestGap = MinimumGap;
        int cut = -1;
        for (int i = 1; i < nodes.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            double gap = nodes[i].Bounds.Left - right;
            if (gap > largestGap)
            {
                largestGap = gap;
                cut = i;
            }

            right = Math.Max(right, nodes[i].Bounds.Right);
        }

        return cut;
    }

    private static int HorizontalCut(Node[] nodes, CancellationToken token)
    {
        double bottom = nodes[0].Bounds.Bottom;
        double boundaryWidth = nodes[0].Bounds.Width;
        double widestBoundary = 0;
        double largestGap = MinimumGap;
        int cut = -1;
        for (int i = 1; i < nodes.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            PdfRectangle bounds = nodes[i].Bounds;
            double gap = bottom - bounds.Top;
            double width = Math.Max(boundaryWidth, bounds.Width);
            // Separate spanning nodes before ordinary inter-line gaps inside columns.
            if (gap > MinimumGap && (width > widestBoundary || (width == widestBoundary && gap > largestGap)))
            {
                widestBoundary = width;
                largestGap = gap;
                cut = i;
            }

            if (bounds.Bottom < bottom)
            {
                bottom = bounds.Bottom;
                boundaryWidth = bounds.Width;
            }
            else if (bounds.Bottom == bottom)
            {
                boundaryWidth = Math.Max(boundaryWidth, bounds.Width);
            }
        }

        return cut;
    }

    private readonly record struct Node(PdfRectangle Bounds, ExtractionDocumentElement Element, int Ordinal);
}