// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class PageReadingOrderTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void InterleavedColumnsReadEntireLeftColumnBeforeNext(int columns)
    {
        var nodes = Enumerable.Range(0, 3).SelectMany(row => Enumerable.Range(0, columns)
            .Reverse().Select(column => Node($"{column}:{row}", column * 100, 300 - row * 50, 60, 10))).ToArray();

        var ordered = PageReadingOrder.Order(nodes, default);

        Assert.Equal(Enumerable.Range(0, columns).SelectMany(column => Enumerable.Range(0, 3)
            .Select(row => $"{column}:{row}")), ordered.Select(Text));
        AssertConserved(nodes, ordered);
    }

    [Fact]
    public void SpanningNodesDivideColumnBandsBeforeLargerLineGaps()
    {
        // Inter-line gaps are 90, while gaps adjacent to the spans are only 5.
        // A largest-horizontal-gap-first algorithm would incorrectly interleave rows.
        var nodes = new[]
        {
            Node("R2", 150, 195, 50, 10), Node("L3", 0, 150, 50, 10),
            Node("title", 0, 315, 200, 15),
            Node("R1", 150, 295, 50, 10), Node("L1", 0, 295, 50, 10),
            Node("table", 0, 170, 200, 20, new ExtractionDocumentTableElement(new TextContent("table"), 0, 0)),
            Node("L2", 0, 195, 50, 10), Node("R3", 150, 150, 50, 10),
            Node("R4", 150, 50, 50, 10), Node("L4", 0, 50, 50, 10),
            Node("footer", 0, 30, 200, 15),
        };

        var ordered = PageReadingOrder.Order(nodes, default);

        Assert.Equal(new[] { "title", "L1", "L2", "R1", "R2", "table", "L3", "L4", "R3", "R4", "footer" },
            ordered.Select(Text));
        Assert.Same(nodes[5].Element, ordered[5]);
        AssertConserved(nodes, ordered);
    }

    [Fact]
    public void PermutationsPreserveAtomicElementsPayloadsAndSources()
    {
        var image = new ExtractionDocumentImageElement { Text = "figure caption inside image" };
        var table = new ExtractionDocumentTableElement(new TextContent("table cells remain opaque"), 0, 0);
        var nodes = new[]
        {
            Node("heading", 0, 300, 240, 20),
            Node("figure", 0, 230, 80, 60, image),
            Node("caption", 0, 210, 80, 10),
            Node("footnote", 0, 150, 80, 10),
            Node("table", 150, 220, 90, 70, table),
            Node("right caption", 150, 200, 90, 10),
            Node("right footnote", 150, 140, 90, 10),
        };
        var (frame, _) = ProviderAdapterHelpers.CreatePageFrame("identity", "fixture", 1, 240, 320,
            ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);
        foreach (var node in nodes)
        {
            node.Element.Sources.Add(new ExtractionDocumentSourceReference(frame,
                [new(node.Bounds.Left, node.Bounds.Bottom), new(node.Bounds.Right, node.Bounds.Bottom),
                    new(node.Bounds.Right, node.Bounds.Top), new(node.Bounds.Left, node.Bounds.Top)], new object()));
        }

        var contents = nodes.Select(node => node.Element.Content).ToArray();
        var sources = nodes.Select(node => node.Element.Sources.Single()).ToArray();
        var natives = sources.Select(source => source.GetRawRepresentation()).ToArray();
        var polygons = sources.Select(source => source.Polygon.Select(point => (point.X, point.Y)).ToArray()).ToArray();
        var random = new Random(7103);
        for (int permutation = 0; permutation < 24; permutation++)
        {
            var input = nodes.OrderBy(_ => random.Next()).ToArray();
            var before = input.ToArray();
            var ordered = PageReadingOrder.Order(input, default);

            Assert.Equal(nodes.Select(node => node.Element), ordered);
            Assert.Equal(before, input);
            AssertConserved(input, ordered);
            for (int i = 0; i < nodes.Length; i++)
            {
                Assert.Same(contents[i], ordered[i].Content);
                Assert.Same(sources[i], ordered[i].Sources.Single());
                Assert.Same(natives[i], ordered[i].Sources.Single().GetRawRepresentation());
                Assert.Equal(polygons[i], ordered[i].Sources.Single().Polygon.Select(point => (point.X, point.Y)));
                if (ordered[i] is ExtractionDocumentTextElement text)
                {
                    Assert.Equal(Text(text), text.Role);
                }
            }

            Assert.Equal("figure caption inside image", Assert.IsType<ExtractionDocumentImageElement>(ordered[1]).Text);
            Assert.Equal("table cells remain opaque", Text(ordered[4]));
            Assert.Equal(0, table.Cells.RowCount);
            Assert.Equal(0, table.Cells.ColumnCount);
        }
    }

    [Fact]
    public void OverlappingGeometryUsesTopLeftThenStableOriginalOrdinal()
    {
        var nodes = new[]
        {
            Node("same A", 0, 50, 100, 100), Node("right", 20, 50, 100, 100),
            Node("lower", 0, 40, 100, 100), Node("same B", 0, 50, 100, 100),
            Node("higher", 10, 60, 100, 100),
        };
        var expected = new[] { "higher", "same A", "same B", "right", "lower" };

        Assert.Equal(expected, PageReadingOrder.Order(nodes, default).Select(Text));
        Assert.Equal(expected, PageReadingOrder.Order(nodes, default).Select(Text));
        (nodes[0], nodes[3]) = (nodes[3], nodes[0]);
        Assert.Equal(new[] { "higher", "same B", "same A", "right", "lower" },
            PageReadingOrder.Order(nodes, default).Select(Text));
    }

    [Theory]
    [InlineData(1, "right", "left")]
    [InlineData(1.001, "left", "right")]
    public void VerticalGutterMustExceedOnePoint(double gap, string first, string second)
    {
        var nodes = new[] { Node("right", 10 + gap, 5, 10, 10), Node("left", 0, 0, 10, 10) };
        Assert.Equal(new[] { first, second }, PageReadingOrder.Order(nodes, default).Select(Text));
    }

    [Fact]
    public void OverlappingBridgeDoesNotCreateFalseWhitespaceGutters()
    {
        var nodes = new[]
        {
            Node("left", 0, 90, 20, 10), Node("bridge", 0, 20, 200, 110),
            Node("middle", 50, 110, 20, 10), Node("right", 150, 130, 20, 10),
        };
        Assert.Equal(new[] { "right", "bridge", "middle", "left" }, PageReadingOrder.Order(nodes, default).Select(Text));
        AssertConserved(nodes, PageReadingOrder.Order(nodes, default));
    }

    [Theory]
    [InlineData(1, "L1,R1,L2,R2,span")]
    [InlineData(1.001, "L1,L2,R1,R2,span")]
    public void HorizontalGutterMustExceedOnePointBeforeSpanningBoundary(double gap, string expected)
    {
        var nodes = new[]
        {
            Node("R1", 150, 100, 50, 10), Node("L2", 0, 80, 50, 10),
            Node("L1", 0, 100, 50, 10), Node("R2", 150, 80, 50, 10),
            Node("span", 0, 50, 200, 30 - gap),
        };
        Assert.Equal(expected.Split(','), PageReadingOrder.Order(nodes, default).Select(Text));
    }

    [Fact]
    public void EmptyAndSingletonPreserveIdentity()
    {
        Assert.Empty(PageReadingOrder.Order([], default));
        var node = Node("only", 10, 20, 30, 40);
        Assert.Same(node.Element, Assert.Single(PageReadingOrder.Order([node], default)));
        Assert.Equal("only", Text(node.Element));
    }

    [Fact]
    public void MaximumElementCountIsAccepted()
    {
        var nodes = Enumerable.Range(0, 4096).Select(i => Node($"node-{i}", 0, 0, 10, 10)).ToArray();
        var ordered = PageReadingOrder.Order(nodes, default);
        Assert.Equal(nodes.Select(node => node.Element), ordered);
        AssertConserved(nodes, ordered);
    }

    [Fact]
    public void ExcessElementCountRejectsBeforeReadingItems()
    {
        var input = new ObservedList(4097, _ => throw new InvalidOperationException("Indexer must not be used"));
        var error = Assert.Throws<InvalidDataException>(() => PageReadingOrder.Order(input, default));
        Assert.Contains("4096", error.Message);
        Assert.Equal(0, input.Reads);
    }

    public static IEnumerable<object[]> InvalidBounds()
    {
        yield return [new PdfRectangle(0, 0, 0, 10)];
        yield return [new PdfRectangle(0, 0, 10, 0)];
        yield return [new PdfRectangle(double.NaN, 0, 10, 10)];
        yield return [new PdfRectangle(0, double.NaN, 10, 10)];
        yield return [new PdfRectangle(0, 0, double.PositiveInfinity, 10)];
        yield return [new PdfRectangle(0, double.NegativeInfinity, 10, 10)];
        yield return [new PdfRectangle(-double.MaxValue, 0, double.MaxValue, 10)];
        yield return [new PdfRectangle(0, -double.MaxValue, 10, double.MaxValue)];
    }

    [Theory]
    [MemberData(nameof(InvalidBounds))]
    public void InvalidBoundsReportOriginalElementOrdinal(PdfRectangle invalid)
    {
        var nodes = new[] { Node("valid", 0, 20, 10, 10), (invalid, Node("invalid", 0, 0, 1, 1).Element) };
        var error = Assert.Throws<InvalidDataException>(() => PageReadingOrder.Order(nodes, default));
        Assert.Contains("element [1]", error.Message);
        Assert.Contains("invalid bounds", error.Message);
    }

    [Fact]
    public void InvalidBoundsRejectBeforeReadingRemainingInput()
    {
        var input = new ObservedList(2, i => i == 0
            ? Node("zero width", 0, 0, 0, 10)
            : throw new InvalidOperationException("Invalid first element must stop processing"));
        var error = Assert.Throws<InvalidDataException>(() => PageReadingOrder.Order(input, default));
        Assert.Contains("element [0]", error.Message);
        Assert.Equal(1, input.Reads);
    }

    [Fact]
    public void PreCancellationDoesNotReadInput()
    {
        var input = new ObservedList(1, _ => throw new InvalidOperationException("Must not read"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() => PageReadingOrder.Order(input, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, input.Reads);
        Assert.Equal(0, input.CountReads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CancellationDuringInputStopsBeforeNextElement(int cancelAtRead)
    {
        using var cancellation = new CancellationTokenSource();
        var input = new ObservedList(2, i =>
        {
            if (i + 1 == cancelAtRead)
            {
                cancellation.Cancel();
            }

            return Node($"node-{i}", i * 20, 0, 10, 10);
        });
        var error = Assert.Throws<OperationCanceledException>(() => PageReadingOrder.Order(input, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(cancelAtRead, input.Reads);
    }

    private static (PdfRectangle Bounds, ExtractionDocumentElement Element) Node(
        string text, double left, double bottom, double width, double height, ExtractionDocumentElement? element = null)
        => (new(left, bottom, left + width, bottom + height),
            element ?? new ExtractionDocumentTextElement(new TextContent(text), role: text));

    private static string Text(ExtractionDocumentElement element) => Assert.IsType<TextContent>(element.Content).Text;

    private static void AssertConserved(
        IReadOnlyList<(PdfRectangle Bounds, ExtractionDocumentElement Element)> input,
        IReadOnlyList<ExtractionDocumentElement> ordered)
    {
        Assert.Equal(input.Count, ordered.Count);
        Assert.Equal(input.Count, ordered.Distinct(ReferenceEqualityComparer.Instance).Count());
        foreach (var node in input)
        {
            Assert.Same(node.Element, Assert.Single(ordered, element => ReferenceEquals(element, node.Element)));
        }
    }

    private sealed class ObservedList(int count, Func<int, (PdfRectangle, ExtractionDocumentElement)> read)
        : IReadOnlyList<(PdfRectangle Bounds, ExtractionDocumentElement Element)>
    {
        internal int Reads { get; private set; }
        internal int CountReads { get; private set; }
        public int Count { get { CountReads++; return count; } }
        public (PdfRectangle Bounds, ExtractionDocumentElement Element) this[int index]
        {
            get { Reads++; return read(index); }
        }

        public IEnumerator<(PdfRectangle Bounds, ExtractionDocumentElement Element)> GetEnumerator()
            => throw new InvalidOperationException("Use indexed access");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
