// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Documents;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class PageReadingOrderRefinementTests
{
    [Fact]
    public void SmallDistantNarrowFooterFollowsAllThreeBodyColumns()
    {
        var footer = Node("f", 20, 20, 40, 30);
        var nodes = new[]
        {
            Node("right", 300, 200, 400, 240), footer,
            Node("middle", 160, 200, 260, 240), Node("left", 20, 200, 120, 240),
        };

        var result = PageReadingOrder.Order(nodes, default);

        Assert.Equal(new[] { "left", "middle", "right", "f" }, result.Select(Text));
        Assert.Same(footer.Element, result[^1]);
        AssertConserved(nodes, result);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void OrdinaryAlignedColumnLineGapsDoNotBecomeMarginBands(int columns)
    {
        var nodes = Enumerable.Range(0, 3).SelectMany(row => Enumerable.Range(0, columns)
            .Reverse().Select(column => Node($"{column}:{row}", 150 * column, 300 - 100 * row,
                150 * column + 80, 310 - 100 * row))).ToArray();

        var result = PageReadingOrder.Order(nodes, default);

        Assert.Equal(Enumerable.Range(0, columns).SelectMany(column => Enumerable.Range(0, 3)
            .Select(row => $"{column}:{row}")), result.Select(Text));
        AssertConserved(nodes, result);
    }

    [Theory]
    [InlineData(null, 10, "left,f,right")]
    [InlineData("footnote", 10, "left,right,f")]
    [InlineData("pageFooter", 10, "left,right,f")]
    [InlineData("footnote", 1, "left,f,right")]
    [InlineData("footnote", 1.001, "left,right,f")]
    [InlineData("pageFooter", 1, "left,f,right")]
    [InlineData("pageFooter", 1.001, "left,right,f")]
    public void FooterRoleRequiresActualGapButNotBodyBandSizedGap(string? role, double gap, string expected)
    {
        var footer = Node("f", 20, 90 - gap, 40, 100 - gap, role);
        var nodes = new[] { Node("right", 200, 100, 300, 140), footer, Node("left", 20, 100, 120, 140) };

        var result = PageReadingOrder.Order(nodes, default);

        Assert.Equal(expected.Split(','), result.Select(Text));
        Assert.Equal(role, Assert.IsType<ExtractionDocumentTextElement>(footer.Element).Role);
        Assert.Same(footer.Element, Assert.Single(result, element => ReferenceEquals(element, footer.Element)));
        AssertConserved(nodes, result);
    }

    [Theory]
    [InlineData(40, 10, "left,f,right")]
    [InlineData(40.001, 10, "left,right,f")]
    [InlineData(100, 40, "left,f,right")]
    [InlineData(100, 41, "left,f,right")]
    public void UnmarkedMarginRequiresStrictlyLargerGapAndStrictlySmallerHeight(
        double gap, double height, string expected)
    {
        var nodes = new[]
        {
            Node("right", 200, 200, 300, 240),
            Node("f", 20, 200 - gap - height, 40, 200 - gap),
            Node("left", 20, 200, 120, 240),
        };

        var result = PageReadingOrder.Order(nodes, default);

        Assert.Equal(expected.Split(','), result.Select(Text));
        AssertConserved(nodes, result);
    }

    [Fact]
    public void FooterMustBeSmallerThanEveryOtherNodeNotJustTallest()
    {
        var nodes = new[]
        {
            Node("right", 200, 230, 300, 240), Node("f", 20, 20, 40, 30),
            Node("left", 20, 200, 120, 240),
        };

        var result = PageReadingOrder.Order(nodes, default);

        Assert.Equal(new[] { "left", "f", "right" }, result.Select(Text));
        AssertConserved(nodes, result);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("table")]
    [InlineData("image")]
    public void StaggeredBridgeKeepsLeftBeforeRightWithoutSplittingAtomicPayload(string kind)
    {
        const string payload = "opaque fragment\nleft-looking words | right-looking words";
        ExtractionDocumentElement bridge = kind switch
        {
            "table" => new ExtractionDocumentTableElement(new TextContent(payload), 0, 0),
            "image" => new ExtractionDocumentImageElement { Text = payload },
            _ => new ExtractionDocumentTextElement(new TextContent(payload)),
        };
        var nodes = new[]
        {
            Node("right", 390, 320, 500, 370), (Bounds: new PdfRectangle(260, 340, 420, 380), Element: bridge),
            Node("left", 200, 300, 330, 350), Node("fullleft", 20, 260, 120, 300),
        };
        object? originalContent = bridge.Content;

        var result = PageReadingOrder.Order(nodes, default);

        Assert.Equal(new[] { "fullleft", payload, "left", "right" }, result.Select(Text));
        Assert.Same(bridge, result[1]);
        Assert.Same(originalContent, bridge.Content);
        Assert.Equal(payload, Text(bridge));
        if (bridge is ExtractionDocumentTableElement table)
        {
            Assert.Equal(0, table.Cells.RowCount);
            Assert.Equal(0, table.Cells.ColumnCount);
        }

        AssertConserved(nodes, result);
    }

    [Fact]
    public void OverlappingAmbiguityAndIdenticalBoundsKeepTopLeftAndOriginalOrdinal()
    {
        var nodes = new[]
        {
            Node("tie B", 10, 20, 110, 120), Node("right", 20, 20, 120, 120),
            Node("lower", 10, 10, 110, 110), Node("tie A", 10, 20, 110, 120),
            Node("higher", 30, 30, 130, 130),
        };
        Assert.Equal(new[] { "higher", "tie B", "tie A", "right", "lower" },
            PageReadingOrder.Order(nodes, default).Select(Text));
        (nodes[0], nodes[3]) = (nodes[3], nodes[0]);
        var result = PageReadingOrder.Order(nodes, default);
        Assert.Equal(new[] { "higher", "tie A", "tie B", "right", "lower" }, result.Select(Text));
        AssertConserved(nodes, result);
    }

    [Fact]
    public void CyclicSpatialConstraintsEmitReadyNodeThenStableUnresolvedRemainder()
    {
        // 0 -> 3 -> 2 -> 4 -> 0 forms a cycle; 1 is the only initial ready node.
        // 5 also remains blocked by 3. No page-wide X or Y whitespace separates them.
        var nodes = new[]
        {
            Node("0", 30, 80, 100, 110), Node("1", 60, 120, 100, 210),
            Node("2", 150, 160, 260, 170), Node("3", 110, 100, 130, 200),
            Node("4", 90, 120, 160, 140), Node("5", 150, 180, 230, 220),
        };
        var random = new Random(6047);
        for (int permutation = 0; permutation < 20; permutation++)
        {
            var input = nodes.OrderBy(_ => random.Next()).ToArray();
            var result = PageReadingOrder.Order(input, default);

            Assert.Equal(new[] { "1", "5", "3", "2", "4", "0" }, result.Select(Text));
            AssertConserved(input, result);
        }
    }

    [Fact]
    public void PermutationsPreserveShortFooterAndBridgedPayloadsSourcesAndInput()
    {
        var footer = Node(" \tf\n ", 20, 0, 35, 5);
        var nodes = new[]
        {
            Node("right", 390, 320, 500, 370), Node("bridge\nopaque", 260, 340, 420, 380),
            Node("left", 200, 300, 330, 350), Node("fullleft", 20, 260, 120, 300), footer,
        };
        var (frame, _) = ProviderAdapterHelpers.CreatePageFrame("refinement", "synthetic", 1, 500, 400,
            ExtractionDocumentCoordinateUnit.Point, ExtractionDocumentCoordinateOrigin.BottomLeft);
        foreach (var node in nodes)
        {
            node.Element.Sources.Add(new ExtractionDocumentSourceReference(frame,
                [new(node.Bounds.Left, node.Bounds.Bottom), new(node.Bounds.Right, node.Bounds.Bottom),
                    new(node.Bounds.Right, node.Bounds.Top), new(node.Bounds.Left, node.Bounds.Top)], new object()));
        }

        var originalPayloads = nodes.Select(node => node.Element.Content).ToArray();
        var originalTexts = nodes.Select(node => Text(node.Element)).ToArray();
        var originalSources = nodes.Select(node => node.Element.Sources.Single()).ToArray();
        var originalNatives = originalSources.Select(source => source.GetRawRepresentation()).ToArray();
        var originalPolygons = originalSources.Select(source => source.Polygon.Select(point => (point.X, point.Y)).ToArray()).ToArray();
        var random = new Random(91027);
        for (int permutation = 0; permutation < 24; permutation++)
        {
            var input = nodes.OrderBy(_ => random.Next()).ToArray();
            var inputBefore = input.ToArray();
            var result = PageReadingOrder.Order(input, default);

            Assert.Equal(new[] { "fullleft", "bridge\nopaque", "left", "right", " \tf\n " }, result.Select(Text));
            Assert.Equal(inputBefore, input);
            AssertConserved(input, result);
            for (int i = 0; i < nodes.Length; i++)
            {
                var element = Assert.Single(result, element => ReferenceEquals(element, nodes[i].Element));
                Assert.Same(originalPayloads[i], element.Content);
                Assert.Equal(originalTexts[i], Text(element));
                Assert.Same(originalSources[i], Assert.Single(element.Sources));
                Assert.Same(originalNatives[i], element.Sources[0].GetRawRepresentation());
                Assert.Same(frame, element.Sources[0].Page);
                Assert.Equal(originalPolygons[i], element.Sources[0].Polygon.Select(point => (point.X, point.Y)));
                Assert.Null(Assert.IsType<ExtractionDocumentTextElement>(element).Role);
            }
        }
    }

    private static (PdfRectangle Bounds, ExtractionDocumentElement Element) Node(
        string text, double left, double bottom, double right, double top, string? role = null)
        => (new(left, bottom, right, top), new ExtractionDocumentTextElement(new TextContent(text), role: role));

    private static string Text(ExtractionDocumentElement element) => element is ExtractionDocumentImageElement image
        ? image.Text!
        : Assert.IsType<TextContent>(element.Content).Text;

    private static void AssertConserved(
        IReadOnlyList<(PdfRectangle Bounds, ExtractionDocumentElement Element)> input,
        IReadOnlyList<ExtractionDocumentElement> result)
    {
        Assert.Equal(input.Count, result.Count);
        Assert.Equal(input.Count, result.Distinct(ReferenceEqualityComparer.Instance).Count());
        foreach (var node in input)
        {
            Assert.Same(node.Element, Assert.Single(result, element => ReferenceEquals(element, node.Element)));
        }
    }
}
