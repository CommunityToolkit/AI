// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using UglyToad.PdfPig.Core;

namespace CommunityToolkit.DocumentExtraction.PdfPig.OnnxLayoutAnalysis;

/// <summary>Distinguishes unobserved table text from an actual empty decode and actual recognized text. None certifies a visual blank.</summary>
public enum PdfPigTableTextStatus
{
    /// <summary>No native word or OCR region was assigned to this estimated cell.</summary>
    Unobserved,
    /// <summary>Assigned OCR observations decoded to no text; this does not certify a blank cell.</summary>
    NoRecognizedText,
    /// <summary>At least one actual native word or OCR observation contains text.</summary>
    Recognized,
}

/// <summary>A detached actual TATR query, including rejected/no-object queries and the original logits/box.</summary>
public sealed class PdfPigTableProposal
{
    internal PdfPigTableProposal(int queryIndex, string label, float confidence, float[] box, float[] logits, PdfRectangle? bounds)
    {
        QueryIndex = queryIndex;
        Label = label;
        Confidence = confidence;
        NormalizedBox = Array.AsReadOnly(box.ToArray());
        Logits = Array.AsReadOnly(logits.ToArray());
        Bounds = bounds;
    }

    /// <summary>Gets the original model query index.</summary>
    public int QueryIndex { get; }
    /// <summary>Gets the highest-probability model class, including no object.</summary>
    public string Label { get; }
    /// <summary>Gets the class probability after softmax over all seven classes.</summary>
    public float Confidence { get; }
    /// <summary>Gets the original normalized center-X, center-Y, width and height.</summary>
    public IReadOnlyList<float> NormalizedBox { get; }
    /// <summary>Gets all seven original class logits.</summary>
    public IReadOnlyList<float> Logits { get; }
    /// <summary>Gets the crop-clipped box in PDF display points, or null if outside the crop.</summary>
    public PdfRectangle? Bounds { get; }
}

/// <summary>A derived model-grid anchor cell and its actual atomic text observations, not measured glyph geometry.</summary>
public sealed class PdfPigTableCellObservation
{
    internal PdfPigTableCellObservation(TableCellPrediction cell)
    {
        RowIndex = cell.Row;
        ColumnIndex = cell.Column;
        RowSpan = cell.RowSpan;
        ColumnSpan = cell.ColumnSpan;
        Role = cell.Role;
        Bounds = cell.Bounds;
        Observations = Array.AsReadOnly(cell.Tokens.Select(token => token.Observation).ToArray());
        Text = cell.Tokens.Count == 0 ? null : string.Join(" ", cell.Tokens.Where(token => token.Text.Length != 0).Select(token => token.Text));
        TextStatus = Text is null ? PdfPigTableTextStatus.Unobserved :
            Text.Length == 0 ? PdfPigTableTextStatus.NoRecognizedText : PdfPigTableTextStatus.Recognized;
    }

    /// <summary>Gets the zero-based anchor row.</summary>
    public int RowIndex { get; }
    /// <summary>Gets the zero-based anchor column.</summary>
    public int ColumnIndex { get; }
    /// <summary>Gets the number of covered rows.</summary>
    public int RowSpan { get; }
    /// <summary>Gets the number of covered columns.</summary>
    public int ColumnSpan { get; }
    /// <summary>Gets the derived columnHeader/rowHeader role, if observed.</summary>
    public string? Role { get; }
    /// <summary>Gets estimated grid bounds in PDF display points; these are not glyph or word measurements.</summary>
    public PdfRectangle Bounds { get; }
    /// <summary>Gets assigned literal text, null for unobserved, or empty for an actual empty recognition.</summary>
    public string? Text { get; }
    /// <summary>Gets the explicit text-evidence state, without asserting a cell is visually blank.</summary>
    public PdfPigTableTextStatus TextStatus { get; }
    /// <summary>Gets genuine PdfPig words or actual OCR regions assigned to this cell, each once.</summary>
    public IReadOnlyList<object> Observations { get; }
}

/// <summary>Detached actual table proposals, derived bounded topology and unassigned text evidence.</summary>
public sealed class PdfPigTableStructure
{
    internal PdfPigTableStructure(PdfPigLayoutRegion region, IEnumerable<PdfPigTableProposal> proposals, TableGrid grid)
    {
        HeronRegion = region;
        Proposals = Array.AsReadOnly(proposals.ToArray());
        RowCount = grid.Rows;
        ColumnCount = grid.Columns;
        Cells = Array.AsReadOnly(grid.Cells.Select(cell => new PdfPigTableCellObservation(cell)).ToArray());
        UnassignedObservations = Array.AsReadOnly(grid.Unassigned.Select(token => token.Observation).ToArray());
        UnassignedText = Array.AsReadOnly(grid.Unassigned.Select(token => token.Text).ToArray());
    }

    /// <summary>Gets the unchanged native observation for the selected Heron table region.</summary>
    public PdfPigLayoutRegion HeronRegion { get; }
    /// <summary>Gets all original TATR queries, not just accepted cell-like proposals.</summary>
    public IReadOnlyList<PdfPigTableProposal> Proposals { get; }
    /// <summary>Gets whether both rows and columns were observed; false does not imply the source has no table.</summary>
    public bool HasStructure => RowCount > 0 && ColumnCount > 0;
    /// <summary>Gets the count of surviving observed rows.</summary>
    public int RowCount { get; }
    /// <summary>Gets the count of surviving observed columns.</summary>
    public int ColumnCount { get; }
    /// <summary>Gets derived non-overlapping anchor cells, including unobserved/empty-recognition cells.</summary>
    public IReadOnlyList<PdfPigTableCellObservation> Cells { get; }
    /// <summary>Gets actual tokens not uniquely assigned to cells, without splitting OCR lines into invented words.</summary>
    public IReadOnlyList<object> UnassignedObservations { get; }
    /// <summary>Gets corresponding unassigned literal text, also retained once in document content.</summary>
    public IReadOnlyList<string> UnassignedText { get; }
}
