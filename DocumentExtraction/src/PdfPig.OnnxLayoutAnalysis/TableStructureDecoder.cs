// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

internal sealed record TableProposal(int Label, float Confidence, PdfRectangle Bounds);

internal sealed record TableTextToken(string Text, PdfRectangle Bounds, object Observation);

internal sealed record TableCellPrediction(
    int Row, int Column, int RowSpan, int ColumnSpan, string? Role,
    PdfRectangle Bounds, IReadOnlyList<TableTextToken> Tokens);

internal sealed record TableGrid(
    int Rows, int Columns, IReadOnlyList<TableCellPrediction> Cells, IReadOnlyList<TableTextToken> Unassigned);

/// <summary>Geometry-only adaptation of Microsoft's MIT-licensed Table Transformer postprocessing.</summary>
internal static class TableStructureDecoder
{
    private const int MaximumProposals = 125;
    private const int MaximumTokens = 16384;
    private const int MaximumBands = 128;
    private const int MaximumPositions = 16384;

    internal static TableGrid Build(
        IReadOnlyList<TableProposal> proposals, IReadOnlyList<TableTextToken> tokens,
        PdfRectangle tableBounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Throw.IfNull(proposals);
        Throw.IfNull(tokens);
        if (proposals.Count > MaximumProposals || tokens.Count > MaximumTokens)
        {
            throw new InvalidDataException("Table structure exceeds the proposal or token budget.");
        }

        ValidateBounds(tableBounds);
        int rowCount = 0;
        int columnCount = 0;
        foreach (var proposal in proposals)
        {
            token.ThrowIfCancellationRequested();
            if (proposal is null || proposal.Label is < 0 or > 5 ||
                !OnnxCompatibility.IsFinite(proposal.Confidence) || proposal.Confidence is < 0.5f or > 1)
            {
                throw new InvalidDataException("Invalid accepted table proposal.");
            }

            ValidateBounds(proposal.Bounds);
            rowCount += proposal.Label == 2 ? 1 : 0;
            columnCount += proposal.Label == 1 ? 1 : 0;
        }

        // Check before topology allocations, even though the proposal limit is currently tighter.
        if (rowCount > MaximumBands || columnCount > MaximumBands ||
            (long)rowCount * columnCount > MaximumPositions)
        {
            throw new InvalidDataException("Table structure exceeds the grid budget.");
        }

        foreach (var text in tokens)
        {
            token.ThrowIfCancellationRequested();
            if (text is null || text.Text is null || text.Observation is null)
            {
                throw new InvalidDataException("Invalid table text token.");
            }

            ValidateBounds(text.Bounds);
        }

        var ranked = proposals.Select((proposal, ordinal) => (Proposal: proposal, Ordinal: ordinal))
            .OrderByDescending(item => item.Proposal.Confidence).ThenBy(item => item.Ordinal).ToArray();
        var rows = RefineBands(2, 0.5);
        var columns = RefineBands(1, 0.25);
        if (rows.Count == 0 || columns.Count == 0)
        {
            return new(0, 0, Array.AsReadOnly(Array.Empty<TableCellPrediction>()),
                Array.AsReadOnly(tokens.ToArray()));
        }

        rows = rows.OrderByDescending(bounds => bounds.Top).ThenBy(bounds => bounds.Bottom).ToList();
        columns = columns.OrderBy(bounds => bounds.Left).ThenBy(bounds => bounds.Right).ToList();
        double left = columns.Min(bounds => bounds.Left);
        double right = columns.Max(bounds => bounds.Right);
        double bottom = rows.Min(bounds => bounds.Bottom);
        double top = rows.Max(bounds => bounds.Top);
        double[] x = new double[columns.Count + 1];
        double[] y = new double[rows.Count + 1];
        x[0] = left;
        x[^1] = right;
        y[0] = top;
        y[^1] = bottom;
        for (int i = 1; i < columns.Count; i++)
        {
            x[i] = Midpoint(columns[i - 1].Right, columns[i].Left);
        }

        for (int i = 1; i < rows.Count; i++)
        {
            y[i] = Midpoint(rows[i - 1].Bottom, rows[i].Top);
        }

        // Contradictory bands cannot be repaired by inventing or dropping a model row/column.
        for (int i = 1; i < x.Length; i++)
        {
            if (x[i] <= x[i - 1])
            {
                throw new InvalidDataException("Table columns cannot form a positive partition.");
            }
        }

        for (int i = 1; i < y.Length; i++)
        {
            if (y[i] >= y[i - 1])
            {
                throw new InvalidDataException("Table rows cannot form a positive partition.");
            }
        }

        int count = rows.Count * columns.Count;
        var owners = Enumerable.Repeat(-1, count).ToArray();
        var cells = new List<TableCellPrediction>(count);
        foreach (var item in ranked)
        {
            token.ThrowIfCancellationRequested();
            if (item.Proposal.Label is not (4 or 5))
            {
                continue;
            }

            PdfRectangle bounds = item.Proposal.Bounds;
            var coveredRows = Enumerable.Range(0, rows.Count)
                .Where(row => Overlap(y[row + 1], y[row], bounds.Bottom, bounds.Top) /
                    (y[row] - y[row + 1]) >= 0.5).ToArray();
            var coveredColumns = Enumerable.Range(0, columns.Count)
                .Where(column => Overlap(x[column], x[column + 1], bounds.Left, bounds.Right) /
                    (x[column + 1] - x[column]) >= 0.5).ToArray();
            if (!Contiguous(coveredRows) || !Contiguous(coveredColumns))
            {
                continue;
            }

            // Lower-priority intersecting spans are excluded, not trimmed into unobserved spans.
            if (coveredRows.Any(row => coveredColumns.Any(column => owners[row * columns.Count + column] >= 0)))
            {
                continue;
            }

            AddCell(coveredRows[0], coveredColumns[0], coveredRows.Length, coveredColumns.Length,
                item.Proposal.Label == 4 ? "rowHeader" : null);
        }

        for (int row = 0; row < rows.Count; row++)
        {
            token.ThrowIfCancellationRequested();
            for (int column = 0; column < columns.Count; column++)
            {
                if (owners[row * columns.Count + column] < 0)
                {
                    AddCell(row, column, 1, 1, null);
                }
            }
        }

        cells = cells.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).ToList();
        var assigned = new List<(TableTextToken Token, int Ordinal)>?[cells.Count];
        var unassigned = new List<TableTextToken>();
        for (int ordinal = 0; ordinal < tokens.Count; ordinal++)
        {
            token.ThrowIfCancellationRequested();
            TableTextToken text = tokens[ordinal];
            int best = -1;
            double bestOverlap = 0;
            bool tied = false;
            for (int i = 0; i < cells.Count; i++)
            {
                if ((i & 255) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }

                double overlap = Fraction(text.Bounds, cells[i].Bounds);
                if (overlap > bestOverlap)
                {
                    best = i;
                    bestOverlap = overlap;
                    tied = false;
                }
                else if (overlap == bestOverlap)
                {
                    tied = true;
                }
            }

            if (bestOverlap >= 0.5 && !tied)
            {
                (assigned[best] ??= []).Add((text, ordinal));
            }
            else
            {
                unassigned.Add(text);
            }
        }

        for (int i = 0; i < cells.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            TableTextToken[] contents = assigned[i]?.OrderByDescending(item => item.Token.Bounds.Top)
                .ThenBy(item => item.Token.Bounds.Left).ThenBy(item => item.Ordinal)
                .Select(item => item.Token).ToArray() ?? [];
            cells[i] = cells[i] with { Tokens = Array.AsReadOnly(contents) };
        }

        token.ThrowIfCancellationRequested();
        return new(rows.Count, columns.Count, Array.AsReadOnly(cells.ToArray()),
            Array.AsReadOnly(unassigned.ToArray()));

        List<PdfRectangle> RefineBands(int label, double threshold)
        {
            var surviving = new List<PdfRectangle>();
            foreach (var item in ranked)
            {
                token.ThrowIfCancellationRequested();
                if (item.Proposal.Label != label || Fraction(item.Proposal.Bounds, tableBounds) == 0)
                {
                    continue;
                }

                var bounds = new PdfRectangle(
                    Math.Max(item.Proposal.Bounds.Left, tableBounds.Left),
                    Math.Max(item.Proposal.Bounds.Bottom, tableBounds.Bottom),
                    Math.Min(item.Proposal.Bounds.Right, tableBounds.Right),
                    Math.Min(item.Proposal.Bounds.Top, tableBounds.Top));
                if (!surviving.Any(other => Fraction(bounds, other) >= threshold))
                {
                    surviving.Add(bounds);
                }
            }

            // Deliberately omit upstream remove_objects_without_content and token-only NMS:
            // missing text (or a coarse OCR LINE across bands) does not disprove a model band.
            return surviving;
        }

        void AddCell(int row, int column, int rowSpan, int columnSpan, string? role)
        {
            var bounds = new PdfRectangle(x[column], y[row + rowSpan], x[column + columnSpan], y[row]);
            if (role is null && ranked.Any(item => item.Proposal.Label == 3 &&
                Fraction(bounds, item.Proposal.Bounds) >= 0.5))
            {
                role = "columnHeader";
            }

            int owner = cells.Count;
            cells.Add(new(row, column, rowSpan, columnSpan, role, bounds, Array.Empty<TableTextToken>()));
            for (int r = row; r < row + rowSpan; r++)
            {
                for (int c = column; c < column + columnSpan; c++)
                {
                    owners[r * columns.Count + c] = owner;
                }
            }
        }
    }

    private static bool Contiguous(int[] indices) =>
        indices.Length > 0 && indices[^1] - indices[0] + 1 == indices.Length;

    private static double Midpoint(double first, double second) => first / 2 + second / 2;

    private static double Overlap(double firstLow, double firstHigh, double secondLow, double secondHigh) =>
        Math.Max(0, Math.Min(firstHigh, secondHigh) - Math.Max(firstLow, secondLow));

    private static double Fraction(PdfRectangle subject, PdfRectangle container) =>
        (Overlap(subject.Left, subject.Right, container.Left, container.Right) / subject.Width) *
        (Overlap(subject.Bottom, subject.Top, container.Bottom, container.Top) / subject.Height);

    private static void ValidateBounds(PdfRectangle bounds)
    {
        if (!OnnxCompatibility.IsFinite(bounds.Left) || !OnnxCompatibility.IsFinite(bounds.Right) ||
            !OnnxCompatibility.IsFinite(bounds.Bottom) || !OnnxCompatibility.IsFinite(bounds.Top) ||
            !OnnxCompatibility.IsFinite(bounds.Width) || !OnnxCompatibility.IsFinite(bounds.Height) ||
            bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidDataException("Table geometry must have finite, positive bounds.");
        }
    }
}
