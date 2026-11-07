// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis.UnitTests;

public sealed class TableStructureDecoderTests
{
    private static readonly PdfRectangle Table = new(0, 0, 90, 90);

    [Fact]
    public void ObservedGridPreservesHeadersSpansEmptyCellsAndSourceReferences()
    {
        TableProposal[] proposals =
        [
            .. Grid(3, 3),
            Proposal(3, 0, 60, 90, 90),
            Proposal(5, 0, 60, 60, 90),
            Proposal(4, 0, 30, 90, 60),
        ];
        TableTextToken[] tokens =
        [
            Text("Heading", 5, 70, 50, 80),
            Text("Group", 5, 40, 85, 50),
            Text("0", 5, 10, 15, 20),
        ];

        var result = Build(proposals, tokens);

        Assert.Equal(3, result.Rows);
        Assert.Equal(3, result.Columns);
        Assert.Equal(new[]
        {
            (0, 0, 1, 2, "columnHeader"), (0, 2, 1, 1, "columnHeader"),
            (1, 0, 1, 3, "rowHeader"), (2, 0, 1, 1, (string?)null),
            (2, 1, 1, 1, (string?)null), (2, 2, 1, 1, (string?)null),
        }, result.Cells.Select(cell => (cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan, cell.Role)));
        Assert.Equal(new PdfRectangle(0, 60, 60, 90), result.Cells[0].Bounds);
        Assert.Equal(new PdfRectangle(0, 30, 90, 60), result.Cells[2].Bounds);
        Assert.Equal("0", Assert.Single(result.Cells[3].Tokens).Text);
        Assert.Null(result.Cells[4].Role);
        Assert.Empty(result.Cells[4].Tokens);
        Assert.Empty(result.Cells[5].Tokens);
        Assert.Empty(result.Unassigned);
        AssertConserved(tokens, result);
        AssertPartition(result);
    }

    [Fact]
    public void ZeroAndEmptyLiteralAreNotMissingAndTokensAreNotFabricated()
    {
        var zero = Text("0", 1, 1, 10, 10);
        var empty = Text("", 11, 1, 20, 10);

        var result = Build(Grid(1, 2), [zero, empty]);

        Assert.Equal(new[] { "0", "" }, result.Cells[0].Tokens.Select(item => item.Text));
        Assert.Same(zero, result.Cells[0].Tokens[0]);
        Assert.Same(empty, result.Cells[0].Tokens[1]);
        Assert.Empty(result.Cells[1].Tokens);
        Assert.Null(result.Cells[1].Role);
        AssertConserved([zero, empty], result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingObservedAxisReturnsNoGridAndAllTokens(int axis)
    {
        var input = new[] { Text("duplicate", 1, 1, 10, 10), Text("duplicate", 11, 1, 20, 10) };
        TableProposal[] proposals = axis == 0 ? [] : [Proposal(axis, 0, 0, 90, 90)];

        var result = Build(proposals, input);

        Assert.Equal(0, result.Rows);
        Assert.Equal(0, result.Columns);
        Assert.Empty(result.Cells);
        Assert.Equal(input, result.Unassigned);
        AssertConserved(input, result);
    }

    [Fact]
    public void AtomicLineTiedAcrossColumnsRemainsUnassignedButUniqueHalfOverlapAssigns()
    {
        var ambiguous = Text("left and right", 0, 10, 90, 20);
        var unique = Text("half inside", -10, 30, 10, 40);
        var outside = Text("outside", 100, 10, 110, 20);
        var lessThanHalf = Text("too little", -30, 50, 10, 60);
        TableTextToken[] tokens = [ambiguous, unique, outside, lessThanHalf];

        var result = Build(Grid(1, 2), tokens);

        Assert.Same(unique, Assert.Single(result.Cells[0].Tokens));
        Assert.Empty(result.Cells[1].Tokens);
        Assert.Equal(new[] { ambiguous, outside, lessThanHalf }, result.Unassigned);
        AssertConserved(tokens, result);
    }

    [Fact]
    public void BlankBandsSurviveEvenWithOnlyOneObservedToken()
    {
        var input = new[] { Text("only observation", 1, 65, 20, 75) };

        var result = Build(Grid(3, 3), input);

        Assert.Equal(3, result.Rows);
        Assert.Equal(3, result.Columns);
        Assert.Equal(9, result.Cells.Count);
        Assert.Equal(8, result.Cells.Count(cell => cell.Tokens.Count == 0));
        AssertConserved(input, result);
        AssertPartition(result);
    }

    [Theory]
    [InlineData(1, 0.25)]
    [InlineData(2, 0.5)]
    public void GeometricNmsUsesLowerRankedObjectOverlapAndStableOrdinal(int label, double threshold)
    {
        var higher = Proposal(label, 0, 0, 90, 90, 0.9f);
        double low = 90 * (1 - threshold);
        var lower = label == 1
            ? Proposal(label, low, 0, low + 90, 90, 0.8f)
            : Proposal(label, 0, low, 90, low + 90, 0.8f);
        var otherAxis = Proposal(label == 1 ? 2 : 1, 0, 0, 180, 180);
        var largeBounds = new PdfRectangle(0, 0, 180, 180);

        var result = TableStructureDecoder.Build([lower, otherAxis, higher], [], largeBounds, default);
        var equalScores = TableStructureDecoder.Build(
            [lower with { Confidence = 0.9f }, higher, otherAxis], [], largeBounds, default);

        Assert.Equal(1, result.Rows);
        Assert.Equal(1, result.Columns);
        Assert.Equal(new PdfRectangle(0, 0, label == 1 ? 90 : 180, label == 2 ? 90 : 180),
            Assert.Single(result.Cells).Bounds);
        Assert.Equal(label == 1 ? low : 0, Assert.Single(equalScores.Cells).Bounds.Left);
        Assert.Equal(label == 2 ? low : 0, Assert.Single(equalScores.Cells).Bounds.Bottom);
    }

    [Fact]
    public void DuplicateProposalsDoNotDuplicateBandsOrSpanAnchors()
    {
        var span = Proposal(5, 0, 60, 60, 90);
        TableProposal[] proposals = [.. Grid(3, 3), .. Grid(3, 3), span, span];

        var result = Build(proposals, []);

        Assert.Equal(3, result.Rows);
        Assert.Equal(3, result.Columns);
        Assert.Equal(8, result.Cells.Count);
        Assert.Equal(2, result.Cells[0].ColumnSpan);
        AssertPartition(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingSpansKeepScoreThenOrdinalWinnerWithoutTrimming(bool tie)
    {
        var first = Proposal(5, 0, 30, 60, 90, tie ? 0.9f : 0.8f);
        var second = Proposal(4, 30, 0, 90, 60, 0.9f);

        var result = Build([.. Grid(3, 3), first, second], []);

        var merged = Assert.Single(result.Cells, cell => cell.RowSpan > 1 || cell.ColumnSpan > 1);
        Assert.Equal(tie ? (0, 0, 2, 2) : (1, 1, 2, 2),
            (merged.Row, merged.Column, merged.RowSpan, merged.ColumnSpan));
        Assert.Equal(tie ? null : "rowHeader", merged.Role);
        Assert.Equal(6, result.Cells.Count);
        AssertPartition(result);
    }

    [Fact]
    public void SingleUnitProjectedHeaderRetainsRoleAndUnobservedSpansRetainTopology()
    {
        var result = Build([.. Grid(2, 2), Proposal(4, 0, 0, 45, 45),
            Proposal(5, 0, 45, 90, 90)], []);

        Assert.Equal(3, result.Cells.Count);
        Assert.Equal(2, result.Cells[0].ColumnSpan);
        Assert.Equal("rowHeader", result.Cells[1].Role);
        Assert.All(result.Cells, cell => Assert.Empty(cell.Tokens));
        AssertPartition(result);
    }

    [Fact]
    public void HeaderEvidenceDoesNotInventEarlierHeadersOrTableProposalCounts()
    {
        var result = Build([.. Grid(3, 2), Proposal(0, 0, 0, 90, 90, 1),
            Proposal(3, 0, 0, 90, 30, 0.5f),
            Proposal(5, 100, 100, 110, 110)], []);

        Assert.Equal(3, result.Rows);
        Assert.Equal(2, result.Columns);
        Assert.Equal(6, result.Cells.Count);
        Assert.All(result.Cells.Take(4), cell => Assert.Null(cell.Role));
        Assert.All(result.Cells.Skip(4), cell => Assert.Equal("columnHeader", cell.Role));
        AssertPartition(result);
    }

    [Fact]
    public void BandsAlignToSurvivingExtentsWithContiguousMidpointBoundaries()
    {
        TableProposal[] proposals =
        [
            Proposal(2, 0, 55, 85, 90), Proposal(2, 5, 5, 90, 45),
            Proposal(1, 10, 0, 55, 80), Proposal(1, 50, 10, 80, 90),
        ];

        var result = Build(proposals, []);

        Assert.Equal(new[]
        {
            new PdfRectangle(10, 50, 52.5, 90), new PdfRectangle(52.5, 50, 80, 90),
            new PdfRectangle(10, 5, 52.5, 50), new PdfRectangle(52.5, 5, 80, 50),
        }, result.Cells.Select(cell => cell.Bounds));
        AssertPartition(result);
    }

    [Fact]
    public void ProposalsOutsideTableAreExcludedAndCrossingBandsAreClipped()
    {
        var result = Build(
            [Proposal(2, -10, -10, 100, 100), Proposal(1, -10, -10, 100, 100),
                Proposal(2, 100, 100, 110, 110), Proposal(1, 100, 100, 110, 110)], []);

        Assert.Equal(Table, Assert.Single(result.Cells).Bounds);
    }

    [Fact]
    public void GeneratedPermutationsConserveEveryOccurrenceAndDeterministicTopology()
    {
        var random = new Random(7103);
        var duplicate = Text("same", 1, 1, 10, 10);
        TableTextToken[] tokens =
        [
            duplicate, duplicate, Text("same", 50, 1, 60, 10),
            Text("line", 0, 70, 90, 80), Text("outside", 100, 0, 110, 10),
            Text("top", 1, 80, 10, 85), Text("left", 1, 40, 10, 50),
        ];
        TableProposal[] proposals = [.. Grid(3, 3), Proposal(5, 0, 0, 60, 30)];
        var baseline = Build(proposals, tokens);
        for (int permutation = 0; permutation < 32; permutation++)
        {
            var shuffledTokens = tokens.OrderBy(_ => random.Next()).ToArray();
            var result = Build(proposals.OrderBy(_ => random.Next()).ToArray(), shuffledTokens);

            Assert.Equal(baseline.Cells.Select(Shape), result.Cells.Select(Shape));
            Assert.Equal(baseline.Cells.Select(cell => cell.Tokens.Count), result.Cells.Select(cell => cell.Tokens.Count));
            AssertConserved(shuffledTokens, result);
            AssertPartition(result);
        }
    }

    [Fact]
    public void CellTokenOrderUsesTopThenLeftThenOriginalOrdinalAndListsAreDetachedReadOnly()
    {
        var first = Text("first tied", 10, 60, 20, 70);
        var second = Text("second tied", 10, 60, 20, 70);
        var left = Text("left", 1, 60, 9, 70);
        var top = Text("top", 20, 80, 30, 85);
        var low = Text("low", 1, 1, 9, 10);
        var outside = Text("outside", 100, 0, 110, 10);
        var tokens = new List<TableTextToken> { low, first, second, top, left, outside };
        var result = Build(Grid(1, 1), tokens);
        tokens.Clear();

        Assert.Equal(new[] { top, left, first, second, low }, result.Cells[0].Tokens);
        Assert.Same(outside, Assert.Single(result.Unassigned));
        Assert.Throws<NotSupportedException>(() => ((IList)result.Cells).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)result.Cells[0].Tokens).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList)result.Unassigned).Clear());
    }

    [Fact]
    public void MissingStructureAlsoReturnsDetachedReadOnlyTokenSnapshot()
    {
        var text = Text("unobserved", 0, 0, 10, 10);
        var tokens = new List<TableTextToken> { text };
        var result = Build([], tokens);
        tokens.Clear();

        Assert.Same(text, Assert.Single(result.Unassigned));
        Assert.Throws<NotSupportedException>(() => ((IList)result.Unassigned).Clear());
    }

    [Theory]
    [InlineData(-1, 0.9f)]
    [InlineData(6, 0.9f)]
    [InlineData(1, 0.49f)]
    [InlineData(1, 1.01f)]
    [InlineData(1, float.NaN)]
    [InlineData(1, float.PositiveInfinity)]
    public void MalformedLabelsAndConfidencesAreRejected(int label, float confidence)
    {
        Assert.Throws<InvalidDataException>(() => Build([new(label, confidence, Table)], []));
    }

    [Fact]
    public void MalformedGeometryAndNullPayloadsAreRejectedEvenWithoutAStructure()
    {
        PdfRectangle[] invalid =
        [
            new(0, 0, 0, 90), new(0, 0, 90, 0),
            new(0, 0, double.PositiveInfinity, 90), new(double.NaN, 0, 90, 90),
            new(-double.MaxValue, 0, double.MaxValue, 90),
        ];
        foreach (var bounds in invalid)
        {
            Assert.Throws<InvalidDataException>(() => TableStructureDecoder.Build([], [], bounds, default));
            Assert.Throws<InvalidDataException>(() => Build([new(1, 0.9f, bounds)], []));
            Assert.Throws<InvalidDataException>(() => Build([], [new("bad", bounds, new object())]));
        }

        Assert.Throws<InvalidDataException>(() => Build([null!], []));
        Assert.Throws<InvalidDataException>(() => Build([], [null!]));
        Assert.Throws<InvalidDataException>(() => Build([], [new(null!, Table, new object())]));
        Assert.Throws<InvalidDataException>(() => Build([], [new("text", Table, null!)]));
        Assert.Throws<ArgumentNullException>(() => Build(null!, []));
        Assert.Throws<ArgumentNullException>(() => Build([], null!));
    }

    [Fact]
    public void BudgetsAreCheckedBeforeEnumerationAndMaximumAcceptedInputsAreConserved()
    {
        Assert.Throws<InvalidDataException>(() => Build(new CountOnlyList<TableProposal>(126), []));
        Assert.Throws<InvalidDataException>(() => Build([], new CountOnlyList<TableTextToken>(16385)));
        var repeated = Text("0", 1, 1, 2, 2);
        var tokens = Enumerable.Repeat(repeated, 16384).ToArray();
        var result = Build(Enumerable.Repeat(Proposal(1, 0, 0, 90, 90), 124)
            .Append(Proposal(2, 0, 0, 90, 90)).ToArray(), tokens);

        Assert.Equal(16384, Assert.Single(result.Cells).Tokens.Count);
        AssertConserved(tokens, result);
    }

    [Fact]
    public void LargestObservedGridWithinProposalBudgetHasNoHoles()
    {
        var result = Build(Grid(62, 63), []);

        Assert.Equal(62, result.Rows);
        Assert.Equal(63, result.Columns);
        Assert.Equal(3906, result.Cells.Count);
        AssertPartition(result);
    }

    [Fact]
    public void CancellationPrecedesValidationAndIsObservedDuringInputProcessing()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var exception = Assert.Throws<OperationCanceledException>(() =>
            TableStructureDecoder.Build(null!, null!, default, canceled.Token));
        Assert.Equal(canceled.Token, exception.CancellationToken);

        using var during = new CancellationTokenSource();
        var tokens = new CancelingTokens(during);
        var duringException = Assert.Throws<OperationCanceledException>(() =>
            TableStructureDecoder.Build(Grid(1, 1), tokens, Table, during.Token));
        Assert.Equal(during.Token, duringException.CancellationToken);
    }

    private static TableGrid Build(IReadOnlyList<TableProposal> proposals, IReadOnlyList<TableTextToken> tokens) =>
        TableStructureDecoder.Build(proposals, tokens, Table, default);

    private static TableProposal Proposal(int label, double left, double bottom, double right, double top,
        float confidence = 0.9f) => new(label, confidence, new(left, bottom, right, top));

    private static TableTextToken Text(string text, double left, double bottom, double right, double top) =>
        new(text, new(left, bottom, right, top), new object());

    private static TableProposal[] Grid(int rows, int columns) =>
        Enumerable.Range(0, rows).Select(row => Proposal(2, 0, row * 90.0 / rows, 90, (row + 1) * 90.0 / rows))
            .Concat(Enumerable.Range(0, columns).Select(column =>
                Proposal(1, column * 90.0 / columns, 0, (column + 1) * 90.0 / columns, 90))).ToArray();

    private static (int, int, int, int, string?, PdfRectangle) Shape(TableCellPrediction cell) =>
        (cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan, cell.Role, cell.Bounds);

    private static void AssertConserved(IReadOnlyList<TableTextToken> input, TableGrid result)
    {
        var output = result.Cells.SelectMany(cell => cell.Tokens).Concat(result.Unassigned).ToArray();
        Assert.Equal(input.Count, output.Length);
        foreach (var text in input.Distinct(ReferenceEqualityComparer.Instance))
        {
            Assert.Equal(input.Count(item => ReferenceEquals(item, text)),
                output.Count(item => ReferenceEquals(item, text)));
        }

        Assert.All(output, text =>
            Assert.Contains(input, item => ReferenceEquals(item, text) && ReferenceEquals(item.Observation, text.Observation)));
    }

    private static void AssertPartition(TableGrid grid)
    {
        var coverage = new int[grid.Rows, grid.Columns];
        foreach (var cell in grid.Cells)
        {
            Assert.True(cell.Bounds.Width > 0 && cell.Bounds.Height > 0);
            Assert.InRange(cell.RowSpan, 1, grid.Rows - cell.Row);
            Assert.InRange(cell.ColumnSpan, 1, grid.Columns - cell.Column);
            for (int row = cell.Row; row < cell.Row + cell.RowSpan; row++)
            {
                for (int column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                {
                    coverage[row, column]++;
                }
            }
        }

        Assert.All(coverage.Cast<int>(), count => Assert.Equal(1, count));
        Assert.Equal(grid.Cells.Count, grid.Cells.Select(cell => (cell.Row, cell.Column)).Distinct().Count());
    }

    private sealed class CountOnlyList<T>(int count) : IReadOnlyList<T>
    {
        public int Count => count;
        public T this[int index] => throw new InvalidOperationException("Budget should be checked first.");
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("Budget should be checked first.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CancelingTokens(CancellationTokenSource source) : IReadOnlyList<TableTextToken>
    {
        public int Count => 1;
        public TableTextToken this[int index] => Text("canceled", 0, 0, 10, 10);
        public IEnumerator<TableTextToken> GetEnumerator()
        {
            source.Cancel();
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
